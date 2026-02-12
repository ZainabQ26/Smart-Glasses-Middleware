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
using System.Linq;
using System.Threading.RateLimiting;
using Polly;
using Polly.Extensions.Http;

#pragma warning disable SKEXP0010

var builder = WebApplication.CreateBuilder(args);

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

// Configuration is now loaded from environment variables (via .env file in docker-compose)
// No need for Docker secrets file loading since all settings are in .env

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
builder.Services.AddOpenApi();

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
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString, o => o.UseVector()));

// Configure Redis (only when a connection string is provided)
var redisConnection = builder.Configuration.GetConnectionString("Redis");
if (!builder.Environment.IsEnvironment("Testing") && !string.IsNullOrWhiteSpace(redisConnection))
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    {
        var configuration = ConfigurationOptions.Parse(redisConnection);
        configuration.AbortOnConnectFail = false;
        return ConnectionMultiplexer.Connect(configuration);
    });
}
else
{
    _startupLogger.LogWarning("Redis registration skipped (either not configured or running in Testing environment).");
}

// Configure MinIO
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

// Configure Semantic Kernel with Ollama
builder.Services.AddSingleton<Kernel>(sp =>
{
    var kernelBuilder = Kernel.CreateBuilder();

    var ollamaEndpoint = builder.Configuration["AI:OllamaEndpoint"] ?? "http://localhost:11434";
    var ollamaModel = builder.Configuration["AI:OllamaModel"] ?? "llama2";
    var ollamaEmbeddingModel = builder.Configuration["AI:OllamaEmbeddingModel"] ?? "nomic-embed-text";

    // Add Ollama chat completion service using OpenAI-compatible endpoint.
    // Note: Ollama doesn't require an API key for local endpoints; the API key
    // parameter is present for OpenAI-compatible overloads and is unused here.
    const string notUsedPlaceholderApiKey = "not-needed";
    
    // Configure with timeout and attach a logging handler to capture outgoing requests
    var chatHandler = new SmartGlassesMiddleware.Services.HttpLoggingHandler(_preLoggerFactory.CreateLogger("OllamaHttp")) { InnerHandler = new HttpClientHandler() };
    var httpClient = new HttpClient(chatHandler)
    {
        Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue<int?>("AI:OllamaTimeoutSeconds") ?? 120)
    };

    kernelBuilder.AddOpenAIChatCompletion(
        modelId: ollamaModel,
        apiKey: notUsedPlaceholderApiKey,
        endpoint: new Uri($"{ollamaEndpoint.TrimEnd('/')}/v1"),
        httpClient: httpClient);
    
    // Add Ollama embedding service
    var embedHandler = new SmartGlassesMiddleware.Services.HttpLoggingHandler(_preLoggerFactory.CreateLogger("OllamaEmbedHttp")) { InnerHandler = new HttpClientHandler() };
    var embeddingHttpClient = new HttpClient(embedHandler)
    {
        Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue<int?>("AI:OllamaEmbeddingTimeoutSeconds") ?? 120)
    };

    // Embeddings are provided via the typed IOllamaEmbeddingClient registered
    // below (used by RAGService). Avoid calling AddOpenAIEmbeddingGenerator
    // to keep compatibility with the installed SemanticKernel package.

    return kernelBuilder.Build();
});

// Add HttpClient for advanced model calls with timeout configuration
builder.Services.AddHttpClient<IAIService, OllamaAIService>((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var timeoutSeconds = config.GetValue<int?>("ServiceTimeouts:AIServiceTimeoutSeconds") ?? 60;
    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
})
.AddPolicyHandler(GetRetryPolicy())
.AddPolicyHandler(GetCircuitBreakerPolicy());

// Register application services
builder.Services.AddScoped<IHistoryService, HistoryService>();
builder.Services.AddScoped<IMediaStorageService, MinioMediaStorageService>();
builder.Services.AddScoped<ICacheService, RedisCacheService>();
builder.Services.AddSingleton<IMetricsService, MetricsService>();
builder.Services.AddSingleton<IRequestMonitoringService, RequestMonitoringService>();
builder.Services.AddScoped<IRagService, RagService>();
// Register Ollama embedding client (typed HttpClient)
builder.Services.AddHttpClient<IOllamaEmbeddingClient, OllamaEmbeddingClient>((sp, http) =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var baseUrl = cfg["AI:OllamaEndpoint"] ?? "http://ollama:11434";
    http.BaseAddress = new Uri(baseUrl);
    http.Timeout = TimeSpan.FromSeconds(cfg.GetValue<int?>("AI:OllamaEmbeddingTimeoutSeconds") ?? 120);
});
// Document processing and RAG services
builder.Services.AddScoped<IDocumentProcessingService, DocumentProcessingService>();
builder.Services.AddScoped<IRAGService, RAGService>();
builder.Services.AddScoped<IDocumentHighlightService, DocumentHighlightService>();
builder.Services.AddScoped<IConversationService, ConversationService>();
// Global mapping service for ResourceGroup -> Conversation
builder.Services.AddScoped<IGroupConversationMappingService, GroupConversationMappingService>();
// Transcription service (server-side) — registered as a typed HTTP client with timeout configuration
builder.Services.AddHttpClient<ITranscriptionService, HttpTranscriptionService>((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var timeoutSeconds = config.GetValue<int?>("ServiceTimeouts:TranscriptionTimeoutSeconds") ?? 30;
    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
})
.AddPolicyHandler(GetRetryPolicy())
.AddPolicyHandler(GetCircuitBreakerPolicy());

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
if (!builder.Environment.IsEnvironment("Testing") && !string.IsNullOrWhiteSpace(redisConnection))
{
    builder.Services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
}
else
{
    // When Redis is not configured (local dev), use an in-memory fallback queue to avoid requiring Redis.
    builder.Services.AddSingleton<IBackgroundTaskQueue, NoOpBackgroundTaskQueue>();
}
builder.Services.AddScoped<IMediaProcessor, MediaProcessor>();
builder.Services.AddHostedService<BackgroundTaskHostedService>();
if (!builder.Environment.IsEnvironment("Testing") && !string.IsNullOrWhiteSpace(redisConnection))
{
    builder.Services.AddHostedService<BackgroundTaskCleanupService>();
}
builder.Services.AddHostedService<DocumentProcessingBackgroundService>();

// Add SignalR for real-time notifications
builder.Services.AddSignalR();
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

        if (builder.Environment.IsDevelopment())
        {
            // In development, allow any origin for easier testing
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

    // Validate AI configuration
    var ollamaEndpoint = config["AI:OllamaEndpoint"];
    if (string.IsNullOrWhiteSpace(ollamaEndpoint))
    {
        logger.LogWarning("Ollama endpoint not configured. AI features may not work correctly.");
    }

    // Validate transcription endpoint
    var transcriptionEndpoint = config["Transcription:Endpoint"];
    if (string.IsNullOrWhiteSpace(transcriptionEndpoint))
    {
        logger.LogWarning("Transcription endpoint not configured. Audio transcription may not work correctly.");
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
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetService<ILogger<Program>>();

        // Check configuration key first, then environment variable. Default: disabled.
        var migrateSetting = configuration.GetValue<bool?>("MigrateOnStartup");
        var migrateEnv = Environment.GetEnvironmentVariable("MIGRATE_ON_STARTUP");
        var shouldMigrate = migrateSetting ?? string.Equals(migrateEnv, "true", StringComparison.OrdinalIgnoreCase);

        if (shouldMigrate)
        {
            logger?.LogInformation("Starting database migration...");
            
            // Run migration with a timeout to prevent indefinite hangs on lock acquisition
            var migrationTask = Task.Run(() => db.Database.Migrate());
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));
            
            var completedTask = await Task.WhenAny(migrationTask, timeoutTask);
            
            if (completedTask == timeoutTask)
            {
                var errorMessage = "Database migration timed out after 30 seconds. " +
                    "This usually means another instance is holding the migration lock. " +
                    "Application startup aborted to prevent schema issues.";
                logger?.LogCritical(errorMessage);
                throw new TimeoutException(errorMessage);
            }
            else
            {
                // Wait for the actual task to propagate any exceptions
                await migrationTask;
                logger?.LogInformation("Database migration completed successfully.");
            }
        }
        else
        {
            logger?.LogInformation("Skipping database migration on startup (MigrateOnStartup not enabled).");
        }
    }
    catch (InvalidOperationException ex)
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

static bool IsModelPresent(string modelName, HashSet<string> existingModels)
{
    if (existingModels.Contains(modelName))
    {
        return true;
    }

    if (!modelName.Contains(':') && existingModels.Contains(modelName + ":latest"))
    {
        return true;
    }

    if (modelName.Contains(':'))
    {
        var baseName = modelName.Split(':')[0];
        if (!string.IsNullOrWhiteSpace(baseName) && existingModels.Contains(baseName))
        {
            return true;
        }
    }

    return false;
}

// Optional Ollama model auto-setup on startup (similar to migrations)
using (var scope = app.Services.CreateScope())
{
    var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
    var isTestHost = AppDomain.CurrentDomain.GetAssemblies()
        .Any(a => a.GetName().Name?.StartsWith("xunit", StringComparison.OrdinalIgnoreCase) == true);

    var autoSetupEnabled = configuration.GetValue<bool?>("AI:OllamaAutoSetupOnStartup") ?? false;
    if (env.IsEnvironment("Testing") || isTestHost)
    {
        logger.LogInformation("Skipping Ollama auto-setup in Testing environment.");
    }
    else if (!autoSetupEnabled)
    {
        logger.LogInformation("Skipping Ollama auto-setup on startup (AI:OllamaAutoSetupOnStartup not enabled).");
    }
    else
    {
        try
        {
            var endpoint = configuration["AI:OllamaEndpoint"] ?? "http://localhost:11434";
            var modelsToEnsure = new[]
            {
                configuration["AI:OllamaModel"],
                configuration["AI:OllamaEmbeddingModel"]
            }
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

            if (modelsToEnsure.Count == 0)
            {
                logger.LogInformation("Ollama auto-setup skipped (no models configured).");
            }
            else
            {
                var timeoutSeconds = configuration.GetValue<int?>("AI:OllamaAutoSetupTimeoutSeconds") ?? 600;
                var failOnError = configuration.GetValue<bool?>("AI:OllamaAutoSetupFailOnError") ?? false;
                var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
                var delay = TimeSpan.FromSeconds(5);
                Exception? lastError = null;

                using var http = new HttpClient
                {
                    BaseAddress = new Uri(endpoint.TrimEnd('/')),
                    Timeout = TimeSpan.FromSeconds(timeoutSeconds)
                };

                while (DateTime.UtcNow < deadline)
                {
                    var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        using var listCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        var listResponse = await http.GetAsync("/api/tags", listCts.Token);
                        if (!listResponse.IsSuccessStatusCode)
                        {
                            var body = await listResponse.Content.ReadAsStringAsync();
                            logger.LogWarning("Failed to list Ollama models: {Status} {Body}", listResponse.StatusCode, body);
                            await Task.Delay(delay);
                            continue;
                        }

                        var bodyText = await listResponse.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(bodyText);
                        if (doc.RootElement.TryGetProperty("models", out var data))
                        {
                            foreach (var item in data.EnumerateArray())
                            {
                                if (item.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                                {
                                    var name = nameEl.GetString();
                                    if (!string.IsNullOrWhiteSpace(name))
                                    {
                                        existing.Add(name);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        logger.LogWarning(ex, "Failed to query Ollama models; retrying.");
                        await Task.Delay(delay);
                        continue;
                    }

                    var missing = modelsToEnsure.Where(m => !IsModelPresent(m, existing)).ToList();
                    if (missing.Count == 0)
                    {
                        logger.LogInformation("Ollama auto-setup complete (all models present).");
                        break;
                    }

                    foreach (var model in missing)
                    {
                        logger.LogInformation("Pulling Ollama model: {Model}", model);
                        var pullResponse = await http.PostAsJsonAsync("/api/pull", new { name = model, stream = false });
                        var pullBody = await pullResponse.Content.ReadAsStringAsync();
                        if (!pullResponse.IsSuccessStatusCode)
                        {
                            logger.LogWarning("Ollama model pull failed: {Model} {Status} {Body}", model, pullResponse.StatusCode, pullBody);
                            if (failOnError)
                            {
                                throw new InvalidOperationException($"Ollama model pull failed for {model}");
                            }
                        }
                        else
                        {
                            logger.LogInformation("Ollama model pull completed: {Model}", model);
                            existing.Add(model);
                        }
                    }

                    await Task.Delay(delay);
                }

                if (DateTime.UtcNow >= deadline)
                {
                    logger.LogWarning(lastError, "Ollama auto-setup timed out after {Seconds} seconds", timeoutSeconds);
                    if (failOnError)
                    {
                        throw new InvalidOperationException("Ollama auto-setup timed out", lastError);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            var failOnError = configuration.GetValue<bool?>("AI:OllamaAutoSetupFailOnError") ?? false;
            logger.LogWarning(ex, "Ollama auto-setup failed");
            if (failOnError)
            {
                throw;
            }
        }
    }
}

// Add request logging middleware FIRST to track all requests
// Enable built-in HTTP logging so we can see headers and bodies
app.UseHttpLogging();

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

app.UseStaticFiles();

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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

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

// Health endpoint to allow Docker healthchecks to verify service dependencies
app.MapGet("/health", async (IServiceProvider services) =>
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
    var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
    var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

    var checks = new Dictionary<string, object>();
    var allHealthy = true;

    // Check Postgres
    var dbOk = false;
    string? dbError = null;
    try
    {
        dbOk = await db.Database.CanConnectAsync();
    }
    catch (Exception ex)
    {
        dbError = ex.Message;
    }
    checks["postgres"] = new { healthy = dbOk, error = dbError };
    allHealthy &= dbOk;

    // Check for pending EF Core migrations
    var migrationsPending = false;
    int pendingMigrationsCount = 0;
    string? migrationsError = null;
    try
    {
        var pending = db.Database.GetPendingMigrations();
        pendingMigrationsCount = pending.Count();
        migrationsPending = pendingMigrationsCount > 0;

        // Determine whether automatic migration-on-startup is enabled
        var migrateSetting = configuration.GetValue<bool?>("MigrateOnStartup");
        var migrateEnv = Environment.GetEnvironmentVariable("MIGRATE_ON_STARTUP");
        var autoMigrateEnabled = migrateSetting ?? string.Equals(migrateEnv, "true", StringComparison.OrdinalIgnoreCase);

        checks["migrations"] = new { pending = migrationsPending, pendingCount = pendingMigrationsCount, autoApplyOnStartup = autoMigrateEnabled };

        // Consider unhealthy if there are pending migrations and they will not be applied automatically
        if (migrationsPending && !autoMigrateEnabled)
        {
            allHealthy &= false;
        }
    }
    catch (Exception ex)
    {
        migrationsError = ex.Message;
        checks["migrations"] = new { pending = false, pendingCount = 0, error = migrationsError };
    }

    // Check Redis
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
    allHealthy &= redisOk;

    // Check MinIO
    var minioOk = false;
    string? minioError = null;
    try
    {
        var minioEndpoint = configuration["MinIO:Endpoint"] ?? "minio:9000";
        using var httpClient = httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(5);
        var response = await httpClient.GetAsync($"http://{minioEndpoint}/minio/health/live");
        minioOk = response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
        minioError = ex.Message;
    }
    checks["minio"] = new { healthy = minioOk, error = minioError };
    allHealthy &= minioOk;

    // Check Ollama
    var ollamaOk = false;
    string? ollamaError = null;
    try
    {
        var ollamaEndpoint = configuration["AI:OllamaEndpoint"] ?? "http://ollama:11434";
        using var httpClient = httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(5);
        var response = await httpClient.GetAsync(ollamaEndpoint);
        ollamaOk = response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
        ollamaError = ex.Message;
    }
    checks["ollama"] = new { healthy = ollamaOk, error = ollamaError };
    allHealthy &= ollamaOk;

    // Check Vosk Transcriber
    var voskOk = false;
    string? voskError = null;
    try
    {
        var voskEndpoint = configuration["Transcription:Endpoint"] ?? "http://vosk-transcriber:8080";
        using var httpClient = httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(5);
        var response = await httpClient.GetAsync($"{voskEndpoint}/health");
        voskOk = response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
        voskError = ex.Message;
    }
    checks["vosk-transcriber"] = new { healthy = voskOk, error = voskError };
    allHealthy &= voskOk;

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
    allHealthy &= bridgeOk;

    var result = new
    {
        status = allHealthy ? "Healthy" : "Unhealthy",
        checks = checks,
        timestamp = DateTime.UtcNow
    };

    if (allHealthy)
    {
        return Results.Ok(result);
    }

    return Results.Json(result, statusCode: 503);
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

// Attempt to construct OllamaAIService manually (bypass IHttpClientFactory)
app.MapGet("/debug/new-ollama", async (IServiceProvider sp, ILoggerFactory lf) =>
{
    var logger = lf.CreateLogger("DebugNewOllama");
    logger.LogInformation("/debug/new-ollama entered");

    try
    {
        var kernel = sp.GetRequiredService<Kernel>();
        var cfg = sp.GetRequiredService<IConfiguration>();
        var dispatcher = sp.GetRequiredService<ITaskDispatcher>();
        var log = sp.GetRequiredService<ILogger<OllamaAIService>>();

        // Construct with a fresh HttpClient to avoid IHttpClientFactory
        var http = new HttpClient() { Timeout = TimeSpan.FromSeconds(30) };
        var instance = new SmartGlassesMiddleware.Services.OllamaAIService(kernel, http, cfg, log, dispatcher);
        logger.LogInformation("Manual OllamaAIService constructed successfully");
        return Results.Ok(new { constructed = true });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Manual OllamaAIService construction failed");
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
