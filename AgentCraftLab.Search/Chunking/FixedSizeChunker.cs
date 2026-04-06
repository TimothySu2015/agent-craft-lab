using AgentCraftLab.Search.Abstractions;

namespace AgentCraftLab.Search.Chunking;

/// <summary>
/// 固定大小分塊策略 — 優先在句號、換行、空格處斷句，避免在字詞中間切割。
/// </summary>
public class FixedSizeChunker : ITextChunker
{
    public IReadOnlyList<ChunkResult> Chunk(string text, int chunkSize, int overlap)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var chunks = new List<ChunkResult>();
        int pos = 0;
        int index = 0;

        while (pos < text.Length)
        {
            int end = Math.Min(pos + chunkSize, text.Length);

            // 嘗試在句號、換行處斷句（向前搜尋）
            if (end < text.Length)
            {
                int breakPoint = FindBreakPoint(text, pos + (chunkSize / 2), end);
                if (breakPoint > pos)
                {
                    end = breakPoint;
                }
            }

            string chunkText = text[pos..end].Trim();
            if (chunkText.Length > 0)
            {
                chunks.Add(new ChunkResult
                {
                    Text = chunkText,
                    Index = index++,
                    StartPosition = pos
                });
            }

            pos = end - overlap;
            if (pos >= text.Length)
            {
                break;
            }

            // 避免無限迴圈
            if (pos <= chunks.Count - 1 && end >= text.Length)
            {
                break;
            }
        }

        return chunks;
    }

    private static int FindBreakPoint(string text, int searchStart, int searchEnd)
    {
        // 優先找句號、問號、驚嘆號（含中文標點）
        for (int i = searchEnd - 1; i >= searchStart; i--)
        {
            if (text[i] is '.' or '。' or '?' or '？' or '!' or '！' or '．')
            {
                return i + 1;
            }
        }

        // 次優：換行
        for (int i = searchEnd - 1; i >= searchStart; i--)
        {
            if (text[i] == '\n')
            {
                return i + 1;
            }
        }

        // 最後：空格
        for (int i = searchEnd - 1; i >= searchStart; i--)
        {
            if (text[i] == ' ')
            {
                return i + 1;
            }
        }

        return searchEnd;
    }
}
