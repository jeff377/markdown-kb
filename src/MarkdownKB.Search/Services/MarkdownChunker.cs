using MarkdownKB.Search.Models;

namespace MarkdownKB.Search.Services;

public class ChunkingOptions
{
    public int MaxTokens { get; set; } = 512;
    public int OverlapTokens { get; set; } = 50;
    public bool SplitByHeading { get; set; } = true;
    public bool IsolateCodeBlocks { get; set; } = true;
    public bool PreserveTable { get; set; } = true;
}

public class MarkdownChunker
{
    // ~4 characters per token (rough approximation for English; conservative for Chinese)
    private static int EstimateTokens(string text) => Math.Max(1, text.Length / 4);

    public IEnumerable<DocumentChunk> Chunk(
        string markdown,
        string filePath,
        ChunkingOptions options)
    {
        filePath = filePath.ToLowerInvariant();
        int chunkIndex = 0;

        var sections = options.SplitByHeading
            ? SplitByHeadings(markdown)
            : [("", markdown)];

        foreach (var (headingPath, sectionContent) in sections)
        {
            string overlapText = "";

            foreach (var (segContent, isAtomic) in ExtractSegments(sectionContent, options))
            {
                if (string.IsNullOrWhiteSpace(segContent)) continue;

                if (!isAtomic && EstimateTokens(segContent) > options.MaxTokens)
                {
                    bool first = true;
                    foreach (var part in SplitWithOverlap(segContent, options))
                    {
                        var content = first && !string.IsNullOrWhiteSpace(overlapText)
                            ? (overlapText + "\n\n" + part).Trim()
                            : part.Trim();

                        yield return MakeChunk(filePath, headingPath, chunkIndex++, content);
                        overlapText = GetOverlapText(part, options.OverlapTokens);
                        first = false;
                    }
                }
                else
                {
                    var content = !isAtomic && !string.IsNullOrWhiteSpace(overlapText)
                        ? (overlapText + "\n\n" + segContent).Trim()
                        : segContent.Trim();

                    yield return MakeChunk(filePath, headingPath, chunkIndex++, content);
                    overlapText = isAtomic ? "" : GetOverlapText(segContent, options.OverlapTokens);
                }
            }
        }
    }

    private static DocumentChunk MakeChunk(
        string filePath, string headingPath, int index, string content) =>
        new()
        {
            FilePath = filePath,
            HeadingPath = string.IsNullOrEmpty(headingPath) ? null : headingPath,
            ChunkIndex = index,
            Content = content,
            TokenCount = EstimateTokens(content)
        };

    // -------------------------------------------------------------------------
    // Heading splitter — tracks H1 > H2 hierarchy for heading paths
    // -------------------------------------------------------------------------
    private static List<(string headingPath, string content)> SplitByHeadings(string markdown)
    {
        var sections = new List<(string, string)>();
        var lines = markdown.Split('\n');
        string h1 = "";
        string currentPath = "";
        var currentLines = new List<string>();

        void Flush()
        {
            if (currentLines.Any(l => !string.IsNullOrWhiteSpace(l)))
                sections.Add((currentPath, string.Join("\n", currentLines)));
            currentLines.Clear();
        }

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("# ") && !trimmed.StartsWith("## "))
            {
                Flush();
                h1 = trimmed[2..].Trim();
                currentPath = h1;
                currentLines.Add(line);
            }
            else if (trimmed.StartsWith("## "))
            {
                Flush();
                var h2 = trimmed[3..].Trim();
                currentPath = string.IsNullOrEmpty(h1) ? h2 : $"{h1} > {h2}";
                currentLines.Add(line);
            }
            else
            {
                currentLines.Add(line);
            }
        }

        Flush();
        return sections.Count > 0 ? sections : [("", markdown)];
    }

    // -------------------------------------------------------------------------
    // Segment extractor — separates code blocks, tables, and plain text
    // -------------------------------------------------------------------------
    private static IEnumerable<(string content, bool isAtomic)> ExtractSegments(
        string content, ChunkingOptions options)
    {
        var lines = content.Split('\n');
        var textBuffer = new List<string>();
        var atomicBuffer = new List<string>();
        bool inCodeBlock = false;
        bool inTable = false;

        var result = new List<(string, bool)>();

        void FlushText()
        {
            if (textBuffer.Count == 0) return;
            var text = string.Join("\n", textBuffer).Trim();
            if (!string.IsNullOrWhiteSpace(text)) result.Add((text, false));
            textBuffer.Clear();
        }

        void FlushAtomic(bool atomic)
        {
            if (atomicBuffer.Count == 0) return;
            var text = string.Join("\n", atomicBuffer).Trim();
            if (!string.IsNullOrWhiteSpace(text)) result.Add((text, atomic));
            atomicBuffer.Clear();
        }

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("```"))
            {
                if (!inCodeBlock)
                {
                    FlushText();
                    if (inTable) { FlushAtomic(true); inTable = false; }
                    inCodeBlock = true;
                    (options.IsolateCodeBlocks ? atomicBuffer : textBuffer).Add(line);
                }
                else
                {
                    inCodeBlock = false;
                    if (options.IsolateCodeBlocks)
                    {
                        atomicBuffer.Add(line);
                        FlushAtomic(true);
                    }
                    else
                    {
                        textBuffer.Add(line);
                    }
                }
            }
            else if (inCodeBlock)
            {
                (options.IsolateCodeBlocks ? atomicBuffer : textBuffer).Add(line);
            }
            else if (options.PreserveTable && IsTableLine(line))
            {
                if (!inTable) { FlushText(); inTable = true; }
                atomicBuffer.Add(line);
            }
            else
            {
                if (inTable) { FlushAtomic(true); inTable = false; }
                textBuffer.Add(line);
            }
        }

        // Flush remaining buffers
        if (inCodeBlock && atomicBuffer.Count > 0) FlushAtomic(false); // unclosed fence → not atomic
        else if (atomicBuffer.Count > 0) FlushAtomic(true);
        FlushText();

        return result;
    }

    private static bool IsTableLine(string line)
    {
        var t = line.Trim();
        if (t.StartsWith("|")) return true;
        // Separator row: e.g. "------|------"
        var stripped = t.Replace("-", "").Replace("|", "").Replace(":", "").Trim();
        return t.Contains("|") && stripped.Length == 0;
    }

    // -------------------------------------------------------------------------
    // Split oversized plain-text segment into MaxTokens windows with overlap
    // -------------------------------------------------------------------------
    private static IEnumerable<string> SplitWithOverlap(string text, ChunkingOptions options)
    {
        var paragraphs = text
            .Split(["\n\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();

        if (paragraphs.Count == 0) yield break;

        var window = new List<string>();
        int tokens = 0;

        foreach (var para in paragraphs)
        {
            int pt = EstimateTokens(para);

            if (tokens + pt > options.MaxTokens && window.Count > 0)
            {
                yield return string.Join("\n\n", window);
                var overlap = GetOverlapText(string.Join("\n\n", window), options.OverlapTokens);
                window.Clear();
                if (!string.IsNullOrWhiteSpace(overlap))
                {
                    window.Add(overlap);
                    tokens = EstimateTokens(overlap);
                }
                else
                {
                    tokens = 0;
                }
            }

            window.Add(para);
            tokens += pt;
        }

        if (window.Count > 0)
            yield return string.Join("\n\n", window);
    }

    private static string GetOverlapText(string text, int overlapTokens)
    {
        if (overlapTokens <= 0 || string.IsNullOrWhiteSpace(text)) return "";
        int chars = overlapTokens * 4;
        if (text.Length <= chars) return text;

        var tail = text[^chars..];
        var nl = tail.IndexOf('\n');
        return nl > 0 ? tail[nl..].TrimStart() : tail;
    }
}
