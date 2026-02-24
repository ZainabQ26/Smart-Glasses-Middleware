using Microsoft.SemanticKernel;
using Minio;
using SmartGlassesMiddleware.Services.AWS;

namespace SmartGlassesMiddleware.Services;

/// <summary>
/// Extension methods for registering services
/// </summary>
public static class ServiceRegistrationExtensions
{
    /// <summary>
    /// Register storage services
    /// </summary>
    public static IServiceCollection AddStorageServices(
        this IServiceCollection services, 
        IConfiguration configuration)
    {
        if (configuration.IsAwsConfigured())
        {
            // Use AWS S3
            services.AddAwsServices(configuration);
        }
        else
        {
            // Use S3-compatible storage
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
    /// Register AI services
    /// </summary>
    public static IServiceCollection AddAIServices(
        this IServiceCollection services,
        IConfiguration configuration,
        Kernel? kernel = null)
    {
        if (configuration.IsAwsConfigured())
        {
            // Use AWS Bedrock
            // AWS Bedrock is already registered by AddAwsServices
            if (!services.Any(s => s.ServiceType == typeof(IAIService)))
            {
                services.AddScoped<IAIService, BedrockAIService>();
            }
        }
        else
        {
            // Use alternative AI service
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
    /// Transcription services removed: audio transcription is now handled entirely in-browser via WebWorker
    /// </summary>
    /// Register embedding services
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
            // Use alternative embedding service
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
