using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.BedrockRuntime;
using Amazon.TranscribeService;
using Amazon.SecretsManager;
using Microsoft.Extensions.DependencyInjection;

namespace SmartGlassesMiddleware.Services.AWS;

/// <summary>
/// Extension methods for configuring AWS services
/// </summary>
public static class AwsServiceConfiguration
{
    /// <summary>
    /// Add AWS services to the service collection
    /// </summary>
    public static IServiceCollection AddAwsServices(this IServiceCollection services, IConfiguration configuration)
    {
        var useLocalStack = configuration.GetValue<bool>("AWS:UseLocalStack");
        var region = configuration["AWS:Region"] ?? "us-east-1";
        var awsRegion = RegionEndpoint.GetBySystemName(region);

        // Configure AWS clients
        if (useLocalStack)
        {
            // Configure for LocalStack
            var localStackEndpoint = configuration["AWS:Endpoint"] ?? "http://localhost:4566";
            var credentials = new BasicAWSCredentials("test", "test");

            var s3Config = new AmazonS3Config
            {
                ServiceURL = localStackEndpoint,
                ForcePathStyle = true,
                UseHttp = true
            };

            services.AddSingleton<IAmazonS3>(sp => new AmazonS3Client(credentials, s3Config));
            services.AddSingleton<IAmazonBedrockRuntime>(sp => 
                new AmazonBedrockRuntimeClient(credentials, new AmazonBedrockRuntimeConfig
                {
                    ServiceURL = localStackEndpoint
                }));
            services.AddSingleton<Amazon.Lambda.IAmazonLambda>(sp =>
                new Amazon.Lambda.AmazonLambdaClient(credentials, new Amazon.Lambda.AmazonLambdaConfig
                {
                    ServiceURL = localStackEndpoint
                }));
            services.AddSingleton<Amazon.SQS.IAmazonSQS>(sp =>
                new Amazon.SQS.AmazonSQSClient(credentials, new Amazon.SQS.AmazonSQSConfig
                {
                    ServiceURL = localStackEndpoint
                }));
            services.AddSingleton<IAmazonTranscribeService>(sp => 
                new AmazonTranscribeServiceClient(credentials, new AmazonTranscribeServiceConfig
                {
                    ServiceURL = localStackEndpoint
                }));
            services.AddSingleton<IAmazonSecretsManager>(sp => 
                new Amazon.SecretsManager.AmazonSecretsManagerClient(credentials, new Amazon.SecretsManager.AmazonSecretsManagerConfig
                {
                    ServiceURL = localStackEndpoint
                }));
        }
        else
        {
            // Configure for real AWS
            services.AddAWSService<IAmazonS3>();
            services.AddAWSService<IAmazonBedrockRuntime>();
            services.AddAWSService<Amazon.Lambda.IAmazonLambda>();
            services.AddAWSService<Amazon.SQS.IAmazonSQS>();
            services.AddAWSService<IAmazonTranscribeService>();
            services.AddAWSService<IAmazonSecretsManager>();
            
            services.AddDefaultAWSOptions(configuration.GetAWSOptions());
        }

        // Register AWS-based service implementations
        services.AddScoped<IMediaStorageService, S3MediaStorageService>();
        services.AddScoped<IAIService, BedrockAIService>();

        // Transcription service: synchronous on-demand Whisper Lambda invocation
        services.AddScoped<ITranscriptionService, LambdaWhisperTranscriptionService>();

        return services;
    }

    /// <summary>
    /// Check if AWS services are configured
    /// </summary>
    public static bool IsAwsConfigured(this IConfiguration configuration)
    {
        var s3Bucket = configuration["AWS:S3:MediaBucket"];
        return !string.IsNullOrEmpty(s3Bucket);
    }

    /// <summary>
    /// Check if LocalStack is being used
    /// </summary>
    public static bool IsLocalStack(this IConfiguration configuration)
    {
        return configuration.GetValue<bool>("AWS:UseLocalStack");
    }
}
