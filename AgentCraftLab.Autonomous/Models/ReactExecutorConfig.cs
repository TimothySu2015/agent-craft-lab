namespace AgentCraftLab.Autonomous.Models;

/// <summary>
/// Autonomous Agent 執行配置 — 集中管理 ReAct 迴圈的可調參數。
/// 所有屬性都有合理的預設值，可透過 DI 或 AutonomousRequest 覆寫。
/// </summary>
public record ReactExecutorConfig
{
    // ═══════════════════════════════════════════
    // 預算提醒
    // ═══════════════════════════════════════════

    /// <summary>預算提醒的注入間隔（每 N 步）。</summary>
    public int BudgetReminderInterval { get; init; } = 5;

    /// <summary>接近迴圈結束時的步數門檻（最後 N 步強制提醒）。</summary>
    public int FinalStepsThreshold { get; init; } = 3;

    /// <summary>事中自我檢查的觸發間隔（每 N 步）。</summary>
    public int MidExecutionCheckInterval { get; init; } = 8;

    // ═══════════════════════════════════════════
    // 收斂偵測
    // ═══════════════════════════════════════════

    /// <summary>相似度門檻：超過此值視為「結果相似」。</summary>
    public double ConvergenceSimilarityThreshold { get; init; } = 0.80;

    /// <summary>收斂偵測所需的最少歷史記錄筆數。</summary>
    public int ConvergenceMinHistory { get; init; } = 3;

    /// <summary>工具呼叫結果片段的最大長度（節省記憶體）。</summary>
    public int ConvergenceSnippetMaxLength { get; init; } = 200;

    // ═══════════════════════════════════════════
    // Sub-agent
    // ═══════════════════════════════════════════

    /// <summary>持久 Sub-agent 數量上限。</summary>
    public int MaxSubAgents { get; init; } = 10;

    /// <summary>臨時 spawn 任務並行上限（獨立於 MaxSubAgents）。</summary>
    public int MaxSpawnTasks { get; init; } = 15;

    /// <summary>Spawn 預設超時秒數（0 = 無超時，由全局 token budget 控制）。</summary>
    public int SpawnDefaultTimeoutSeconds { get; init; } = 120;

    /// <summary>Spawn 巢狀深度上限。1 = 只有頂層可 spawn（預設），2 = spawn worker 可再 spawn 子 worker（硬上限，不可超過 2）。</summary>
    public int MaxSpawnDepth { get; init; } = 1;

    /// <summary>Orchestrator 模式 spawn worker 的最大 tool call 迭代數（需容納 spawn→collect→answer 流程）。</summary>
    public int OrchestratorMaxIterations { get; init; } = 5;

    /// <summary>Spawn worker 完成 LLM 呼叫後等待新訊息的寬限期（毫秒）。0 = 不等待（與舊行為相同）。</summary>
    public int SpawnMessageGraceMs { get; init; } = 1000;

    /// <summary>Sub-agent 回應的最大字元長度。</summary>
    public int SubAgentMaxResponseLength { get; init; } = 2000;

    /// <summary>Sub-agent 歷史裁剪門檻：超過此數量才裁剪。</summary>
    public int SubAgentHistoryTrimThreshold { get; init; } = 20;

    /// <summary>Sub-agent 歷史裁剪後保留的近期訊息數。</summary>
    public int SubAgentHistoryKeepRecent { get; init; } = 15;

    // ═══════════════════════════════════════════
    // 歷史壓縮（三層策略：截斷 → 本地壓縮 → LLM 摘要）
    // ═══════════════════════════════════════════

    /// <summary>壓縮觸發的 token 門檻（以字元數 / CharsPerTokenEstimate 估算）。預設 30000 tokens。</summary>
    public long HistoryCompressionTokenThreshold { get; init; } = 30_000;

    /// <summary>向下相容：訊息數門檻（token 門檻未達但訊息數超過此值仍觸發）。</summary>
    public int HistoryCompressionThreshold { get; init; } = 20;

    /// <summary>LLM 壓縮時保留的近期訊息數。</summary>
    public int HistoryRecentMessageCount { get; init; } = 8;

    /// <summary>本地壓縮的目標訊息數。</summary>
    public int HistoryLocalTargetCount { get; init; } = 12;

    /// <summary>短訊息閾值：低於此字元數的同角色連續訊息會被合併。</summary>
    public int HistoryShortMessageThreshold { get; init; } = 100;

    /// <summary>Layer 1：工具結果的截斷字元數上限（超過此長度的 ToolResult 直接截斷）。</summary>
    public int ToolResultTruncateLength { get; init; } = 1500;

    // ═══════════════════════════════════════════
    // Token 估算
    // ═══════════════════════════════════════════

    /// <summary>字元轉 Token 的粗略估算比率（每 N 個字元約 1 token）。</summary>
    public int CharsPerTokenEstimate { get; init; } = 4;

    // ═══════════════════════════════════════════
    // 計劃
    // ═══════════════════════════════════════════

    /// <summary>規劃用的模型（強模型，只呼叫一次）。null = 使用與執行相同的模型。</summary>
    public string? PlannerModel { get; init; } = "gpt-4o";

    /// <summary>規劃時列出的工具名稱上限。</summary>
    public int PlanMaxToolsInPrompt { get; init; } = 20;

    /// <summary>計劃文字的最大長度（防止 LLM 產出過多內容）。</summary>
    public int PlanMaxLength { get; init; } = 800;

    /// <summary>規劃 LLM 呼叫的 MaxOutputTokens 限制（加速生成，計劃本身不需長回應）。</summary>
    public int PlanMaxOutputTokens { get; init; } = 200;

    // ═══════════════════════════════════════════
    // 記憶
    // ═══════════════════════════════════════════

    /// <summary>查詢相似經驗的預設筆數。</summary>
    public int MemoryMaxExperiences { get; init; } = 3;

    /// <summary>注入 prompt 的最大字元數，避免 prompt 膨脹。</summary>
    public int MemoryMaxPromptLength { get; init; } = 1500;

    /// <summary>關鍵字萃取的最大詞數。</summary>
    public int MemoryMaxKeywords { get; init; } = 15;

    /// <summary>每 N 次儲存觸發一次清理（機率式清理）。</summary>
    public int MemoryCleanupProbability { get; init; } = 10;

    /// <summary>記憶寫入超時秒數。</summary>
    public int MemoryWriteTimeoutSeconds { get; init; } = 30;

    // ═══════════════════════════════════════════
    // 檢查點持久化
    // ═══════════════════════════════════════════

    /// <summary>啟用完整狀態檢查點持久化（預設關閉，向後相容）。</summary>
    public bool CheckpointEnabled { get; init; }

    /// <summary>檢查點儲存間隔（每 N 步儲存一次）。</summary>
    public int CheckpointInterval { get; init; } = 5;

    /// <summary>檢查點最大保留時間（小時）。超過後自動清理。預設 24 小時。</summary>
    public int CheckpointRetentionHours { get; init; } = 24;

    // ═══════════════════════════════════════════
    // 多層記憶（Entity + Contextual）
    // ═══════════════════════════════════════════

    /// <summary>啟用實體記憶（從執行結果抽取實體與事實）。</summary>
    public bool EntityMemoryEnabled { get; init; }

    /// <summary>啟用情境記憶（聚合使用者互動模式）。</summary>
    public bool ContextualMemoryEnabled { get; init; }

    /// <summary>每次執行最多抽取的實體數。</summary>
    public int EntityMaxPerExecution { get; init; } = 10;

    /// <summary>情境模式注入 prompt 的最大字元數。</summary>
    public int ContextualMaxPromptLength { get; init; } = 300;

    /// <summary>實體記憶注入 prompt 的最大字元數。</summary>
    public int EntityMaxPromptLength { get; init; } = 500;

    /// <summary>情境聚合觸發機率（每 N 次執行觸發一次）。</summary>
    public int ContextualAggregationProbability { get; init; } = 5;

    // ═══════════════════════════════════════════
    // 平行 Guardrails
    // ═══════════════════════════════════════════

    /// <summary>啟用平行 Guardrails 模式（ReAct 迴圈中 guardrails 與 LLM 平行執行）。</summary>
    public bool ParallelGuardRails { get; init; }

    // ═══════════════════════════════════════════
    // Tool Search（按需載入）
    // ═══════════════════════════════════════════

    /// <summary>啟用 Tool Search 按需載入模式（預設關閉，向後相容）。</summary>
    public bool ToolSearchEnabled { get; init; }

    /// <summary>search_tools 預設回傳的最大結果數。</summary>
    public int ToolSearchMaxResults { get; init; } = 5;

    // ═══════════════════════════════════════════
    // Middleware
    // ═══════════════════════════════════════════

    /// <summary>Orchestrator 的 middleware 管線（逗號分隔）。預設含 recovery 以支援 output 截斷恢復和 context overflow 壓縮。</summary>
    public string OrchestratorMiddleware { get; init; } = "logging,retry,recovery";
}
