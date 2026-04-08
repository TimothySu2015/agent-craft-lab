namespace AgentCraftLab.Search.Abstractions;

/// <summary>
/// 文字分塊策略介面。
/// </summary>
public interface ITextChunker
{
    /// <summary>將文字依策略分塊。</summary>
    IReadOnlyList<ChunkResult> Chunk(string text, int chunkSize, int overlap);
}

/// <summary>分塊結果。</summary>
public class ChunkResult
{
    /// <summary>分塊文字。</summary>
    public required string Text { get; init; }

    /// <summary>在原文中的分塊索引（從 0 開始）。</summary>
    public required int Index { get; init; }

    /// <summary>在原文中的起始字元位置。</summary>
    public int StartPosition { get; init; }
}
