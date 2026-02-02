using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
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

// Create a lightweight LoggerFactory for startup logging (avoids BuildServiceProvider)
using var _preLoggerFactory = LoggerFactory.Create(lb =>
{
    lb.AddConfiguration(builder.Configuration.GetSection("Logging"));
    lb.AddConsole();
});
var _startupLogger = _preLoggerFactory.CreateLogger("Startup");

// Load any Docker secret files from /run/secrets into configuration.
// Filename convention: use double-underscore to indicate nesting, e.g.
//   ConnectionStrings__DefaultConnection -> Configuration["ConnectionStrings:DefaultConnection"]
// This makes it easy to provide any configuration key via a secret file.
try
{
    var secretsPath = "/run/secrets";
    if (Directory.Exists(secretsPath))
    {
        foreach (var file in Directory.GetFiles(secretsPath))
        {
            try
            {
                var name = Path.GetFileName(file);
                if (string.IsNullOrWhiteSpace(name)) continue;
                var key = name.Replace("__", ":");
                var value = File.ReadAllText(file).Trim();
                if (!string.IsNullOrEmpty(value))
                {
                    builder.Configuration[key] = value;
                }
            }
            catch (Exception ex)
            {
                _startupLogger.LogWarning(ex, "Failed to read secret file '{File}'", file);
            }
        }
    }
}
catch (Exception ex)
{
    _startupLogger.LogWarning(ex, "Error loading secrets");
}

// Map common simple secret filenames to configuration keys to keep secrets easy to understand
try
{
    // Postgres password: update connection string password
    var pgPass = builder.Configuration["postgres_password"];
    if (!string.IsNullOrEmpty(pgPass))
    {
        var current = builder.Configuration.GetConnectionString("DefaultConnection")
                      ?? "Host=localhost;Database=smartglasses;Username=postgres;Password=REPLACE_WITH_ENV_VARIABLE";
        if (current.IndexOf("Password=", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var parts = current.Split(';').Where(p => !p.TrimStart().StartsWith("Password=", StringComparison.OrdinalIgnoreCase)).ToList();
            parts.Add($"Password={pgPass}");
            builder.Configuration["ConnectionStrings:DefaultConnection"] = string.Join(";", parts);
        }
        else
        {
            builder.Configuration["ConnectionStrings:DefaultConnection"] = current + $";Password={pgPass}";
        }
        // remove the raw key to avoid duplication
        builder.Configuration["postgres_password"] = null;
    }

    // MinIO keys
    var minioAccess = builder.Configuration["minio_access_key"];
    var minioSecret = builder.Configuration["minio_secret_key"];
    if (!string.IsNullOrEmpty(minioAccess)) builder.Configuration["MinIO:AccessKey"] = minioAccess;
    if (!string.IsNullOrEmpty(minioSecret)) builder.Configuration["MinIO:SecretKey"] = minioSecret;
    if (!string.IsNullOrEmpty(minioAccess)) builder.Configuration["minio_access_key"] = null;
    if (!string.IsNullOrEmpty(minioSecret)) builder.Configuration["minio_secret_key"] = null;

    // Advanced model endpoint (mapped from legacy secret key name if present)
    var advanced = builder.Configuration["ai_advanced_endpoint"] ?? builder.Configuration["ai_stronger_endpoint"];
    if (!string.IsNullOrEmpty(advanced))
    {
        builder.Configuration["AI:AdvancedModelEndpoint"] = advanced;
        // remove legacy keys to avoid duplication
        builder.Configuration["ai_stronger_endpoint"] = null;
        builder.Configuration["ai_advanced_endpoint"] = null;
    }
}
catch (Exception ex)
{
    _startupLogger.LogWarning(ex, "Error mapping secret names");
}

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

// Configure PostgreSQL
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Host=localhost;Database=smartglasses;Username=postgres;Password=postgres";
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

// Configure Redis
var redisConnection = builder.Configuration.GetConnectionString("Redis") 
    ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var configuration = ConfigurationOptions.Parse(redisConnection);
    configuration.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(configuration);
});

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
    
    // Add Ollama chat completion service using OpenAI-compatible endpoint.
    // Note: Ollama doesn't require an API key for local endpoints; the API key
    // parameter is present for OpenAI-compatible overloads and is unused here.
    const string notUsedPlaceholderApiKey = "not-needed";
    kernelBuilder.AddOpenAIChatCompletion(
        modelId: ollamaModel,
        apiKey: notUsedPlaceholderApiKey,
        endpoint: new Uri(ollamaEndpoint));
    
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
    builder.Services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
    builder.Services.AddScoped<IMediaProcessor, MediaProcessor>();
    builder.Services.AddHostedService<BackgroundTaskHostedService>();
    builder.Services.AddHostedService<BackgroundTaskCleanupService>();

// Add SignalR for real-time notifications
builder.Services.AddSignalR();
// Add Redis pub/sub bridge to forward messages to SignalR clients
builder.Services.AddSingleton<RedisPubSubBridge>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RedisPubSubBridge>());
// Register bridge status for health checks
builder.Services.AddSingleton<RedisPubSubBridgeStatus>();

// Add rate limiting
var rateLimitEnabled = builder.Configuration.GetValue<bool?>("RateLimiting:Enabled") ?? true;
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
                    QueueLimit = 0,
                    Window = TimeSpan.FromMinutes(1)
                }));

        options.OnRejected = async (context, token) =>
        {
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
    if (string.IsNullOrWhiteSpace(dbConnStr) || dbConnStr.Contains("SET_IN_secrets"))
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
    if (string.IsNullOrWhiteSpace(minioAccessKey) || minioAccessKey.Contains("SET_IN_secrets") ||
        string.IsNullOrWhiteSpace(minioSecretKey) || minioSecretKey.Contains("SET_IN_secrets"))
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
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Ensure database is created (tolerant to missing DB during local development)
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Database.EnsureCreated();
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetService<ILogger<Program>>();
        logger?.LogWarning(ex, "Could not ensure database is created. Continuing startup without DB initialization.");
    }
}

app.UseHttpsRedirection();
app.UseCors();

// Only use rate limiter if it's enabled
var rateLimitConfig = app.Configuration.GetValue<bool?>("RateLimiting:Enabled") ?? true;
if (rateLimitConfig)
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

// Map SignalR hubs
app.MapHub<SmartGlassesMiddleware.Hubs.TaskHub>("/hubs/tasks");

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
        var ollamaEndpoint = configuration["AI:Endpoint"] ?? "http://ollama:11434";
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

app.Run();

// Make Program accessible to tests
public partial class Program { }
