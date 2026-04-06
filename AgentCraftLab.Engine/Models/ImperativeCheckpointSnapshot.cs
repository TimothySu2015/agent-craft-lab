using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentCraftLab.Engine.Models;

/// <summary>
/// 畫布 Workflow（ImperativeWorkflowStrategy）的 Checkpoint 快照。
/// 每個節點完成後存一份，供「節點重跑」和「Debug Mode」使用。
/// 比 ReAct 的 CheckpointSnapshot 簡單：不存 ChatHistories（畫布是單輪對話，重跑時重建）。
/// </summary>
public record ImperativeCheckpointSnapshot
{
    /// <summary>已完成的節點 ID 列表（有序）。</summary>
    public required List<string> CompletedNodeIds { get; init; }

    /// <summary>最後一個節點的 output（Resume 時作為下一個節點的 PreviousResult）。</summary>
    public required string PreviousResult { get; init; }

    /// <summary>下一個要執行的節點 ID（導航後的結果）。</summary>
    public required string NextNodeId { get; init; }

    /// <summary>各節點的 output（accumulate 模式用）。</summary>
    public Dictionary<string, string> NodeResults { get; init; } = new();

    /// <summary>Loop 計數器（迴圈節點 resume 用）。</summary>
    public Dictionary<string, int> LoopCounters { get; init; } = new();

    /// <summary>使用者原始輸入（Context Passing 模式用）。</summary>
    public string OriginalUserMessage { get; init; } = "";

    /// <summary>Context Passing 模式：previous-only / with-original / accumulate。</summary>
    public string ContextPassing { get; init; } = "previous-only";

    /// <summary>快照建立時間。</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
    };

    public string Serialize() => JsonSerializer.Serialize(this, JsonOptions);

    public static ImperativeCheckpointSnapshot? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ImperativeCheckpointSnapshot>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
