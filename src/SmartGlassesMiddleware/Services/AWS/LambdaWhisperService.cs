using System.Text;
using System.Text.Json;
using Amazon.Lambda;
using Amazon.Lambda.Model;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.EntityFrameworkCore;
using SmartGlassesMiddleware.Data;

namespace SmartGlassesMiddleware.Services;

/// <summary>
/// Transcription service that invokes a Whisper Lambda (container) on AWS.
/// Expects the Lambda to accept a JSON payload: { "audio_base64": "..." }
/// and return JSON: { "transcript": "..." }.
/// </summary>
public class LambdaWhisperService : ITranscriptionService
{
    private readonly IAmazonS3 _s3Client;
    private readonly IAmazonSQS _sqsClient;
    private readonly IAmazonLambda _lambdaClient;
    private readonly IConnectionMultiplexer _redis;
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<LambdaWhisperService> _logger;
    private readonly string _queueUrl;
    private readonly string _tempBucket;
    private readonly TimeSpan _timeout;
    private readonly long _syncThresholdBytes;
    private readonly string? _lambdaFunctionName;

    public LambdaWhisperService(
        IAmazonS3 s3Client,
        IAmazonSQS sqsClient,
        IAmazonLambda lambdaClient,
        IConnectionMultiplexer redis,
        ApplicationDbContext dbContext,
        IConfiguration configuration,
        ILogger<LambdaWhisperService> logger)
    {
        _s3Client = s3Client;
        _sqsClient = sqsClient;
        _lambdaClient = lambdaClient;
        _redis = redis;
        _dbContext = dbContext;
        _logger = logger;
        _queueUrl = configuration["AWS:Transcription:QueueUrl"] ?? throw new InvalidOperationException("AWS:Transcription:QueueUrl is required");
        _tempBucket = configuration["AWS:S3:MediaBucket"] ?? throw new InvalidOperationException("AWS:S3:MediaBucket is required for Whisper Lambda uploads");
        var timeoutSeconds = configuration.GetValue<int?>("AWS:Transcription:TranscriptionTimeoutSeconds") ?? 300;
        _timeout = TimeSpan.FromSeconds(timeoutSeconds);
        _syncThresholdBytes = configuration.GetValue<long?>("AWS:Transcription:SyncThresholdBytes") ?? 200000; // default 200KB
        _lambdaFunctionName = configuration["AWS:Transcription:WhisperLambdaFunction"];
    }

    public async Task<string> TranscribeAudioAsync(Stream audioStream, CancellationToken cancellationToken = default)
    {
        // Buffer the incoming stream so we can inspect size and reuse for upload
        await using var buffered = new MemoryStream();
        await audioStream.CopyToAsync(buffered, cancellationToken);
        var size = buffered.Length;
        buffered.Position = 0;

        var s3Key = $"transcribe-uploads/{Guid.NewGuid()}.wav";
        var jobId = Guid.NewGuid().ToString();
        try
        {
            _logger.LogInformation("Uploading audio to S3 bucket {Bucket} key {Key}", _tempBucket, s3Key);
            // Upload audio stream to S3
            await _s3Client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _tempBucket,
                Key = s3Key,
                InputStream = buffered,
                ContentType = "audio/wav"
            }, cancellationToken);

            // Create DB transcription job record
            var dbJob = new SmartGlassesMiddleware.Models.TranscriptionJob
            {
                Id = Guid.NewGuid(),
                JobId = jobId,
                S3Bucket = _tempBucket,
                S3Key = s3Key,
                Status = "queued",
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.TranscriptionJobs.Add(dbJob);
            await _dbContext.SaveChangesAsync(cancellationToken);

            // Set initial job status in Redis
            try
            {
                var db = _redis.GetDatabase();
                await db.StringSetAsync($"transcribe:job:{jobId}:status", "queued", _timeout.Add(TimeSpan.FromSeconds(30)));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to set Redis job status for {JobId}", jobId);
            }

            var payloadObj = new { s3_bucket = _tempBucket, s3_key = s3Key, job_id = jobId };
            var payloadJson = JsonSerializer.Serialize(payloadObj);

            // Fast synchronous path: if small enough and we have a configured Lambda function name,
            // invoke the Lambda synchronously and return the transcript without going through SQS.
            if (size <= _syncThresholdBytes && !string.IsNullOrEmpty(_lambdaFunctionName))
            {
                try
                {
                    _logger.LogInformation("Attempting synchronous Lambda invoke for job {JobId}", jobId);
                    var invokeReq = new InvokeRequest
                    {
                        FunctionName = _lambdaFunctionName,
                        InvocationType = InvocationType.RequestResponse,
                        Payload = payloadJson
                    };

                    var invokeResp = await _lambdaClient.InvokeAsync(invokeReq, cancellationToken);
                    using var sr = new StreamReader(invokeResp.Payload);
                    var respContent = await sr.ReadToEndAsync();
                    try
                    {
                        using var doc = JsonDocument.Parse(respContent);
                        if (doc.RootElement.TryGetProperty("transcript", out var t))
                        {
                            var transcript = t.GetString() ?? string.Empty;
                            try
                            {
                                var db = _redis.GetDatabase();
                                await db.StringSetAsync($"transcribe:job:{jobId}:status", "completed", TimeSpan.FromMinutes(5));
                                await db.StringSetAsync($"transcribe:job:{jobId}:result", transcript, TimeSpan.FromMinutes(5));
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to update Redis on synchronous completion for {JobId}", jobId);
                            }

                            // Update DB transcription job record
                            try
                            {
                                var job = await _dbContext.TranscriptionJobs.FirstOrDefaultAsync(j => j.JobId == jobId, cancellationToken);
                                if (job != null)
                                {
                                    job.Status = "completed";
                                    job.TranscriptStorageKey = null;
                                    job.CompletedAt = DateTime.UtcNow;
                                    await _dbContext.SaveChangesAsync(cancellationToken);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to update DB transcription job for {JobId}", jobId);
                            }

                            return transcript;
                        }
                    }
                    catch (JsonException)
                    {
                        // If response wasn't JSON, return raw content
                        return respContent;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Synchronous Lambda invoke failed for job {JobId}, falling back to SQS", jobId);
                    // fall through to async path
                }
            }

            // Send message to SQS for asynchronous processing by the Lambda
            _logger.LogInformation("Sending message to SQS queue {QueueUrl} for job {JobId}", _queueUrl, jobId);
            await _sqsClient.SendMessageAsync(new Amazon.SQS.Model.SendMessageRequest
            {
                QueueUrl = _queueUrl,
                MessageBody = payloadJson
            }, cancellationToken);

            // Poll S3 for result file: transcribe-results/{jobId}.json
            var resultKey = $"transcribe-results/{jobId}.json";
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed < _timeout)
            {
                try
                {
                    var getResp = await _s3Client.GetObjectAsync(_tempBucket, resultKey, cancellationToken);
                    using var reader = new StreamReader(getResp.ResponseStream);
                    var content = await reader.ReadToEndAsync();
                    try
                    {
                        using var doc = JsonDocument.Parse(content);
                        if (doc.RootElement.TryGetProperty("transcript", out var t))
                        {
                            var transcript = t.GetString() ?? string.Empty;
                            try
                            {
                                var db = _redis.GetDatabase();
                                await db.StringSetAsync($"transcribe:job:{jobId}:status", "completed", TimeSpan.FromMinutes(5));
                                await db.StringSetAsync($"transcribe:job:{jobId}:result", transcript, TimeSpan.FromMinutes(5));
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to update Redis on completion for {JobId}", jobId);
                            }

                            // Update DB transcription job record
                            try
                            {
                                var job = await _dbContext.TranscriptionJobs.FirstOrDefaultAsync(j => j.JobId == jobId, cancellationToken);
                                if (job != null)
                                {
                                    job.Status = "completed";
                                    job.TranscriptStorageKey = resultKey;
                                    job.CompletedAt = DateTime.UtcNow;
                                    await _dbContext.SaveChangesAsync(cancellationToken);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to update DB transcription job for {JobId}", jobId);
                            }

                            return transcript;
                        }
                    }
                    catch (JsonException)
                    {
                        return content;
                    }
                }
                catch (Amazon.S3.AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // not ready yet
                }

                await Task.Delay(1000, cancellationToken);
            }

            // timed out
            try
            {
                var db = _redis.GetDatabase();
                await db.StringSetAsync($"transcribe:job:{jobId}:status", "failed", TimeSpan.FromMinutes(5));
            }
            catch { }

            try
            {
                var job = await _dbContext.TranscriptionJobs.FirstOrDefaultAsync(j => j.JobId == jobId, cancellationToken);
                if (job != null)
                {
                    job.Status = "failed";
                    job.Error = "timeout";
                    job.CompletedAt = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
            }
            catch { }

            throw new TimeoutException("Transcription timed out");
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("Whisper Lambda invocation timed out");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invoking Whisper Lambda");
            throw;
        }
        finally
        {
            try
            {
                _logger.LogInformation("Deleting uploaded S3 object {Bucket}/{Key}", _tempBucket, s3Key);
                await _s3Client.DeleteObjectAsync(new DeleteObjectRequest { BucketName = _tempBucket, Key = s3Key }, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete S3 object {Bucket}/{Key}", _tempBucket, s3Key);
            }
        }
    }
}
