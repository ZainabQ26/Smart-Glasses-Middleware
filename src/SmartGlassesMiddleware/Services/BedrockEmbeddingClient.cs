using System.Text;
using System.Text.Json;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;

namespace SmartGlassesMiddleware.Services;

/// <summary>
/// Embedding client backed by Amazon Bedrock (Titan Text Embeddings V2).
/// Implements IOllamaEmbeddingClient so it can be used as a drop-in replacement
/// without changing the RAGService or any consumers.
///
/// Titan Embed V2 supports output dimensions: 256, 512, or 1024 (default 1024).
/// The pgvector column must match — see ApplicationDbContext vector(1024).
/// </summary>
public class BedrockEmbeddingClient : IOllamaEmbeddingClient
{
    private readonly IAmazonBedrockRuntime _bedrockRuntime;
    private readonly string _modelId;
    private readonly int _dimensions;
    private readonly ILogger<BedrockEmbeddingClient> _logger;

    public BedrockEmbeddingClient(
        IAmazonBedrockRuntime bedrockRuntime,
        IConfiguration config,
        ILogger<BedrockEmbeddingClient> logger)
    {
        _bedrockRuntime = bedrockRuntime;
        _modelId = config["AWS:Bedrock:EmbeddingModelId"] ?? "amazon.titan-embed-text-v2:0";
        _dimensions = config.GetValue<int?>("AWS:Bedrock:EmbeddingDimensions") ?? 1024;
        _logger = logger;
    }

    /// <summary>
    /// Generates an embedding for the given input text.
    /// The <paramref name="model"/> parameter is ignored — the embedding model is taken from configuration.
    /// </summary>
    public async Task<float[]> GenerateEmbeddingAsync(string model, string input, CancellationToken ct = default)
    {
        var requestBody = JsonSerializer.Serialize(new
        {
            inputText = input,
            dimensions = _dimensions,
            normalize = true
        });

        var request = new InvokeModelRequest
        {
            ModelId = _modelId,
            ContentType = "application/json",
            Accept = "application/json",
            Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody))
        };

        try
        {
            _logger.LogDebug("Invoking Bedrock embedding model {ModelId} (dims={Dims})", _modelId, _dimensions);
            var response = await _bedrockRuntime.InvokeModelAsync(request, ct);

            using var doc = JsonDocument.Parse(response.Body);

            // Titan Embed V2 response: { "embedding": [...], "inputTextTokenCount": N }
            if (doc.RootElement.TryGetProperty("embedding", out var emb))
            {
                var result = emb.EnumerateArray()
                    .Select(e => e.GetSingle())
                    .ToArray();
                _logger.LogDebug("Bedrock embedding returned {Dims} dimensions", result.Length);
                return result;
            }

            _logger.LogError("Unexpected Bedrock embedding response format (no 'embedding' property)");
            throw new InvalidOperationException("Unexpected Bedrock embedding response format");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get embedding from Bedrock model {ModelId}", _modelId);
            throw;
        }
    }
}
