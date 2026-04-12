namespace AgentCraftLab.Engine.Models.Schema;

/// <summary>
/// 條件判斷設定 — 共用於 ConditionNode / LoopNode。
/// 取代舊 schema 的 ConditionType + ConditionExpression 兩個散落欄位。
/// </summary>
public sealed record ConditionConfig
{
    public ConditionKind Kind { get; init; } = ConditionKind.Contains;

    /// <summary>
    /// 條件運算值：
    /// - Contains: 關鍵字
    /// - Regex: 正則表達式
    /// - LlmJudge: 判斷 prompt（LLM 評估上游輸出）
    /// - Expression: 可執行的布林運算式（未來擴充）
    /// </summary>
    public string Value { get; init; } = "";
}

public enum ConditionKind
{
    Contains,
    Regex,
    LlmJudge,
    Expression
}
