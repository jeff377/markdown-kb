using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Embeddings;

namespace MarkdownKB.Search.Services;

public class OpenAIEmbeddingService(
    IConfiguration configuration,
    ILogger<OpenAIEmbeddingService> logger) : IEmbeddingService
{
    private const string Model = "text-embedding-3-small";
    private const int BatchSize = 100;
    private const int MaxRetries = 3;

    private EmbeddingClient CreateClient()
    {
        var apiKey = configuration["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException("OpenAI:ApiKey is not configured.");
        return new EmbeddingClient(Model, apiKey);
    }

    public async Task<float[]> EmbedAsync(string text)
    {
        var results = await EmbedBatchAsync([text]);
        return results.First();
    }

    public async Task<IEnumerable<float[]>> EmbedBatchAsync(IEnumerable<string> texts)
    {
        var client = CreateClient();
        var textList = texts.ToList();
        var allEmbeddings = new List<float[]>(textList.Count);

        // Process in batches of BatchSize
        for (int offset = 0; offset < textList.Count; offset += BatchSize)
        {
            var batch = textList.Skip(offset).Take(BatchSize).ToList();
            var batchEmbeddings = await EmbedBatchWithRetryAsync(client, batch);
            allEmbeddings.AddRange(batchEmbeddings);
        }

        return allEmbeddings;
    }

    private async Task<List<float[]>> EmbedBatchWithRetryAsync(
        EmbeddingClient client, IReadOnlyList<string> batch)
    {
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                var result = await client.GenerateEmbeddingsAsync(batch);
                return result.Value
                    .OrderBy(e => e.Index)
                    .Select(e => e.ToFloats().ToArray())
                    .ToList();
            }
            catch (Exception ex) when (IsRateLimitError(ex) && attempt < MaxRetries - 1)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1)); // 2s, 4s, 8s
                logger.LogWarning(
                    "OpenAI rate limit hit (attempt {Attempt}/{Max}), retrying in {Delay}s",
                    attempt + 1, MaxRetries, delay.TotalSeconds);
                await Task.Delay(delay);
            }
        }

        // Final attempt — let exception propagate
        var finalResult = await client.GenerateEmbeddingsAsync(batch);
        return finalResult.Value
            .OrderBy(e => e.Index)
            .Select(e => e.ToFloats().ToArray())
            .ToList();
    }

    private static bool IsRateLimitError(Exception ex) =>
        ex.Message.Contains("429") ||
        ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("Rate limit", StringComparison.OrdinalIgnoreCase);
}
