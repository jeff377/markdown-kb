using MarkdownKB.Search.Models;
using MarkdownKB.Search.Services;

namespace MarkdownKB.Search.Tests.Evaluation;

/// <summary>
/// Chunking 品質評估輔助工具，對應 Phase 2 Step 3 的三個評估維度。
/// </summary>
public class ChunkingEvaluator
{
    private readonly MarkdownChunker _chunker = new();

    // -------------------------------------------------------------------------
    // 評估維度一：Chunk 大小分布
    // -------------------------------------------------------------------------

    public record SizeDistribution(
        int Total,
        int TooSmall,    // < 100 tokens
        int InRange,     // 100–600 tokens
        int TooLarge,    // > 600 tokens
        double MinTokens,
        double MaxTokens,
        double AvgTokens,
        double MedianTokens);

    public SizeDistribution AnalyzeDistribution(
        string markdown, string filePath, ChunkingOptions options)
    {
        var chunks = _chunker.Chunk(markdown, filePath, options).ToList();
        return ComputeDistribution(chunks);
    }

    public SizeDistribution AnalyzeDistribution(IReadOnlyList<DocumentChunk> chunks)
        => ComputeDistribution(chunks);

    private static SizeDistribution ComputeDistribution(IReadOnlyList<DocumentChunk> chunks)
    {
        if (chunks.Count == 0)
            return new SizeDistribution(0, 0, 0, 0, 0, 0, 0, 0);

        var tokens = chunks.Select(c => (double)(c.TokenCount ?? 0)).OrderBy(t => t).ToList();

        return new SizeDistribution(
            Total: chunks.Count,
            TooSmall: tokens.Count(t => t < 100),
            InRange: tokens.Count(t => t is >= 100 and <= 600),
            TooLarge: tokens.Count(t => t > 600),
            MinTokens: tokens.First(),
            MaxTokens: tokens.Last(),
            AvgTokens: Math.Round(tokens.Average(), 1),
            MedianTokens: tokens[tokens.Count / 2]);
    }

    // -------------------------------------------------------------------------
    // 評估維度二：Top-K 覆蓋率
    // -------------------------------------------------------------------------

    public record CoverageResult(
        string Question,
        string ExpectedAnswer,
        bool Found,
        int? FoundAtChunkIndex,
        string? FoundInChunk);

    /// <summary>
    /// 驗證 expectedAnswer 文字是否出現在任何一個 chunk 中（模擬 Top-K 命中）。
    /// 實際 embedding 搜尋未建立前，以 contains 比對作為保守驗證。
    /// </summary>
    public IReadOnlyList<CoverageResult> EvaluateCoverage(
        string markdown,
        string filePath,
        ChunkingOptions options,
        IReadOnlyList<(string question, string expectedAnswer)> qaSet)
    {
        var chunks = _chunker.Chunk(markdown, filePath, options).ToList();

        return qaSet.Select(qa =>
        {
            var hit = chunks.FirstOrDefault(c =>
                c.Content.Contains(qa.expectedAnswer, StringComparison.OrdinalIgnoreCase));

            return new CoverageResult(
                Question: qa.question,
                ExpectedAnswer: qa.expectedAnswer,
                Found: hit is not null,
                FoundAtChunkIndex: hit?.ChunkIndex,
                FoundInChunk: hit?.Content[..Math.Min(200, hit.Content.Length)] + "…");
        }).ToList();
    }

    public double CoverageRate(IReadOnlyList<CoverageResult> results) =>
        results.Count == 0 ? 0 : (double)results.Count(r => r.Found) / results.Count;

    // -------------------------------------------------------------------------
    // 評估維度三：語意完整性摘要（供人工抽查使用）
    // -------------------------------------------------------------------------

    public record QualitySample(
        int ChunkIndex,
        string? HeadingPath,
        int TokenCount,
        bool StartsAbruptly,   // 第一個字是小寫非句首（可能是截斷）
        bool EndsAbruptly,     // 最後一字非標點（可能是截斷）
        string Preview);

    public IReadOnlyList<QualitySample> SampleForQualityReview(
        string markdown, string filePath, ChunkingOptions options, int sampleSize = 10)
    {
        var chunks = _chunker.Chunk(markdown, filePath, options).ToList();
        var rng = new Random(42);
        var sample = chunks.OrderBy(_ => rng.Next()).Take(sampleSize).ToList();

        // Connector phrases that imply the sentence continues beyond the chunk boundary
        string[] connectors = ["以下", "如下", "包含：", "包括：", "如下：", "所示：", "為："];

        return sample.Select(c =>
        {
            var trimmed = c.Content.Trim();
            bool startsAbruptly = trimmed.Length > 0 &&
                char.IsAsciiLetterLower(trimmed[0]) && !trimmed.StartsWith("```");
            bool endsAbruptly = connectors.Any(p => trimmed.EndsWith(p));

            return new QualitySample(
                ChunkIndex: c.ChunkIndex,
                HeadingPath: c.HeadingPath,
                TokenCount: c.TokenCount ?? 0,
                StartsAbruptly: startsAbruptly,
                EndsAbruptly: endsAbruptly,
                Preview: trimmed[..Math.Min(300, trimmed.Length)]);
        }).OrderBy(s => s.ChunkIndex).ToList();
    }
}
