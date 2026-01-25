using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Minio;
using SmartGlassesMiddleware.Data;
using SmartGlassesMiddleware.Services;
using StackExchange.Redis;

#pragma warning disable SKEXP0010

var builder = WebApplication.CreateBuilder(args);

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
    kernelBuilder.AddOpenAIChatCompletion(
        modelId: ollamaModel,
        apiKey: "not-needed", // Ollama doesn't require API key
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
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
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

app.Run();
