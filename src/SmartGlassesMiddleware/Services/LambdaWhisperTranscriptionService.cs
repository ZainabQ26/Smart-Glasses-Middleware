using Amazon.Lambda;
using Amazon.Lambda.Model;
using Amazon.S3;
using Amazon.S3.Model;
using System.Text.Json;

namespace SmartGlassesMiddleware.Services;

/// <summary>
/// Transcription service that invokes the Whisper Lambda function on-demand.
///
/// Flow:
///   1. Upload audio bytes to S3 at a temporary key.
///   2. Invoke the Lambda synchronously (RequestResponse) with { s3_bucket, s3_key }.
///   3. Parse { "transcript": "..." } from the Lambda response.
///   4. The Lambda deletes the S3 object after reading it.
/// </summary>
public class LambdaWhisperTranscriptionService : ITranscriptionService
{
    private readonly IAmazonS3 _s3;
    private readonly IAmazonLambda _lambda;
    private readonly ILogger<LambdaWhisperTranscriptionService> _logger;
    private readonly string _mediaBucket;
    private readonly string _functionName;
    private readonly TimeSpan _timeout;

    public LambdaWhisperTranscriptionService(
        IAmazonS3 s3,
        IAmazonLambda lambda,
        IConfiguration configuration,
        ILogger<LambdaWhisperTranscriptionService> logger)
    {
        _s3 = s3;
        _lambda = lambda;
        _logger = logger;

        _mediaBucket = configuration["AWS:S3:MediaBucket"]
            ?? throw new InvalidOperationException("AWS:S3:MediaBucket must be configured for Lambda Whisper transcription.");

        _functionName = configuration["AWS:Transcription:WhisperLambdaFunction"]
            ?? "smart-glasses-whisper";

        var timeoutSec = configuration.GetValue<int?>("ServiceTimeouts:TranscriptionTimeoutSeconds") ?? 300;
        _timeout = TimeSpan.FromSeconds(timeoutSec);
    }

    /// <inheritdoc />
    public async Task<string?> TranscribeAsync(
        Stream audioStream,
        string fileName,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        var result = await TranscribeAudioAsync(audioStream, cancellationToken);
        return string.IsNullOrEmpty(result) ? null : result;
    }

    /// <inheritdoc />
    public async Task<string> TranscribeAudioAsync(
        Stream audioStream,
        CancellationToken cancellationToken = default)
    {
        var s3Key = $"transcribe-temp/{Guid.NewGuid():N}.wav";

        try
        {
            await using var buffered = new MemoryStream();
            await audioStream.CopyToAsync(buffered, cancellationToken);
            buffered.Position = 0;

            _logger.LogInformation(
                "Uploading {Bytes} bytes of audio to S3 {Bucket}/{Key}",
                buffered.Length, _mediaBucket, s3Key);

            await _s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _mediaBucket,
                Key = s3Key,
                InputStream = buffered,
                ContentType = "audio/wav",
            }, cancellationToken);

            var payload = JsonSerializer.Serialize(new { s3_bucket = _mediaBucket, s3_key = s3Key });

            _logger.LogInformation("Invoking Whisper Lambda {Function}", _functionName);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_timeout);

            var invokeResponse = await _lambda.InvokeAsync(
                new InvokeRequest
                {
                    FunctionName = _functionName,
                    InvocationType = InvocationType.RequestResponse,
                    Payload = payload,
                },
                cts.Token);

            using var reader = new StreamReader(invokeResponse.Payload);
            var responseJson = await reader.ReadToEndAsync(cts.Token);

            _logger.LogDebug("Whisper Lambda response: {Response}", responseJson);

            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("transcript", out var t))
            {
                var transcript = t.GetString() ?? string.Empty;
                _logger.LogInformation("Transcription complete: {Preview}",
                    transcript.Length > 80 ? transcript[..80] + "\u2026" : transcript);
                return transcript;
            }

            if (root.TryGetProperty("error", out var err))
                _logger.LogWarning("Whisper Lambda returned error: {Error}", err.GetString());
            else
                _logger.LogWarning("Unexpected Lambda response shape: {Json}", responseJson);

            return string.Empty;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Whisper Lambda transcription timed out after {Timeout}", _timeout);
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Whisper Lambda transcription failed (key={Key})", s3Key);
            return string.Empty;
        }
    }
}