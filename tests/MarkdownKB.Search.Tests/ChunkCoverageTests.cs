using MarkdownKB.Search.Services;
using MarkdownKB.Search.Tests.Evaluation;
using Xunit.Abstractions;

namespace MarkdownKB.Search.Tests;

/// <summary>
/// 評估維度二：Top-K 覆蓋率
/// 準備 15 個已知答案問題，確認答案所在段落出現在至少一個 chunk 中。
/// 目標：覆蓋率 > 80%（即 15 題中 12 題以上命中）。
/// </summary>
public class ChunkCoverageTests(ITestOutputHelper output)
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
    // Q&A 資料集（共 15 題，涵蓋 4 種文件類型）
    // -------------------------------------------------------------------------

    // 純文字段落文件（plain-text.md）
    private static readonly (string q, string answer)[] PlainTextQA =
    [
        ("前端使用什麼框架？",         "ASP.NET Core Razor Pages"),
        ("靜態資源放在哪裡？",         "wwwroot 目錄"),
        ("Embedding 向量維度是多少？",  "1536"),
        ("資料庫使用什麼 ORM？",       "EF Core"),
        ("生產環境建議搭配什麼？",      "Nginx"),
    ];

    // 大量 code block 文件（code-heavy.md）
    private static readonly (string q, string answer)[] CodeHeavyQA =
    [
        ("認證 Header 名稱為何？",          "Authorization"),
        ("Chunking MaxTokens 預設值？",      "MaxTokens = 512"),
        ("pgvector 使用何種距離計算？",       "CosineDistance"),
        ("OpenAI API 錯誤碼 429 代表什麼？",  "TooManyRequests"),
    ];

    // 表格文件（tables.md）
    private static readonly (string q, string answer)[] TablesQA =
    [
        ("text-embedding-3-small 的維度是多少？", "1536"),
        ("pgvector 的授權為何？",                "Apache 2.0"),
        ("搜尋 API 選用哪個框架？",              "ASP.NET Core Web API"),
    ];

    // 多層 heading 長文件（long-guide.md）
    private static readonly (string q, string answer)[] LongGuideQA =
    [
        ("建議的最低記憶體規格？",     "4 GB"),
        ("Docker Compose 最低版本？",  "2.20+"),
        ("索引進度如何監控？",         "watch -n 5"),
    ];

    // -------------------------------------------------------------------------
    // 輔助：執行覆蓋率評估並輸出報告
    // -------------------------------------------------------------------------
    private double RunCoverage(
        string fileName,
        (string q, string answer)[] qaSet,
        ChunkingOptions options)
    {
        var md = File.ReadAllText(Path.Combine(FixturesDir, fileName));
        var results = _eval.EvaluateCoverage(md, fileName, options, qaSet);
        var rate = _eval.CoverageRate(results);

        output.WriteLine($"=== {fileName} | MaxTokens={options.MaxTokens} | Coverage={rate:P0} ===");
        foreach (var r in results)
        {
            var status = r.Found ? "✓" : "✗";
            output.WriteLine($"  {status} Q: {r.Question}");
            if (!r.Found)
                output.WriteLine($"      Answer not found: \"{r.ExpectedAnswer}\"");
        }
        output.WriteLine("");

        return rate;
    }

    // -------------------------------------------------------------------------
    // 策略 A — 各文件類型覆蓋率
    // -------------------------------------------------------------------------

    [Fact]
    public void PlainText_OptionsA_CoverageAbove80Percent()
    {
        var rate = RunCoverage("plain-text.md", PlainTextQA, OptionsA);
        Assert.True(rate >= 0.8, $"plain-text 覆蓋率應 >= 80%，實際：{rate:P0}");
    }

    [Fact]
    public void CodeHeavy_OptionsA_CoverageAbove80Percent()
    {
        var rate = RunCoverage("code-heavy.md", CodeHeavyQA, OptionsA);
        Assert.True(rate >= 0.8, $"code-heavy 覆蓋率應 >= 80%，實際：{rate:P0}");
    }

    [Fact]
    public void Tables_OptionsA_CoverageAbove80Percent()
    {
        var rate = RunCoverage("tables.md", TablesQA, OptionsA);
        Assert.True(rate >= 0.8, $"tables 覆蓋率應 >= 80%，實際：{rate:P0}");
    }

    [Fact]
    public void LongGuide_OptionsA_CoverageAbove80Percent()
    {
        var rate = RunCoverage("long-guide.md", LongGuideQA, OptionsA);
        Assert.True(rate >= 0.8, $"long-guide 覆蓋率應 >= 80%，實際：{rate:P0}");
    }

    // -------------------------------------------------------------------------
    // 策略 B — 比較與 A 的覆蓋率差異（僅記錄，不 assert 勝負）
    // -------------------------------------------------------------------------

    [Fact]
    public void AllFiles_OptionsAvsB_CoverageComparison()
    {
        var files = new[]
        {
            ("plain-text.md", PlainTextQA),
            ("code-heavy.md", CodeHeavyQA),
            ("tables.md",     TablesQA),
            ("long-guide.md", LongGuideQA),
        };

        output.WriteLine("=== 策略 A vs B 覆蓋率比較 ===");
        output.WriteLine($"{"檔案",-20} {"A (512/50)",12} {"B (256/30)",12}");
        output.WriteLine(new string('-', 46));

        foreach (var (file, qa) in files)
        {
            var md = File.ReadAllText(Path.Combine(FixturesDir, file));
            var rA = _eval.CoverageRate(_eval.EvaluateCoverage(md, file, OptionsA, qa));
            var rB = _eval.CoverageRate(_eval.EvaluateCoverage(md, file, OptionsB, qa));
            output.WriteLine($"{file,-20} {rA,11:P0} {rB,11:P0}");
        }
    }

    // -------------------------------------------------------------------------
    // 評估維度三：語意完整性抽查報告（輸出供人工審查）
    // -------------------------------------------------------------------------

    [Fact]
    public void LongGuide_OptionsA_QualitySampleReport()
    {
        var md = File.ReadAllText(Path.Combine(FixturesDir, "long-guide.md"));
        var samples = _eval.SampleForQualityReview(md, "long-guide.md", OptionsA, sampleSize: 10);

        output.WriteLine("=== 語意完整性抽查（long-guide.md, 策略A）===");
        foreach (var s in samples)
        {
            var warn = (s.StartsAbruptly ? "[截頭?] " : "") + (s.EndsAbruptly ? "[截尾?]" : "");
            output.WriteLine($"[Chunk {s.ChunkIndex}] {s.HeadingPath} | {s.TokenCount} tokens {warn}");
            output.WriteLine($"  {s.Preview}");
            output.WriteLine("");
        }

        // 截斷比例不應超過 30%
        var abruptCount = samples.Count(s => s.StartsAbruptly || s.EndsAbruptly);
        var abruptRate = (double)abruptCount / samples.Count;
        output.WriteLine($"疑似截斷 chunk 比例：{abruptRate:P0} ({abruptCount}/{samples.Count})");

        Assert.True(abruptRate <= 0.3,
            $"疑似截斷的 chunk 比例應 <= 30%，實際：{abruptRate:P0}");
    }
}
