using MarkdownKB.Search.Services;
using MarkdownKB.Search.Tests.Evaluation;
using Xunit.Abstractions;

namespace MarkdownKB.Search.Tests;

/// <summary>
/// 評估維度一：Chunk 大小分布
/// 目標區間：100–600 tokens；過小 &lt; 100；過大 &gt; 600
/// </summary>
public class ChunkSizeDistributionTests(ITestOutputHelper output)
{
    private static readonly string FixturesDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static ChunkingOptions OptionsA => new()
    {
        MaxTokens = 512, OverlapTokens = 50,
        SplitByHeading = true, IsolateCodeBlocks = true, PreserveTable = true
    };

    private static ChunkingOptions OptionsB => new()
    {
        MaxTokens = 256, OverlapTokens = 30,
        SplitByHeading = true, IsolateCodeBlocks = true, PreserveTable = true
    };

    private readonly ChunkingEvaluator _eval = new();

    // -------------------------------------------------------------------------
    // 輔助：讀取 fixture 並輸出分布報告
    // -------------------------------------------------------------------------
    private ChunkingEvaluator.SizeDistribution EvalFile(
        string fileName, ChunkingOptions options)
    {
        var path = Path.Combine(FixturesDir, fileName);
        var md = File.ReadAllText(path);
        var dist = _eval.AnalyzeDistribution(md, fileName, options);

        output.WriteLine($"=== {fileName} | MaxTokens={options.MaxTokens} ===");
        output.WriteLine($"  Total   : {dist.Total}");
        output.WriteLine($"  TooSmall: {dist.TooSmall} (< 100 tokens)");
        output.WriteLine($"  InRange : {dist.InRange} (100–600 tokens)");
        output.WriteLine($"  TooLarge: {dist.TooLarge} (> 600 tokens)");
        output.WriteLine($"  Min/Avg/Median/Max: {dist.MinTokens}/{dist.AvgTokens}/{dist.MedianTokens}/{dist.MaxTokens}");
        output.WriteLine("");

        return dist;
    }

    // -------------------------------------------------------------------------
    // 策略 A (512/50) — 各文件類型
    // -------------------------------------------------------------------------

    [Fact]
    public void PlainText_OptionsA_ShouldProduceChunks()
    {
        var dist = EvalFile("plain-text.md", OptionsA);
        Assert.True(dist.Total > 0, "應產生至少 1 個 chunk");
        Assert.True(dist.TooLarge == 0, $"不應有過大 chunk（> 600 tokens），實際：{dist.TooLarge}");
    }

    [Fact]
    public void CodeHeavy_OptionsA_CodeBlocksShouldBeIsolated()
    {
        var path = Path.Combine(FixturesDir, "code-heavy.md");
        var md = File.ReadAllText(path);
        var chunker = new MarkdownChunker();
        var chunks = chunker.Chunk(md, "code-heavy.md", OptionsA).ToList();

        var codeChunks = chunks.Where(c => c.Content.TrimStart().StartsWith("```")).ToList();
        output.WriteLine($"Code-heavy: {chunks.Count} chunks, {codeChunks.Count} code blocks isolated");

        Assert.True(codeChunks.Count > 0, "應有獨立的 code block chunks");

        EvalFile("code-heavy.md", OptionsA);
    }

    [Fact]
    public void Tables_OptionsA_TablesShouldBePreservedWhole()
    {
        var path = Path.Combine(FixturesDir, "tables.md");
        var md = File.ReadAllText(path);
        var chunker = new MarkdownChunker();
        var chunks = chunker.Chunk(md, "tables.md", OptionsA).ToList();

        // 表格 chunk 應包含多個 | 行（至少 header + separator + 1 row = 3 行）
        var tableChunks = chunks.Where(c =>
            c.Content.Split('\n').Count(l => l.TrimStart().StartsWith("|")) >= 3).ToList();

        output.WriteLine($"Tables: {chunks.Count} chunks, {tableChunks.Count} table chunks");
        Assert.True(tableChunks.Count > 0, "應有保留完整表格的 chunk");

        EvalFile("tables.md", OptionsA);
    }

    [Fact]
    public void LongGuide_OptionsA_HeadingPathShouldBePopulated()
    {
        var path = Path.Combine(FixturesDir, "long-guide.md");
        var md = File.ReadAllText(path);
        var chunker = new MarkdownChunker();
        var chunks = chunker.Chunk(md, "long-guide.md", OptionsA).ToList();

        var withHeading = chunks.Where(c => !string.IsNullOrEmpty(c.HeadingPath)).ToList();
        output.WriteLine($"LongGuide: {chunks.Count} chunks, {withHeading.Count} with heading path");
        foreach (var h in withHeading.Select(c => c.HeadingPath).Distinct())
            output.WriteLine($"  heading: {h}");

        Assert.True(withHeading.Count > 0, "長文件應有帶 heading path 的 chunks");
        EvalFile("long-guide.md", OptionsA);
    }

    // -------------------------------------------------------------------------
    // 策略 B (256/30) — 與 A 的分布差異比較
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("plain-text.md")]
    [InlineData("code-heavy.md")]
    [InlineData("tables.md")]
    [InlineData("long-guide.md")]
    public void OptionsB_ShouldProduceMoreChunksThanOptionsA(string fileName)
    {
        var distA = EvalFile(fileName, OptionsA);
        var distB = EvalFile(fileName, OptionsB);

        output.WriteLine($"{fileName}: A={distA.Total} chunks vs B={distB.Total} chunks");

        // B 的 MaxTokens 較小，chunk 數應 >= A
        Assert.True(distB.Total >= distA.Total,
            $"策略 B (MaxTokens=256) chunk 數應 >= 策略 A (MaxTokens=512)");
    }

    // -------------------------------------------------------------------------
    // file_path 統一小寫驗證
    // -------------------------------------------------------------------------

    [Fact]
    public void AllChunks_FilePathShouldBeLowercase()
    {
        var chunker = new MarkdownChunker();
        var md = File.ReadAllText(Path.Combine(FixturesDir, "plain-text.md"));
        var chunks = chunker.Chunk(md, "Docs/Plain-Text.MD", OptionsA).ToList();

        Assert.All(chunks, c =>
            Assert.Equal(c.FilePath, c.FilePath.ToLowerInvariant()));
    }
}
