using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Minio;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Services;
using StackExchange.Redis;

#pragma warning disable SKEXP0010

var builder = WebApplication.CreateBuilder(args);

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
            catch { /* ignore individual secret read errors */ }
        }
    }
}
catch { }

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
catch { }

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
    
    // Add Ollama chat completion service using OpenAI-compatible endpoint
    // Ollama doesn't require an API key, so we use a placeholder
    const string placeholderApiKey = "not-needed";
    kernelBuilder.AddOpenAIChatCompletion(
        modelId: ollamaModel,
        apiKey: placeholderApiKey,
        endpoint: new Uri(ollamaEndpoint));
    
    return kernelBuilder.Build();
});

// Add HttpClient for stronger model calls
builder.Services.AddHttpClient<IAIService, OllamaAIService>();

// Register application services
builder.Services.AddScoped<IAIService, OllamaAIService>();
builder.Services.AddScoped<IHistoryService, HistoryService>();
builder.Services.AddScoped<IMediaStorageService, MinioMediaStorageService>();
builder.Services.AddScoped<ICacheService, RedisCacheService>();

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
