using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.SemanticKernel;
using Minio;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Services;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Reflection;
using System.Runtime.Loader;
using System.Collections.Generic;
using QuestPDF;
using System.Linq;
using System.Threading.RateLimiting;
using Polly;
using Polly.Extensions.Http;
using Scalar.AspNetCore;

#pragma warning disable SKEXP0010

static bool IsTransientMigrationConnectivityFailure(Exception ex)
{
    Exception? current = ex;
    while (current != null)
    {
        var typeName = current.GetType().FullName ?? string.Empty;
        var message = current.Message ?? string.Empty;
        var connectivityRelated = typeName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("SocketException", StringComparison.OrdinalIgnoreCase)
            || current is TimeoutException;

        if (connectivityRelated &&
            (message.Contains("Failed to connect", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Timeout during connection attempt", StringComparison.OrdinalIgnoreCase)
                || message.Contains("connection attempt", StringComparison.OrdinalIgnoreCase)
                || message.Contains("timed out", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        current = current.InnerException;
    }

    return false;
}

static async Task ResetStalePendingUploadsAsync(IServiceProvider services, ILogger? logger)
{
    try
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Files stuck in PENDING state for more than 20 minutes are almost certainly
        // orphaned by a worker crash or server restart. Mark them as failed so clients
        // can surface a clear error instead of polling forever.
        var staleCutoff = DateTime.UtcNow.AddMinutes(-20);
        var staleFiles = await db.ManagedFiles
            .Where(f => f.StorageKey.StartsWith("PENDING:") && f.UploadedAt < staleCutoff)
            .ToListAsync();

        if (staleFiles.Count > 0)
        {
            foreach (var file in staleFiles)
                file.StorageKey = "FAILED: Upload processing timed out. Please re-upload the file.";
            await db.SaveChangesAsync();
            logger?.LogInformation("Marked {Count} stale pending upload(s) as failed at startup", staleFiles.Count);
        }
    }
    catch (Exception ex)
    {
        logger?.LogWarning(ex, "Failed to reset stale pending uploads at startup (non-fatal)");
    }
}

static async Task EnsureAdminListPerformanceIndexesAsync(IServiceProvider services, ILogger? logger)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var provider = db.Database.ProviderName ?? string.Empty;
    if (!provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
    {
        return;
    }

    // These indexes target the exact predicates used by admin files/groups list endpoints.
    var sqlStatements = new[]
    {
        // Partial index covering the exact filter used by GetFiles (visible files only).
        // Dramatically speeds up both the COUNT(*) and the paginated SELECT.
        "CREATE INDEX IF NOT EXISTS \"IX_ManagedFiles_Visible_UploadedAt\" ON \"ManagedFiles\" (\"UploadedAt\" DESC) WHERE \"FileSize\" > 0 AND \"StorageKey\" IS NOT NULL AND \"StorageKey\" != '' AND \"StorageKey\" NOT LIKE 'FAILED:%' AND \"StorageKey\" NOT LIKE 'PENDING:%'",
        "CREATE INDEX IF NOT EXISTS \"IX_ManagedFiles_UploadedAt\" ON \"ManagedFiles\" (\"UploadedAt\" DESC)",
        "CREATE INDEX IF NOT EXISTS \"IX_ResourceGroups_CreatedAt\" ON \"ResourceGroups\" (\"CreatedAt\" DESC)",
        "CREATE INDEX IF NOT EXISTS \"IX_ManagedFileGroups_ResourceGroupId\" ON \"ManagedFileGroups\" (\"ResourceGroupId\")",
        "CREATE INDEX IF NOT EXISTS \"IX_ManagedPromptGroups_ResourceGroupId\" ON \"ManagedPromptGroups\" (\"ResourceGroupId\")",
        "CREATE INDEX IF NOT EXISTS \"IX_DocumentGroups_ResourceGroupId\" ON \"DocumentGroups\" (\"ResourceGroupId\")",
        "CREATE INDEX IF NOT EXISTS \"IX_Conversations_ResourceGroupId\" ON \"Conversations\" (\"ResourceGroupId\")",
        "CREATE INDEX IF NOT EXISTS \"IX_ChatMessages_ConversationId\" ON \"ChatMessages\" (\"ConversationId\")"
    };

    foreach (var sql in sqlStatements)
    {
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    logger?.LogInformation("Ensured admin list performance indexes");
}

static async Task EnsureAbbreviationsTableAsync(IServiceProvider services, ILogger? logger)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var provider = db.Database.ProviderName ?? string.Empty;
    if (!provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
    {
        return;
    }

    // Some environments run with migrations disabled; ensure the abbreviation sync
    // storage exists so startup sync does not fail on missing relation errors.
    var sqlStatements = new[]
    {
        @"CREATE TABLE IF NOT EXISTS ""Abbreviations"" (
            ""Id"" uuid NOT NULL,
            ""ShortForm"" character varying(255) NOT NULL,
            ""ExpandedForm"" character varying(500) NOT NULL,
            ""Category"" character varying(100),
            ""Source"" character varying(50) NOT NULL,
            ""UpdatedAt"" timestamp with time zone NOT NULL,
            ""CreatedAt"" timestamp with time zone NOT NULL,
            CONSTRAINT ""PK_Abbreviations"" PRIMARY KEY (""Id"")
        )",
                // Keep exactly one row per case-insensitive (ShortForm, ExpandedForm) pair
                // so we can enforce dedupe with a unique functional index.
                @"DELETE FROM ""Abbreviations"" a
                    USING ""Abbreviations"" b
                    WHERE a.""Id"" > b.""Id""
                        AND lower(a.""ShortForm"") = lower(b.""ShortForm"")
                        AND lower(a.""ExpandedForm"") = lower(b.""ExpandedForm"")",
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Abbreviations_Short_Expanded_Unique_CI""
                    ON ""Abbreviations"" (lower(""ShortForm""), lower(""ExpandedForm""))",
        "CREATE INDEX IF NOT EXISTS \"IX_Abbreviations_ShortForm\" ON \"Abbreviations\" (\"ShortForm\")",
        "CREATE INDEX IF NOT EXISTS \"IX_Abbreviations_ExpandedForm\" ON \"Abbreviations\" (\"ExpandedForm\")",
        "CREATE INDEX IF NOT EXISTS \"IX_Abbreviations_Category\" ON \"Abbreviations\" (\"Category\")",
        "CREATE INDEX IF NOT EXISTS \"IX_Abbreviations_Source\" ON \"Abbreviations\" (\"Source\")"
    };

    foreach (var sql in sqlStatements)
    {
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    logger?.LogInformation("Ensured Abbreviations table and indexes");
}

var builder = WebApplication.CreateBuilder(args);

// Configure QuestPDF license for community usage to avoid runtime license validation errors
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

// Configure basic logging providers early so we can log during startup
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
// Increase default logging level to capture HTTP pipeline details during debugging
builder.Logging.SetMinimumLevel(LogLevel.Debug);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Debug);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Debug);

// Create a lightweight LoggerFactory for startup logging (avoids BuildServiceProvider)
using var _preLoggerFactory = LoggerFactory.Create(lb =>
{
    lb.AddConfiguration(builder.Configuration.GetSection("Logging"));
    lb.AddConsole();
});
var _startupLogger = _preLoggerFactory.CreateLogger("Startup");

// Configuration is loaded from environment variables (via .env file or docker-compose)
// For AWS LocalStack deployment, see docker-compose.localstack.yml

// Add services to the container.
builder.Services.AddControllers();
// Use RFC7807 Problem Details for model validation responses
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var pd = new ValidationProblemDetails(context.ModelState)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation Failed"
        };
        return new BadRequestObjectResult(pd)
        {
            ContentTypes = { "application/problem+json" }
        };
    };
});
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info = new()
        {
            Title = "Smart Glasses Middleware API",
            Version = "v1",
            Description = """
                Backend middleware for a smart glasses application.
                Provides a multi-modal AI API for voice-driven task dispatch, document processing,
                audio transcription, and conversation management.

                **Voice pipeline:** Audio is transcribed by an on-device Whisper WebGPU worker.
                Transcripts are sent to `POST /api/process`, which classifies the intent with
                AWS Bedrock and routes to the matching task handler.

                **Document pipeline:** Files uploaded via `POST /api/admin/files` are processed
                by a Lambda function (PyMuPDF + Bedrock embeddings) and stored in PostgreSQL
                with pgvector for semantic search via `POST /api/documents/query`.
                """,
            Contact = new() { Name = "Smart Glasses Middleware" },
        };
        return Task.CompletedTask;
    });
});

// Add response compression for better performance
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
    options.MimeTypes = Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults.MimeTypes.Concat(new[]
    {
        "application/json",
        "application/javascript",
        "text/javascript",
        "text/json",
        "image/svg+xml"
    });
});

// Configure compression levels
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProviderOptions>(options =>
{
    options.Level = System.IO.Compression.CompressionLevel.Fastest;
});

builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProviderOptions>(options =>
{
    options.Level = System.IO.Compression.CompressionLevel.Fastest;
});

// Enable detailed HTTP request/response logging (bodies limited to reasonable size)
builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields = HttpLoggingFields.RequestPropertiesAndHeaders | HttpLoggingFields.RequestBody | HttpLoggingFields.ResponseBody;
    options.RequestBodyLogLimit = 4096; // bytes
    options.ResponseBodyLogLimit = 4096; // bytes
    options.MediaTypeOptions.AddText("application/json");
});

// Configure PostgreSQL
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Database=smartglasses;Username=postgres;Password=postgres";

// db.t3.micro has ~112 max_connections. Npgsql's default pool is 100 per process,
// which would monopolise the entire DB budget and cause connection starvation.
// Cap the pool and set a short command timeout so slow queries fail fast
// and release connections back to the pool quickly.
var npgsqlConnBuilder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
if (npgsqlConnBuilder.MaxPoolSize >= 90)   npgsqlConnBuilder.MaxPoolSize   = 20;
if (npgsqlConnBuilder.MinPoolSize > 2)     npgsqlConnBuilder.MinPoolSize   = 1;
if (npgsqlConnBuilder.CommandTimeout >= 30) npgsqlConnBuilder.CommandTimeout = 10;
var tunedConnectionString = npgsqlConnBuilder.ConnectionString;

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(tunedConnectionString, o =>
    {
        o.UseVector();
        o.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(2),
            errorCodesToAdd: null);
    }));

// Configure Redis (only when a connection string is provided)
var redisConnection = builder.Configuration.GetConnectionString("Redis");
var redisAvailable = false;
if (!builder.Environment.IsEnvironment("Testing") && !string.IsNullOrWhiteSpace(redisConnection))
{
    try
    {
        var configuration = ConfigurationOptions.Parse(redisConnection);
        configuration.AbortOnConnectFail = false;
        configuration.ConnectRetry = 1;
        configuration.ConnectTimeout = 2000;
        configuration.SyncTimeout = 2000;

        var multiplexer = ConnectionMultiplexer.Connect(configuration);
        if (multiplexer.IsConnected)
        {
            builder.Services.AddSingleton<IConnectionMultiplexer>(multiplexer);
            redisAvailable = true;
            _startupLogger.LogInformation("Redis connection established at startup. Using RedisCacheService.");
        }
        else
        {
            multiplexer.Dispose();
            _startupLogger.LogWarning("Redis connection did not become ready during startup. Falling back to in-memory cache.");
        }
    }
    catch (Exception ex)
    {
        _startupLogger.LogWarning(ex, "Redis startup connection failed. Falling back to in-memory cache.");
    }
}
else
{
    _startupLogger.LogWarning("Redis registration skipped (either not configured or running in Testing environment).");
}

// Configure S3-compatible storage client
// Always register the AWS SDK clients (use default credential chain — ECS task role on AWS,
// environment variables or ~/.aws in local dev). Calls gracefully fail at runtime if no creds.
var awsRegion = builder.Configuration["AWS:Region"] ?? "us-east-1";
var awsRegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(awsRegion);
builder.Services.AddSingleton<Amazon.S3.IAmazonS3>(_ => new Amazon.S3.AmazonS3Client(awsRegionEndpoint));
builder.Services.AddSingleton<Amazon.Lambda.IAmazonLambda>(_ => new Amazon.Lambda.AmazonLambdaClient(awsRegionEndpoint));

// Use native AWS S3 for media storage unless LocalStack/MinIO is explicitly enabled.
// Storage backend must not depend on whether Bedrock is enabled.
var useLocalStack = builder.Configuration.GetValue<bool?>("AWS:UseLocalStack") ?? false;
var useAwsStorage = !useLocalStack;

if (!useAwsStorage)
{
    builder.Services.AddSingleton<IMinioClient>(sp =>
    {
        var endpoint = builder.Configuration["MinIO:Endpoint"] ?? "localhost:9000";
        var accessKey = builder.Configuration["MinIO:AccessKey"] ?? "minioadmin";
        var secretKey = builder.Configuration["MinIO:SecretKey"] ?? "minioadmin";

        return new MinioClient()
            .WithEndpoint(endpoint)
            .WithCredentials(accessKey, secretKey)
            .Build();
    });
}

// AI always uses AWS Bedrock. Register the Bedrock runtime client
// (uses default AWS credential chain — ECS task role on AWS).
builder.Services.AddSingleton<Amazon.BedrockRuntime.IAmazonBedrockRuntime>(_ =>
    new Amazon.BedrockRuntime.AmazonBedrockRuntimeClient(awsRegionEndpoint));

// Build the Semantic Kernel with the Bedrock chat completion service.
// RagService uses the kernel for local response generation.
builder.Services.AddSingleton<Kernel>(sp =>
{
    var kernelBuilder = Kernel.CreateBuilder();
    var bedrockRuntime = sp.GetRequiredService<Amazon.BedrockRuntime.IAmazonBedrockRuntime>();
    var bedrockLogger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<SmartGlassesMiddleware.Services.BedrockChatCompletionService>();
    var bedrockChatService = new SmartGlassesMiddleware.Services.BedrockChatCompletionService(
        bedrockRuntime, builder.Configuration, bedrockLogger);
    kernelBuilder.Services.AddSingleton<Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService>(bedrockChatService);
    return kernelBuilder.Build();
});

// Add HttpClient for advanced model calls with timeout configuration
builder.Services.AddHttpClient<IAIService, AIService>((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var timeoutSeconds = config.GetValue<int?>("ServiceTimeouts:AIServiceTimeoutSeconds") ?? 60;
    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
})
.AddPolicyHandler(GetRetryPolicy())
.AddPolicyHandler(GetCircuitBreakerPolicy());

// Configure HttpClient for abbreviation/FDA service with timeout and retry
builder.Services.AddHttpClient<IAbbreviationService, AbbreviationService>((sp, client) =>
{
    client.Timeout = TimeSpan.FromSeconds(30); // openFDA requests should be fast
})
.AddPolicyHandler(GetRetryPolicy())
.AddPolicyHandler(GetCircuitBreakerPolicy());

// Register application services
builder.Services.AddScoped<IHistoryService, HistoryService>();
// Use native AWS S3 storage service on ECS; MinIO for local dev
if (useAwsStorage)
    builder.Services.AddScoped<IMediaStorageService, SmartGlassesMiddleware.Services.AwsS3MediaStorageService>();
else
    builder.Services.AddScoped<IMediaStorageService, MinioMediaStorageService>();
// Register cache service: prefer Redis when configured, otherwise fall back to in-memory implementation
if (!builder.Environment.IsEnvironment("Testing") && redisAvailable)
{
    builder.Services.AddScoped<ICacheService, RedisCacheService>();
}
else
{
    builder.Services.AddScoped<ICacheService, InMemoryCacheService>();
    _startupLogger.LogWarning("Redis not configured or unavailable — using in-memory cache fallback.");
}
builder.Services.AddSingleton<IMetricsService, MetricsService>();
builder.Services.AddSingleton<IRequestMonitoringService, RequestMonitoringService>();
builder.Services.AddScoped<IRagService, RagService>();
// Always use Bedrock (Titan Embed V2) for embeddings
builder.Services.AddScoped<IEmbeddingClient, SmartGlassesMiddleware.Services.BedrockEmbeddingClient>();
// Document processing and RAG services
builder.Services.AddScoped<IDocumentProcessingService, DocumentProcessingService>();
builder.Services.AddScoped<IRAGService, RAGService>();
builder.Services.AddScoped<IDocumentHighlightService, DocumentHighlightService>();
builder.Services.AddScoped<IConversationService, ConversationService>();
builder.Services.AddScoped<IPageSummaryService, BedrockPageSummaryService>();
// Global mapping service for ResourceGroup -> Conversation
builder.Services.AddScoped<IGroupConversationMappingService, GroupConversationMappingService>();
builder.Services.AddScoped<IServerSettingsService, ServerSettingsService>();

// Audio transcription service removed: transcription now handled entirely in-browser via Whisper worker

// Auto-register ITaskHandler implementations from this assembly and optional plugin DLLs in the 'tasks' folder.

var handlerAssemblies = new List<Assembly> { Assembly.GetExecutingAssembly() };
var pluginsPath = Path.Combine(builder.Environment.ContentRootPath ?? string.Empty, "tasks");
if (Directory.Exists(pluginsPath))
{
    // Load precompiled DLL plugins
    foreach (var dll in Directory.GetFiles(pluginsPath, "*.dll"))
    {
        try
        {
            var asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(dll);
            handlerAssemblies.Add(asm);
        }
        catch (Exception ex)
        {
            var loggerFactory = LoggerFactory.Create(lb => lb.AddConsole());
            var tmpLogger = loggerFactory.CreateLogger("Startup");
            tmpLogger.LogWarning(ex, "Failed to load task plugin '{Dll}'", dll);
        }
    }

    // Compile any .cs source files in the tasks folder into an in-memory assembly
    var csFiles = Directory.GetFiles(pluginsPath, "*.cs", SearchOption.AllDirectories);
    if (csFiles.Length > 0)
    {
        try
        {
            var compiled = CompileSourceFiles(csFiles, _startupLogger);
            if (compiled != null) handlerAssemblies.Add(compiled);
        }
        catch (Exception ex)
        {
            _startupLogger.LogWarning(ex, "Failed to compile source task files in '{Path}'", pluginsPath);
        }
    }
}

var handlerTypes = handlerAssemblies
    .SelectMany(a => a.GetTypesSafe())
    .Where(t => typeof(ITaskHandler).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract)
    .ToList();

foreach (var ht in handlerTypes)
{
    builder.Services.AddScoped(typeof(ITaskHandler), ht);
}

// Local function to compile source files into a disk-cached assembly using Roslyn
Assembly? CompileSourceFiles(string[] filePaths, ILogger logger)
{
    try
    {
        // compute a simple cache key based on file paths and last write times
        using var sha = System.Security.Cryptography.SHA256.Create();
        var entries = filePaths.Select(fp => fp + ":" + File.GetLastWriteTimeUtc(fp).Ticks.ToString()).OrderBy(s => s);
        var keyBytes = System.Text.Encoding.UTF8.GetBytes(string.Join("|", entries));
        var hash = BitConverter.ToString(sha.ComputeHash(keyBytes)).Replace("-", "").ToLowerInvariant();

        var compiledDir = Path.Combine(pluginsPath, "compiled");
        Directory.CreateDirectory(compiledDir);
        var outPath = Path.Combine(compiledDir, $"plugins_{hash}.dll");

        // Remove other cached assemblies that don't match current key (cleanup outdated caches)
        try
        {
            foreach (var file in Directory.GetFiles(compiledDir, "plugins_*.dll"))
            {
                if (string.Equals(Path.GetFullPath(file), Path.GetFullPath(outPath), StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    File.Delete(file);
                    logger.LogInformation("Removed outdated compiled plugin cache: {File}", file);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Failed to remove cached plugin '{File}'", file);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error while cleaning compiled plugin cache directory");
        }

        if (File.Exists(outPath))
        {
            try
            {
                return AssemblyLoadContext.Default.LoadFromAssemblyPath(outPath);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to load cached plugin assembly '{Path}', will recompile", outPath);
                // fall through to recompile
            }
        }

        var syntaxTrees = filePaths.Select(fp => Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(File.ReadAllText(fp), path: fp)).ToArray();

        var refs = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(a.Location))
            .ToList();

        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            assemblyName: "TaskPlugins_" + Guid.NewGuid().ToString("N"),
            syntaxTrees: syntaxTrees,
            references: refs,
            options: new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary)
        );

        using var ms = new MemoryStream();
        var emitResult = compilation.Emit(ms);
        if (!emitResult.Success)
        {
            foreach (var diag in emitResult.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error))
            {
                logger.LogWarning("Plugin compile error: {Diag}", diag.ToString());
            }
            return null;
        }

        // write compiled assembly to disk for caching
        ms.Seek(0, SeekOrigin.Begin);
        using (var fs = File.Open(outPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            ms.CopyTo(fs);
        }

        return AssemblyLoadContext.Default.LoadFromAssemblyPath(outPath);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Compilation failed");
        return null;
    }
}

// Task dispatcher (configurable handler selection)
builder.Services.AddScoped<ITaskDispatcher, TaskDispatcher>();

// Allow an explicit E2E_MODE env var to opt into synchronous background processing
// which makes end-to-end tests deterministic by completing tasks inline.
var e2eMode = false;
var e2eModeEnv = Environment.GetEnvironmentVariable("E2E_MODE");
if (!string.IsNullOrEmpty(e2eModeEnv))
{
    bool.TryParse(e2eModeEnv, out e2eMode);
}

if (e2eMode)
{
    builder.Services.AddSingleton<IBackgroundTaskQueue, SynchronousBackgroundTaskQueue>();
}
else if (!builder.Environment.IsEnvironment("Testing") && !string.IsNullOrWhiteSpace(redisConnection))
{
    builder.Services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
}
else
{
    // When Redis is not configured (local dev), use an in-memory fallback queue to avoid requiring Redis.
    builder.Services.AddSingleton<IBackgroundTaskQueue, NoOpBackgroundTaskQueue>();
}

builder.Services.AddScoped<IMediaProcessor, MediaProcessor>();

// Only run the hosted worker in non-E2E mode — synchronous queue handles execution inline for tests
if (!e2eMode)
{
    builder.Services.AddHostedService<BackgroundTaskHostedService>();
}

if (!builder.Environment.IsEnvironment("Testing") && !string.IsNullOrWhiteSpace(redisConnection) && !e2eMode)
{
    builder.Services.AddHostedService<BackgroundTaskCleanupService>();
}

builder.Services.AddHostedService<DocumentProcessingBackgroundService>();

// Monthly FDA abbreviation sync background service
builder.Services.AddHostedService<AbbreviationSyncBackgroundService>();
builder.Services.AddHostedService<PreventSleepResetBackgroundService>();

// Add SignalR for real-time notifications with WebSocket optimizations
builder.Services.AddSignalR(options =>
{
    // Enable detailed errors in development
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    
    // Keep connections alive with heartbeat
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
    
    // Optimize message size
    options.MaximumReceiveMessageSize = 1024 * 1024; // 1 MB
    
    // Enable parallel invocations for better throughput
    options.MaximumParallelInvocationsPerClient = 10;
});

// Add Redis pub/sub bridge to forward messages to SignalR clients (only if Redis is configured)
if (!builder.Environment.IsEnvironment("Testing") && !string.IsNullOrWhiteSpace(redisConnection))
{
    builder.Services.AddSingleton<RedisPubSubBridge>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<RedisPubSubBridge>());
    // Register bridge status for health checks
    builder.Services.AddSingleton<RedisPubSubBridgeStatus>();
}

// Add rate limiting
// Default rate limiting to enabled in production only. Disable by default during
// development or testing runs to avoid flakiness in automated E2E tests.
// Allow explicit override via environment variable for CI/E2E runs.
var e2eDisable = false;
var e2eEnv = Environment.GetEnvironmentVariable("E2E_DISABLE_RATE_LIMIT");
if (!string.IsNullOrEmpty(e2eEnv))
{
    bool.TryParse(e2eEnv, out e2eDisable);
}
var rateLimitEnabled = builder.Configuration.GetValue<bool?>("RateLimiting:Enabled") ?? !(builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing") || e2eDisable);
if (rateLimitEnabled)
{
    builder.Services.AddRateLimiter(options =>
    {
        var requestsPerMinute = builder.Configuration.GetValue<int?>("RateLimiting:RequestsPerMinute") ?? 60;

        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.User.Identity?.Name ?? context.Request.Headers.Host.ToString(),
                factory: partition => new FixedWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = requestsPerMinute,
                    QueueLimit = 10, // Allow queueing up to 10 requests instead of immediate rejection
                    Window = TimeSpan.FromMinutes(1)
                }));

        options.OnRejected = async (context, token) =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogWarning("Rate limit exceeded for {Path} from {Host}", 
                context.HttpContext.Request.Path, context.HttpContext.Request.Headers.Host);
            
            context.HttpContext.Response.StatusCode = 429;
            context.HttpContext.Response.ContentType = "application/problem+json";

            var problemDetails = new ProblemDetails
            {
                Title = "Too Many Requests",
                Status = 429,
                Detail = "Rate limit exceeded. Please try again later.",
                Instance = context.HttpContext.Request.Path
            };
            problemDetails.Extensions["retryAfter"] = "60";

            await context.HttpContext.Response.WriteAsJsonAsync(problemDetails, token);
        };
    });
}

// (AssemblyExtensions moved to a dedicated file)

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? new[] { "http://localhost:3000", "http://localhost:5000" };

        if (builder.Environment.IsDevelopment() || allowedOrigins.Contains("*"))
        {
            // In development or when wildcard is configured, allow any origin
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
        else
        {
            // In production, restrict to specific origins
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
    });
});

var app = builder.Build();

// Validate critical configuration at startup
try
{
    var config = app.Services.GetRequiredService<IConfiguration>();
    var logger = app.Services.GetRequiredService<ILogger<Program>>();

    // Validate database connection string
    var dbConnStr = config.GetConnectionString("DefaultConnection");
    if (string.IsNullOrWhiteSpace(dbConnStr))
    {
        logger.LogWarning("Database connection string not properly configured. Using default localhost connection.");
    }

    // Validate Redis connection
    var redisConnStr = config.GetConnectionString("Redis");
    if (string.IsNullOrWhiteSpace(redisConnStr))
    {
        logger.LogWarning("Redis connection string not configured. Using default localhost:6379.");
    }

    // Validate MinIO configuration
    var minioAccessKey = config["MinIO:AccessKey"];
    var minioSecretKey = config["MinIO:SecretKey"];
    if (string.IsNullOrWhiteSpace(minioAccessKey) || string.IsNullOrWhiteSpace(minioSecretKey))
    {
        logger.LogWarning("MinIO credentials not properly configured. Using default credentials.");
    }

    // Validate advanced Bedrock model configuration
    var advancedModelId = config["AWS:Bedrock:AdvancedModelId"];
    var defaultModelId = config["AWS:Bedrock:ModelId"];
    if (string.IsNullOrWhiteSpace(advancedModelId) && string.IsNullOrWhiteSpace(defaultModelId))
    {
        logger.LogWarning("Advanced Bedrock model is not configured (AWS:Bedrock:AdvancedModelId or AWS:Bedrock:ModelId). Advanced tasks will be unavailable.");
    }

    logger.LogInformation("Configuration validation completed.");
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogError(ex, "Error during configuration validation");
}

// Configure the HTTP request pipeline.

// Apply EF Core migrations at startup if enabled (guarded to avoid test-time provider conflicts)
using (var scope = app.Services.CreateScope())
{
    try
    {
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetService<ILogger<Program>>();

        // Check configuration key first, then environment variable. Default: disabled.
        var migrateSetting = configuration.GetValue<bool?>("MigrateOnStartup");
        var migrateEnv = Environment.GetEnvironmentVariable("MIGRATE_ON_STARTUP");
        // MIGRATE_ONLY=true is set by the CI run-migrations job; it forces migration
        // and then exits cleanly so no HTTP server starts.
        var migrateOnly = string.Equals(Environment.GetEnvironmentVariable("MIGRATE_ONLY"), "true", StringComparison.OrdinalIgnoreCase);
        var shouldMigrate = migrateOnly || (migrateSetting ?? string.Equals(migrateEnv, "true", StringComparison.OrdinalIgnoreCase));

        if (shouldMigrate)
        {
            var maxMigrationAttempts = migrateOnly ? 5 : 1;
            var migrationDelay = TimeSpan.FromSeconds(migrateOnly ? 30 : 5);
            var migrationTimeout = TimeSpan.FromSeconds(migrateOnly ? 300 : 120);

            for (var attempt = 1; attempt <= maxMigrationAttempts; attempt++)
            {
                try
                {
                    logger?.LogInformation("Starting database migration (attempt {Attempt}/{Total})...", attempt, maxMigrationAttempts);

                    using var migrationScope = app.Services.CreateScope();
                    var db = migrationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                    var migrationTask = Task.Run(() => db.Database.Migrate());
                    var timeoutTask = Task.Delay(migrationTimeout);

                    var completedTask = await Task.WhenAny(migrationTask, timeoutTask);

                    if (completedTask == timeoutTask)
                    {
                        var errorMessage = $"Database migration timed out after {migrationTimeout.TotalSeconds:0} seconds on attempt {attempt}/{maxMigrationAttempts}. " +
                            "Application startup aborted to prevent schema issues.";
                        logger?.LogCritical(errorMessage);
                        throw new TimeoutException(errorMessage);
                    }

                    await migrationTask;
                    logger?.LogInformation("Database migration completed successfully.");
                    break;
                }
                catch (Exception ex) when (migrateOnly && attempt < maxMigrationAttempts && IsTransientMigrationConnectivityFailure(ex))
                {
                    logger?.LogWarning(ex,
                        "Database migration attempt {Attempt}/{Total} failed due to transient connectivity. Retrying in {DelaySeconds}s...",
                        attempt,
                        maxMigrationAttempts,
                        migrationDelay.TotalSeconds);
                    await Task.Delay(migrationDelay);
                }
            }

        }
        else
        {
            logger?.LogInformation("Skipping database migration on startup (MIGRATE_ON_STARTUP not set).");
        }

        try
        {
            await EnsureAdminListPerformanceIndexesAsync(app.Services, logger);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to ensure admin list performance indexes during startup; continuing");
        }

        try
        {
            await ResetStalePendingUploadsAsync(app.Services, logger);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to reset stale pending uploads at startup; continuing");
        }

        try
        {
            await EnsureAbbreviationsTableAsync(app.Services, logger);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to ensure abbreviations table at startup; continuing");
        }

        try
        {
            _startupLogger.LogInformation("Warming abbreviation cache from database...");
            using var abbrScope = app.Services.CreateScope();
            var abbrService = abbrScope.ServiceProvider.GetRequiredService<IAbbreviationService>();
            await abbrService.RefreshCacheAsync();
            _startupLogger.LogInformation("Abbreviation cache warmed. Scheduled sync service will decide whether a fetch is needed.");
        }
        catch (Exception ex)
        {
            _startupLogger.LogWarning(ex, "Failed to warm abbreviation cache at startup; continuing (scheduled sync can repopulate cache)");
        }

        // When MIGRATE_ONLY is set the container is a one-shot CI task — exit cleanly
        // so ECS marks it as stopped with exit code 0 and the CI job succeeds.
        if (migrateOnly)
        {
            _startupLogger.LogInformation("MIGRATE_ONLY=true — migrations complete, exiting.");
            return;
        }
    }
    catch (InvalidOperationException ex) when (app.Environment.IsEnvironment("Testing"))
    {
        var logger = scope.ServiceProvider.GetService<ILogger<Program>>();
        logger?.LogWarning(ex, "Skipping Database.Migrate during startup (likely test environment with multiple EF providers)");
    }
    catch (TimeoutException)
    {
        // Re-throw timeout exceptions to prevent app from starting with incomplete migrations
        throw;
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetService<ILogger<Program>>();
        logger?.LogError(ex, "Failed to apply migrations during startup; aborting application start");
        throw; // Re-throw to prevent app from starting with schema issues
    }
}

// Add request logging middleware FIRST to track all requests
// Enable response compression for better performance
app.UseResponseCompression();

// Enable built-in HTTP logging so we can see headers and bodies
app.UseHttpLogging();

static string GetFrameOptionsForPath(PathString path)
{
    var value = path.Value ?? string.Empty;
    var lowered = value.ToLowerInvariant();

    if (lowered.StartsWith("/api/admin/files/") && lowered.EndsWith("/preview"))
    {
        return "SAMEORIGIN";
    }

    if (lowered.StartsWith("/api/documents/") && lowered.EndsWith("/download"))
    {
        return "SAMEORIGIN";
    }

    return "DENY";
}

// Add request logging middleware FIRST to track all requests
app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    var requestId = Activity.Current?.Id ?? context.TraceIdentifier;
    var startTime = DateTime.UtcNow;
    
    logger.LogInformation("→ REQUEST START: {Method} {Path} [{RequestId}]", 
        context.Request.Method, context.Request.Path, requestId);
    
    try
    {
        await next();
        var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
        logger.LogInformation("← REQUEST END: {Method} {Path} [{RequestId}] - {StatusCode} ({Duration}ms)", 
            context.Request.Method, context.Request.Path, requestId, context.Response.StatusCode, duration);
    }
    catch (Exception ex)
    {
        var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
        logger.LogError(ex, "✗ REQUEST FAILED: {Method} {Path} [{RequestId}] ({Duration}ms)", 
            context.Request.Method, context.Request.Path, requestId, duration);
        throw;
    }
});

// Ensure baseline security headers are set for all responses, including static files.
// Placed before UseStaticFiles so static file responses receive these headers.
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        if (!context.Response.Headers.ContainsKey("X-Content-Type-Options"))
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        }
        if (!context.Response.Headers.ContainsKey("X-Frame-Options"))
        {
            context.Response.Headers["X-Frame-Options"] = GetFrameOptionsForPath(context.Request.Path);
        }
        if (!context.Response.Headers.ContainsKey("Referrer-Policy"))
        {
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
        }
        return Task.CompletedTask;
    });

    await next();
});

// Register MIME types that ASP.NET Core doesn't include by default but are
// required for the Silero VAD / ONNX Runtime WASM assets served from wwwroot.
var contentTypeProvider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
contentTypeProvider.Mappings[".wasm"] = "application/wasm";
contentTypeProvider.Mappings[".mjs"]  = "application/javascript";
contentTypeProvider.Mappings[".onnx"] = "application/octet-stream";
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = contentTypeProvider,
});

// Diagnostic middleware: capture raw request body for /api/ask for troubleshooting
app.Use(async (context, next) =>
{
    if (context.Request.Path.HasValue && context.Request.Path.Value!.StartsWith("/api/ask", StringComparison.OrdinalIgnoreCase))
    {
        try
        {
            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;
            var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogDebug("Raw request body for /api/ask (truncated 8k): {Body}", body.Length > 8192 ? body.Substring(0, 8192) : body);
        }
        catch (Exception ex)
        {
            var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogWarning(ex, "Failed to read raw request body for /api/ask");
        }
    }

    await next();
});

// OpenAPI JSON endpoint (available in all environments)
app.MapOpenApi();

// Interactive OpenAPI UI at /swagger (powered by Scalar)
app.MapScalarApiReference("/swagger/{documentName}", options =>
{
    options.WithTitle("Smart Glasses Middleware API");
});

if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}
app.UseCors();

// CRITICAL: UseRouting() is required for controller endpoints to work
app.UseRouting();

// Only use rate limiter if it's enabled (respect the computed flag including E2E override)
if (rateLimitEnabled)
{
    app.UseRateLimiter();
}

// Add metrics tracking middleware
app.Use(async (context, next) =>
{
    var startTime = DateTime.UtcNow;
    var endpoint = context.Request.Path.ToString();
    var metricsService = context.RequestServices.GetService<IMetricsService>();

    try
    {
        metricsService?.IncrementRequestCount(endpoint);
        await next();
    }
    catch
    {
        metricsService?.IncrementErrorCount(endpoint);
        throw;
    }
    finally
    {
        var duration = DateTime.UtcNow - startTime;
        metricsService?.RecordRequestDuration(endpoint, duration);
    }
});

// Add a consistent request id header for all responses so clients can correlate errors with logs
app.Use(async (context, next) =>
{
    var requestId = Activity.Current?.Id ?? context.TraceIdentifier;
    // ensure header is added even if later middleware short-circuits
    context.Response.OnStarting(() =>
    {
            context.Response.Headers["X-Request-Id"] = requestId;
            // Baseline security headers for basic hardening and tests
            if (!context.Response.Headers.ContainsKey("X-Content-Type-Options"))
            {
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            }
            if (!context.Response.Headers.ContainsKey("X-Frame-Options"))
            {
                context.Response.Headers["X-Frame-Options"] = GetFrameOptionsForPath(context.Request.Path);
            }
            if (!context.Response.Headers.ContainsKey("Referrer-Policy"))
            {
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
            }
        return Task.CompletedTask;
    });

    // make available to controllers and exception handlers
    context.Items["RequestId"] = requestId;
    await next();
});

// Global exception handler returning RFC7807 ProblemDetails
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exFeature = context.Features.Get<IExceptionHandlerFeature>();
        var ex = exFeature?.Error;
        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;

        var pd = new ProblemDetails
        {
            Title = "An unexpected error occurred",
            Detail = ex?.Message,
            Status = StatusCodes.Status500InternalServerError,
            Instance = context.Request.Path
        };
        pd.Extensions["requestId"] = traceId;

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";
        await JsonSerializer.SerializeAsync(context.Response.Body, pd, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    });
});

app.MapControllers();

// Add a test minimal API endpoint to verify routing works
app.MapGet("/test", () => "Test endpoint works!");

// Helper methods for retry policies
static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
}

static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));
}

// Health endpoint used by WakeupGate, ALB health checks, and deployment smoke tests.
// Postgres readiness is required before the app is considered ready for traffic.
// Redis status is reported for diagnostics but does not block startup.
// Cache-Control: no-store ensures CloudFront never caches this response.
app.MapGet("/health", async (IServiceProvider services, HttpContext context) =>
{
    // ECS/ALB process liveness check — always 200 when the app process is running.
    // This prevents the ALB from cycling containers during RDS cold-start (which can
    // take 4-8 minutes). The WakeupGate and CI smoke test use this endpoint so they
    // unblock as soon as ECS is up (~60 s), not when the DB is ready.
    // Use /ready for the DB/Redis readiness check.
    context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
    context.Response.Headers.Pragma = "no-cache";
    return Results.Ok(new { status = "Healthy", timestamp = DateTime.UtcNow });
});

// DB + Redis readiness endpoint — 503 until Postgres is reachable.
// The admin page's waitForAPI() polls this before loading data so it waits out
// the full RDS cold-start without showing 500 errors to the user.
app.MapGet("/ready", async (IServiceProvider services, HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
    context.Response.Headers.Pragma = "no-cache";

    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var redis = scope.ServiceProvider.GetService<IConnectionMultiplexer>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    var checks = new Dictionary<string, object>();
    var ready = true;

    // Postgres is required for API traffic.
    // Use a short timeout so /ready never hangs on a stuck connection.
    var dbOk = false;
    string? dbError = null;
    try
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        dbOk = await db.Database.CanConnectAsync(cts.Token);
    }
    catch (OperationCanceledException)
    {
        dbError = "Timeout: database check took too long";
    }
    catch (Exception ex)
    {
        dbError = ex.Message;
    }
    checks["postgres"] = new { healthy = dbOk, error = dbError };
    ready &= dbOk;
    if (!dbOk)
        logger.LogWarning("Postgres readiness check failed: {DbError}", dbError);

    // Redis is useful but non-blocking for readiness.
    if (redis != null)
    {
        var redisOk = false;
        string? redisError = null;
        try
        {
            await redis.GetDatabase().PingAsync();
            redisOk = true;
        }
        catch (Exception ex)
        {
            redisError = ex.Message;
        }
        checks["redis"] = new { healthy = redisOk, error = redisError };
        if (!redisOk)
            logger.LogWarning("Redis readiness check failed: {RedisError}", redisError);

        // Check Redis Pub/Sub bridge
        var bridgeOk = false;
        string? bridgeError = null;
        try
        {
            var bridgeStatus = scope.ServiceProvider.GetService<RedisPubSubBridgeStatus>();
            if (bridgeStatus != null)
            {
                bridgeOk = bridgeStatus.IsSubscribed;
                bridgeError = bridgeStatus.LastError;
            }
            else
            {
                bridgeOk = false;
                bridgeError = "RedisPubSubBridgeStatus not registered";
            }
        }
        catch (Exception ex)
        {
            bridgeError = ex.Message;
        }
        checks["redis-pubsub-bridge"] = new { healthy = bridgeOk, error = bridgeError };
        if (!bridgeOk)
            logger.LogWarning("Redis Pub/Sub bridge check failed: {BridgeError}", bridgeError);
    }
    else
    {
        checks["redis"] = new { healthy = (object)true, configured = false, note = "Redis disabled (not configured)" };
        checks["redis-pubsub-bridge"] = new { healthy = (object)true, configured = false, note = "Redis disabled (not configured)" };
    }

    var result = new
    {
        status = ready ? "Ready" : "Starting",
        checks = checks,
        timestamp = DateTime.UtcNow
    };

    return ready
        ? Results.Ok(result)
        : Results.Json(result, statusCode: 503);
});

// Metrics endpoint to view application statistics
app.MapGet("/metrics", (IMetricsService metricsService, HttpContext context, IConfiguration config) =>
{
    // In production, restrict access to metrics endpoint
    var env = context.RequestServices.GetRequiredService<IWebHostEnvironment>();
    if (!env.IsDevelopment())
    {
        // Check for authorization header or API key
        var apiKey = config["Metrics:ApiKey"];
        if (!string.IsNullOrEmpty(apiKey))
        {
            var providedKey = context.Request.Headers["X-API-Key"].FirstOrDefault();
            if (providedKey != apiKey)
            {
                return Results.Unauthorized();
            }
        }
        else
        {
            // If no API key configured in production, deny access
            return Results.Forbid();
        }
    }

    var summary = metricsService.GetSummary();
    return Results.Ok(new
    {
        timestamp = DateTime.UtcNow,
        endpoints = summary
    });
});

// Debug helper endpoint to call IAIService directly (bypasses multipart form parsing)
app.MapGet("/debug/ai", async (IServiceProvider sp, HttpContext ctx, ILoggerFactory lf) =>
{
    var logger = lf.CreateLogger("DebugAI");
    var input = ctx.Request.Query["input"].ToString();
    if (string.IsNullOrWhiteSpace(input))
    {
        return Results.BadRequest(new { error = "missing input query parameter" });
    }

    logger.LogInformation("/debug/ai entered; input length {L}", input.Length);

    IAIService aiService;
    try
    {
        logger.LogInformation("Resolving IAIService via DI");
        aiService = sp.GetRequiredService<IAIService>();
        logger.LogInformation("IAIService resolved successfully");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to resolve IAIService");
        return Results.StatusCode(500);
    }

    try
    {
        logger.LogInformation("Invoking IAIService.ProcessRequestAsync");
        var (task, response, usedAdvanced) = await aiService.ProcessRequestAsync(input, ctx.RequestAborted);
        logger.LogInformation("IAIService.ProcessRequestAsync completed");
        return Results.Ok(new { task, response, usedAdvanced });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Debug AI call failed");
        return Results.StatusCode(500);
    }
});

// Short-timeout debug endpoint to exercise IAIService with a safety timeout (10s)
app.MapGet("/debug/ai-nowarn", async (IServiceProvider sp, HttpContext ctx, ILoggerFactory lf) =>
{
    var logger = lf.CreateLogger("DebugAINow");
    var input = ctx.Request.Query["input"].ToString();
    if (string.IsNullOrWhiteSpace(input))
    {
        return Results.BadRequest(new { error = "missing input query parameter" });
    }

    logger.LogInformation("/debug/ai-nowarn entered; input length {L}", input.Length);

    IAIService aiService;
    try
    {
        aiService = sp.GetRequiredService<IAIService>();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to resolve IAIService");
        return Results.StatusCode(500);
    }

    try
    {
        var task = aiService.ProcessRequestAsync(input);
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10)));
        if (completed != task)
        {
            logger.LogWarning("IAIService.ProcessRequestAsync did not complete within 10s");
            return Results.StatusCode(504);
        }

        var (taskName, response, usedAdvanced) = await task;
        logger.LogInformation("IAIService.ProcessRequestAsync completed within timeout");
        return Results.Ok(new { task = taskName, response, usedAdvanced });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Debug AI call (nowarn) failed");
        return Results.StatusCode(500);
    }
});

// Fire-and-forget debug endpoint to start IAIService processing without waiting
app.MapGet("/debug/ai-fire", (IServiceProvider sp, HttpContext ctx, ILoggerFactory lf) =>
{
    var logger = lf.CreateLogger("DebugAIFire");
    var input = ctx.Request.Query["input"].ToString();
    if (string.IsNullOrWhiteSpace(input))
    {
        return Results.BadRequest(new { error = "missing input query parameter" });
    }

    try
    {
        var aiService = sp.GetRequiredService<IAIService>();
        // Launch processing on threadpool and return immediately
        Task.Run(async () =>
        {
            try
            {
                logger.LogInformation("Background: invoking IAIService.ProcessRequestAsync");
                var (t, r, u) = await aiService.ProcessRequestAsync(input);
                logger.LogInformation("Background: IAIService completed (len={L})", r?.Length ?? 0);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background AI processing failed");
            }
        });

        return Results.Accepted();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to start background AI processing");
        return Results.StatusCode(500);
    }
});

// Diagnostic endpoint: attempt to resolve IAIService with a short timeout
app.MapGet("/debug/resolve-ai", async (IServiceProvider sp, ILoggerFactory lf) =>
{
    var logger = lf.CreateLogger("DebugResolve");
    logger.LogInformation("/debug/resolve-ai entered");

    try
    {
        var resolveTask = Task.Run(() =>
        {
            try
            {
                var svc = sp.GetRequiredService<IAIService>();
                return svc != null;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "IAIService resolution threw");
                return false;
            }
        });

        var completed = await Task.WhenAny(resolveTask, Task.Delay(TimeSpan.FromSeconds(5)));
        if (completed != resolveTask)
        {
            logger.LogWarning("IAIService resolution did not complete within 5s");
            return Results.StatusCode(504);
        }

        return resolveTask.Result ? Results.Ok(new { resolved = true }) : Results.StatusCode(500);
    }
    catch (Exception ex)
    {
        lf.CreateLogger("DebugResolve").LogError(ex, "Unexpected error in /debug/resolve-ai");
        return Results.StatusCode(500);
    }
});

// Resolve components individually to isolate blocking DI resolution
app.MapGet("/debug/resolve-components", async (IServiceProvider sp, ILoggerFactory lf) =>
{
    var logger = lf.CreateLogger("DebugResolveComponents");
    logger.LogInformation("/debug/resolve-components entered");

    object? kernelResolved = null;
    object? dispatcherResolved = null;
    object? httpFactoryResolved = null;

    try
    {
        var kTask = Task.Run(() => { try { return (object)sp.GetRequiredService<Kernel>(); } catch (Exception ex) { logger.LogError(ex, "Kernel resolve failed"); return null; } });
        var dTask = Task.Run(() => { try { return (object)sp.GetRequiredService<ITaskDispatcher>(); } catch (Exception ex) { logger.LogError(ex, "Dispatcher resolve failed"); return null; } });
        var hTask = Task.Run(() => { try { return (object)sp.GetRequiredService<IHttpClientFactory>(); } catch (Exception ex) { logger.LogError(ex, "HttpClientFactory resolve failed"); return null; } });

        var all = Task.WhenAll(kTask, dTask, hTask);
        var completed = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(5)));
        if (completed != all)
        {
            logger.LogWarning("Component resolution timed out");
            // attempt to read partial results
            kernelResolved = kTask.IsCompletedSuccessfully ? kTask.Result : null;
            dispatcherResolved = dTask.IsCompletedSuccessfully ? dTask.Result : null;
            httpFactoryResolved = hTask.IsCompletedSuccessfully ? hTask.Result : null;
            return Results.StatusCode(504);
        }

        kernelResolved = kTask.Result;
        dispatcherResolved = dTask.Result;
        httpFactoryResolved = hTask.Result;

        return Results.Ok(new
        {
            kernel = kernelResolved != null,
            dispatcher = dispatcherResolved != null,
            httpFactory = httpFactoryResolved != null
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Unexpected error in /debug/resolve-components");
        return Results.StatusCode(500);
    }
});

// Map SignalR hubs LAST to avoid routing issues
app.MapHub<SmartGlassesMiddleware.Hubs.TaskHub>("/hubs/tasks");
app.MapHub<SmartGlassesMiddleware.Hubs.ConversationHub>("/hubs/conversations");
app.MapHub<SmartGlassesMiddleware.Hubs.ResourceHub>("/hubs/resources");

app.Run();

// Make Program accessible to tests
public partial class Program { }
