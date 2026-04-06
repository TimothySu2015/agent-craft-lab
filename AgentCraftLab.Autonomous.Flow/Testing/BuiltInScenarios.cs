using AgentCraftLab.Engine.Models;

namespace AgentCraftLab.Autonomous.Flow.Testing;

/// <summary>
/// 內建測試情境 — 涵蓋所有節點類型。
/// </summary>
public static class BuiltInScenarios
{
    public static IReadOnlyList<FlowTestScenario> All { get; } =
    [
        new()
        {
            Id = "pure-reasoning",
            Name = "純推理（零工具）",
            Goal = "比較微服務架構和單體架構的優缺點，用表格呈現，並給出 3 個適合用微服務、3 個適合用單體的場景",
            ExpectedNodeTypes = [NodeTypes.Agent],
            ExpectedToolCallRange = (0, 2),
            MaxIterations = 10,
            MaxTotalTokens = 50_000
        },
        new()
        {
            Id = "parallel-code",
            Name = "Parallel + Code（5 分支搜尋）",
            Goal = "以下是 5 個程式語言：Python, Rust, Go, TypeScript, C#。對每個語言搜尋 2026 年最新的重大更新，各寫 100 字摘要",
            ExpectedNodeTypes = [NodeTypes.Parallel],
            ExpectedToolCallRange = (3, 30),
            MaxIterations = 15,
            MaxTotalTokens = 100_000,
            RequiredTools = ["azure_web_search"]
        },
        new()
        {
            Id = "condition-branch",
            Name = "Condition 分支",
            Goal = "搜尋 Tesla 今天的股價，如果跌超過 2% 就分析下跌原因並找出相關新聞，否則給出簡短的市場概要",
            ExpectedNodeTypes = [NodeTypes.Condition, NodeTypes.Agent],
            ExpectedToolCallRange = (1, 20),
            MaxIterations = 15,
            MaxTotalTokens = 80_000,
            RequiredTools = ["azure_web_search"]
        },
        new()
        {
            Id = "loop-refine",
            Name = "Loop 迭代改進",
            Goal = "寫一篇關於 AI Agent 的技術文章，500 字，寫完後自我審查，如果品質不夠好就改進，最多改 3 次",
            ExpectedNodeTypes = [NodeTypes.Agent],
            ExpectedToolCallRange = (0, 15),
            MaxIterations = 20,
            MaxTotalTokens = 120_000
        },
        new()
        {
            Id = "parallel-multilang",
            Name = "Parallel 多語言輸出",
            Goal = "搜尋台灣和日本今天的天氣，然後根據天氣建議今天適合的戶外活動，最後用中文和日文各寫一份旅遊建議",
            ExpectedNodeTypes = [NodeTypes.Parallel, NodeTypes.Agent],
            ExpectedToolCallRange = (1, 20),
            MaxIterations = 15,
            MaxTotalTokens = 100_000,
            RequiredTools = ["azure_web_search"]
        },
        new()
        {
            Id = "complex-flow",
            Name = "複合流程（Parallel + Condition + Agent）",
            Goal = "分析 NVIDIA 和 AMD 這兩家公司，搜尋今天的股價和最近一週的重大新聞，比較兩家在 AI 晶片市場的競爭優勢，如果 NVIDIA 股價高於 AMD 的 3 倍就重點分析 NVIDIA 的護城河，否則分析 AMD 的追趕策略，最後用 500 字寫一篇投資分析報告",
            ExpectedNodeTypes = [NodeTypes.Parallel, NodeTypes.Agent],
            ExpectedToolCallRange = (2, 30),
            MaxIterations = 20,
            MaxTotalTokens = 150_000,
            RequiredTools = ["azure_web_search"]
        },
        new()
        {
            Id = "node-reference",
            Name = "跨節點引用（{{node:}} 語法）",
            Goal = "Step 1: 列出 3 個最受歡迎的程式語言，用 JSON 陣列格式輸出（例如 [\"Python\", \"JavaScript\", \"Java\"]）。\n" +
                   "Step 2: 為每個語言寫一句話描述其主要用途。\n" +
                   "Step 3: 結合 Step 1 的原始清單和 Step 2 的描述，產出一份 Markdown 表格，欄位為「語言」和「主要用途」。\n" +
                   "注意：Step 3 需要同時引用 Step 1 和 Step 2 的輸出，不能只用 Step 2 的結果。",
            ExpectedNodeTypes = [NodeTypes.Agent],
            ExpectedToolCallRange = (0, 5),
            MaxIterations = 10,
            MaxTotalTokens = 50_000
        }
    ];
}
