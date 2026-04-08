using System.Text.RegularExpressions;
using AgentCraftLab.Search.Abstractions;

namespace AgentCraftLab.Search.Chunking;

/// <summary>
/// 結構感知分塊策略 — 按 Markdown heading、HTML heading、空行等結構邊界切割。
/// 太長的段落會二次切割（委派 FixedSizeChunker），太短的段落會合併到下一段。
/// 不依賴 embedding，純規則式。
/// </summary>
public partial class StructuralChunker : ITextChunker
{
    /// <summary>段落合併的最小字元數（低於此值的段落會合併到下一段）。</summary>
    private const int MinChunkChars = 50;

    private readonly FixedSizeChunker _fallbackChunker = new();

    public IReadOnlyList<ChunkResult> Chunk(string text, int chunkSize, int overlap)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        // 第一步：按結構邊界切割成段落（含位置追蹤）
        var sections = SplitBySections(text);

        // 第二步：合併太短的段落、切割太長的段落
        var normalized = NormalizeSections(sections, chunkSize, overlap);

        // 第三步：產生 ChunkResult
        var results = new List<ChunkResult>(normalized.Count);
        for (int i = 0; i < normalized.Count; i++)
        {
            var (sectionText, startPos) = normalized[i];
            var trimmed = sectionText.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            results.Add(new ChunkResult
            {
                Text = trimmed,
                Index = results.Count,
                StartPosition = startPos
            });
        }

        return results;
    }

    /// <summary>
    /// 按結構邊界（Markdown heading、HTML heading、連續空行）切割成段落，正向追蹤位置。
    /// </summary>
    private static List<(string Text, int StartPos)> SplitBySections(string text)
    {
        var boundaries = SectionBoundaryRegex().Matches(text);

        if (boundaries.Count == 0)
        {
            // 沒有結構邊界，按雙換行切割
            var parts = new List<(string, int)>();
            int pos = 0;
            foreach (var segment in text.Split(["\n\n", "\r\n\r\n"], StringSplitOptions.None))
            {
                var trimmed = segment.Trim();
                if (trimmed.Length > 0)
                {
                    // 找到 trimmed 在原文中的實際起始位置
                    var actualPos = text.IndexOf(trimmed[..Math.Min(trimmed.Length, 20)], pos, StringComparison.Ordinal);
                    parts.Add((trimmed, actualPos >= 0 ? actualPos : pos));
                }

                pos += segment.Length + 2; // +2 for "\n\n"
            }

            return parts;
        }

        var sections = new List<(string Text, int StartPos)>();
        int lastPos = 0;

        foreach (Match match in boundaries)
        {
            if (match.Index > lastPos)
            {
                var before = text[lastPos..match.Index].Trim();
                if (before.Length > 0)
                {
                    sections.Add((before, lastPos));
                }
            }

            lastPos = match.Index;
        }

        if (lastPos < text.Length)
        {
            var remaining = text[lastPos..].Trim();
            if (remaining.Length > 0)
            {
                sections.Add((remaining, lastPos));
            }
        }

        return sections;
    }

    /// <summary>
    /// 合併太短的段落、切割太長的段落，保留起始位置。
    /// </summary>
    private List<(string Text, int StartPos)> NormalizeSections(
        List<(string Text, int StartPos)> sections, int chunkSize, int overlap)
    {
        var result = new List<(string Text, int StartPos)>();
        string buffer = "";
        int bufferStartPos = 0;

        foreach (var (sectionText, sectionStartPos) in sections)
        {
            var candidate = buffer.Length > 0 ? buffer + "\n\n" + sectionText : sectionText;
            var candidateStartPos = buffer.Length > 0 ? bufferStartPos : sectionStartPos;

            if (candidate.Length <= chunkSize)
            {
                buffer = candidate;
                bufferStartPos = candidateStartPos;
            }
            else if (buffer.Length >= MinChunkChars)
            {
                result.Add((buffer, bufferStartPos));
                if (sectionText.Length > chunkSize)
                {
                    var subChunks = _fallbackChunker.Chunk(sectionText, chunkSize, overlap);
                    foreach (var sub in subChunks)
                    {
                        result.Add((sub.Text, sectionStartPos + sub.StartPosition));
                    }

                    buffer = "";
                    bufferStartPos = 0;
                }
                else
                {
                    buffer = sectionText;
                    bufferStartPos = sectionStartPos;
                }
            }
            else
            {
                var subChunks = _fallbackChunker.Chunk(candidate, chunkSize, overlap);
                foreach (var sub in subChunks)
                {
                    result.Add((sub.Text, candidateStartPos + sub.StartPosition));
                }

                buffer = "";
                bufferStartPos = 0;
            }
        }

        if (buffer.Length > 0)
        {
            if (buffer.Length < MinChunkChars && result.Count > 0)
            {
                var last = result[^1];
                result[^1] = (last.Text + "\n\n" + buffer, last.StartPos);
            }
            else
            {
                result.Add((buffer, bufferStartPos));
            }
        }

        return result;
    }

    /// <summary>
    /// 匹配 Markdown heading（# ~ ######）或 HTML heading（&lt;h1&gt;~&lt;h6&gt;）或連續空行。
    /// </summary>
    [GeneratedRegex(@"(?m)^(?:#{1,6}\s|<h[1-6][^>]*>)|\n{2,}", RegexOptions.Compiled)]
    private static partial Regex SectionBoundaryRegex();
}
