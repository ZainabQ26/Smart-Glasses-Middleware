using Microsoft.SemanticKernel;
using Minio;
using SmartGlassesMiddleware.Services.AWS;

namespace SmartGlassesMiddleware.Services;

/// <summary>
/// Extension methods for registering services (AWS or legacy)
/// </summary>
public static class ServiceRegistrationExtensions
{
    /// <summary>
    /// Register storage services (AWS S3 or MinIO)
    /// </summary>
    public static IServiceCollection AddStorageServices(
        this IServiceCollection services, 
        IConfiguration configuration)
    {
        if (configuration.IsAwsConfigured())
        {
            // Use AWS services
            services.AddAwsServices(configuration);
        }
        else
        {
            // Use legacy MinIO
            services.AddSingleton<IMinioClient>(sp =>
            {
                var endpoint = configuration["MinIO:Endpoint"] ?? "localhost:9000";
                var accessKey = configuration["MinIO:AccessKey"] ?? "minioadmin";
                var secretKey = configuration["MinIO:SecretKey"] ?? "minioadmin";

                return new MinioClient()
                    .WithEndpoint(endpoint)
                    .WithCredentials(accessKey, secretKey)
                    .Build();
            });
            
            services.AddScoped<IMediaStorageService, MinioMediaStorageService>();
        }

        return services;
    }

    /// <summary>
    /// Register AI services (AWS Bedrock or Ollama)
    /// </summary>
    public static IServiceCollection AddAIServices(
        this IServiceCollection services,
        IConfiguration configuration,
        Kernel? kernel = null)
    {
        if (configuration.IsAwsConfigured())
        {
            // AWS Bedrock is already registered by AddAwsServices
            // Just ensure it's configured
            if (!services.Any(s => s.ServiceType == typeof(IAIService)))
            {
                services.AddScoped<IAIService, BedrockAIService>();
            }
        }
        else
        {
            // Use legacy Ollama with Semantic Kernel
            if (kernel != null)
            {
                services.AddSingleton(kernel);
            }

            services.AddHttpClient<IAIService, OllamaAIService>((sp, client) =>
            {
                var timeoutSeconds = configuration.GetValue<int?>("ServiceTimeouts:AIServiceTimeoutSeconds") ?? 60;
                client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            });
        }

        return services;
    }

    /// <summary>
    /// Register transcription services (stub implementation for local testing)
    /// TODO: Replace with actual transcription service (AWS Transcribe, Whisper Lambda, HTTP, etc.)
    /// </summary>
    public static IServiceCollection AddTranscriptionServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Always use stub for now to avoid dependency on disabled services
        if (!services.Any(s => s.ServiceType == typeof(ITranscriptionService)))
        {
            services.AddScoped<ITranscriptionService, StubTranscriptionService>();
        }

        return services;
    }

    /// <summary>
    /// Register embedding services (AWS Bedrock or Ollama)
    /// </summary>
    public static IServiceCollection AddEmbeddingServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (configuration.IsAwsConfigured())
        {
            // AWS Bedrock embeddings are handled in BedrockAIService
            // No separate registration needed
        }
        else
        {
            // Use legacy Ollama embeddings
            services.AddHttpClient<IOllamaEmbeddingClient, OllamaEmbeddingClient>((sp, http) =>
            {
                var baseUrl = configuration["AI:OllamaEndpoint"] ?? "http://ollama:11434";
                http.BaseAddress = new Uri(baseUrl);
                http.Timeout = TimeSpan.FromSeconds(configuration.GetValue<int?>("AI:OllamaEmbeddingTimeoutSeconds") ?? 120);
            });
        }

        return services;
    }

    /// <summary>
    /// Check if this is an AWS deployment
    /// </summary>
    public static bool IsAwsDeployment(this IConfiguration configuration)
    {
        return configuration.IsAwsConfigured();
    }
}
