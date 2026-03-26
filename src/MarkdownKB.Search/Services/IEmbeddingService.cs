namespace MarkdownKB.Search.Services;

public interface IEmbeddingService
{
    Task<float[]> EmbedAsync(string text);
    Task<IEnumerable<float[]>> EmbedBatchAsync(IEnumerable<string> texts);
}
