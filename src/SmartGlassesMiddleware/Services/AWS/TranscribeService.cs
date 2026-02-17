using Amazon.TranscribeService;
using Amazon.TranscribeService.Model;
using Amazon.S3;
using Amazon.S3.Model;

namespace SmartGlassesMiddleware.Services;

/// <summary>
/// AWS Transcribe implementation of transcription service
/// </summary>
public class AwsTranscribeService : ITranscriptionService
{
    private readonly IAmazonTranscribeService _transcribeClient;
    private readonly IAmazonS3 _s3Client;
    private readonly ILogger<AwsTranscribeService> _logger;
    private readonly string _tempBucket;
    private readonly string _languageCode;

    public AwsTranscribeService(
        IAmazonTranscribeService transcribeClient,
        IAmazonS3 s3Client,
        IConfiguration configuration,
        ILogger<AwsTranscribeService> logger)
    {
        _transcribeClient = transcribeClient;
        _s3Client = s3Client;
        _logger = logger;
        
        _tempBucket = configuration["AWS:S3:MediaBucket"] 
            ?? throw new InvalidOperationException("AWS:S3:MediaBucket is required");
        _languageCode = configuration["AWS:Transcribe:LanguageCode"] ?? "en-US";
    }

    public async Task<string> TranscribeAudioAsync(Stream audioStream, CancellationToken cancellationToken = default)
    {
        var jobName = $"transcribe-{Guid.NewGuid()}";
        var s3Key = $"transcribe-temp/{jobName}.wav";

        try
        {
            // Upload audio to S3
            _logger.LogInformation("Uploading audio to S3 for transcription: {Key}", s3Key);
            await _s3Client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _tempBucket,
                Key = s3Key,
                InputStream = audioStream,
                ContentType = "audio/wav"
            }, cancellationToken);

            // Start transcription job
            _logger.LogInformation("Starting transcription job: {JobName}", jobName);
            var transcriptionRequest = new StartTranscriptionJobRequest
            {
                TranscriptionJobName = jobName,
                LanguageCode = _languageCode,
                MediaFormat = MediaFormat.Wav,
                Media = new Media
                {
                    MediaFileUri = $"s3://{_tempBucket}/{s3Key}"
                },
                OutputBucketName = _tempBucket,
                OutputKey = $"transcribe-output/{jobName}.json"
            };

            await _transcribeClient.StartTranscriptionJobAsync(transcriptionRequest, cancellationToken);

            // Poll for completion
            var transcriptionResult = await WaitForTranscriptionCompletionAsync(jobName, cancellationToken);

            // Clean up
            await CleanupAsync(s3Key, $"transcribe-output/{jobName}.json", jobName, cancellationToken);

            return transcriptionResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error transcribing audio with AWS Transcribe");
            throw;
        }
    }

    private async Task<string> WaitForTranscriptionCompletionAsync(
        string jobName, 
        CancellationToken cancellationToken)
    {
        var maxAttempts = 60; // 5 minutes max
        var attempt = 0;

        while (attempt < maxAttempts)
        {
            var statusRequest = new GetTranscriptionJobRequest
            {
                TranscriptionJobName = jobName
            };

            var statusResponse = await _transcribeClient.GetTranscriptionJobAsync(statusRequest, cancellationToken);
            var status = statusResponse.TranscriptionJob.TranscriptionJobStatus;

            _logger.LogInformation("Transcription job status: {Status}", status);

            if (status == TranscriptionJobStatus.COMPLETED)
            {
                // Download and parse transcript
                var outputKey = $"transcribe-output/{jobName}.json";
                var transcriptJson = await DownloadTranscriptAsync(outputKey, cancellationToken);
                return ParseTranscript(transcriptJson);
            }
            else if (status == TranscriptionJobStatus.FAILED)
            {
                throw new Exception($"Transcription job failed: {statusResponse.TranscriptionJob.FailureReason}");
            }

            await Task.Delay(5000, cancellationToken); // Wait 5 seconds
            attempt++;
        }

        throw new TimeoutException("Transcription job timed out");
    }

    private async Task<string> DownloadTranscriptAsync(string key, CancellationToken cancellationToken)
    {
        var response = await _s3Client.GetObjectAsync(_tempBucket, key, cancellationToken);
        using var reader = new StreamReader(response.ResponseStream);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private string ParseTranscript(string json)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            var results = document.RootElement.GetProperty("results");
            var transcripts = results.GetProperty("transcripts");
            
            if (transcripts.GetArrayLength() > 0)
            {
                return transcripts[0].GetProperty("transcript").GetString() ?? string.Empty;
            }

            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing transcript JSON");
            return string.Empty;
        }
    }

    private async Task CleanupAsync(
        string audioKey, 
        string transcriptKey, 
        string jobName, 
        CancellationToken cancellationToken)
    {
        try
        {
            // Delete S3 files
            await _s3Client.DeleteObjectAsync(_tempBucket, audioKey, cancellationToken);
            await _s3Client.DeleteObjectAsync(_tempBucket, transcriptKey, cancellationToken);

            // Delete transcription job
            await _transcribeClient.DeleteTranscriptionJobAsync(
                new DeleteTranscriptionJobRequest { TranscriptionJobName = jobName },
                cancellationToken);

            _logger.LogInformation("Cleaned up transcription resources for job: {JobName}", jobName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error cleaning up transcription resources");
        }
    }
}

/// <summary>
/// Interface for transcription service
/// </summary>
public interface ITranscriptionService
{
    Task<string> TranscribeAudioAsync(Stream audioStream, CancellationToken cancellationToken = default);
}
