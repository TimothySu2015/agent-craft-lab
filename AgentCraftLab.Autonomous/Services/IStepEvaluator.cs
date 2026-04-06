namespace AgentCraftLab.Autonomous.Services;

/// <summary>
/// 步驟評估器介面 — 每步 tool call 後評估決策品質（Step-level PRM）。
/// Route A: 確定性規則（零 LLM 成本）
/// Route B: LLM 評分（未來，可組合）
/// </summary>
public interface IStepEvaluator
{
    /// <summary>
    /// 評估一步 tool call 的品質。
    /// </summary>
    /// <param name="context">當前步驟的上下文。</param>
    /// <returns>評估結果（null = 無異常，不需介入）。</returns>
    StepEvaluation? Evaluate(StepContext context);
}

/// <summary>步驟上下文 — 提供評估所需的當前步驟資訊。</summary>
public sealed record StepContext
{
    /// <summary>當前迭代數。</summary>
    public required int Iteration { get; init; }
    /// <summary>最大迭代數。</summary>
    public required int MaxIterations { get; init; }
    /// <summary>工具名稱。</summary>
    public required string ToolName { get; init; }
    /// <summary>工具參數 JSON。</summary>
    public required string ToolArgs { get; init; }
    /// <summary>工具回傳結果。</summary>
    public required string ToolResult { get; init; }
    /// <summary>本步驟是否有文字回應。</summary>
    public required bool HasTextResponse { get; init; }
    /// <summary>前一步的工具名稱（null = 第一步）。</summary>
    public string? PreviousToolName { get; init; }
    /// <summary>前一步的工具參數。</summary>
    public string? PreviousToolArgs { get; init; }
    /// <summary>連續無文字回應的步數。</summary>
    public int ConsecutiveNoTextSteps { get; init; }
    /// <summary>同一工具連續失敗次數。</summary>
    public int ConsecutiveToolFailures { get; init; }
}

/// <summary>步驟評估結果。</summary>
public sealed record StepEvaluation
{
    /// <summary>評估等級。</summary>
    public required StepEvalLevel Level { get; init; }
    /// <summary>注入到對話歷史的提示訊息。</summary>
    public required string Hint { get; init; }
    /// <summary>觸發的規則名稱（供追蹤用）。</summary>
    public required string RuleName { get; init; }
}

/// <summary>步驟評估等級。</summary>
public enum StepEvalLevel
{
    /// <summary>提示（不影響執行）。</summary>
    Hint,
    /// <summary>警告（記錄但不阻斷）。</summary>
    Warning,
    /// <summary>阻斷（要求 Agent 換策略）。</summary>
    Block
}
