using Microsoft.EntityFrameworkCore;
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

    // Stronger model endpoint
    var stronger = builder.Configuration["ai_stronger_endpoint"];
    if (!string.IsNullOrEmpty(stronger))
    {
        builder.Configuration["AI:StrongerModelEndpoint"] = stronger;
        builder.Configuration["ai_stronger_endpoint"] = null;
    }
}
catch (Exception ex)
{
    _startupLogger.LogWarning(ex, "Error mapping secret names");
}

// Add services to the container.
builder.Services.AddControllers();
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

// Add HttpClient for stronger model calls
builder.Services.AddHttpClient<IAIService, OllamaAIService>();

// Register application services
builder.Services.AddScoped<IHistoryService, HistoryService>();
builder.Services.AddScoped<IMediaStorageService, MinioMediaStorageService>();
builder.Services.AddScoped<ICacheService, RedisCacheService>();
// Transcription service (server-side) — registered as a typed HTTP client above
builder.Services.AddHttpClient<ITranscriptionService, HttpTranscriptionService>();

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

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.EnsureCreated();
}

app.UseHttpsRedirection();
app.UseCors();

app.MapControllers();

// Health endpoint to allow Docker healthchecks to verify service dependencies
app.MapGet("/health", async (IServiceProvider services) =>
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();

    var dbOk = false;
    try
    {
        dbOk = await db.Database.CanConnectAsync();
    }
    catch
    {
        dbOk = false;
    }

    var redisOk = false;
    try
    {
        await redis.GetDatabase().PingAsync();
        redisOk = true;
    }
    catch
    {
        redisOk = false;
    }

    if (dbOk && redisOk)
    {
        return Results.Ok(new { status = "Healthy" });
    }

    return Results.StatusCode(503);
});

app.Run();
