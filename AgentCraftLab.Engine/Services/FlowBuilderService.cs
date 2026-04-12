using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Strategies;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentCraftLab.Engine.Services;

/// <summary>
/// 透過 LLM 將自然語言描述轉換為 Workflow 規格（AI Build 模式）。
/// </summary>
public sealed class FlowBuilderService
{
    private readonly ILogger<FlowBuilderService> _logger;
    private readonly ConcurrentDictionary<string, object> _clientCache = new();
    private readonly string _systemPrompt;
    private readonly string[] _toolIds;

    public FlowBuilderService(
        ILogger<FlowBuilderService> logger,
        ToolRegistryService toolRegistry,
        SkillRegistryService skillRegistry)
    {
        _logger = logger;

        // 快取工具 ID 清單（供 JS 白名單注入）
        var tools = toolRegistry.GetAvailableTools();
        _toolIds = tools.Select(t => t.Id).ToArray();

        // 從 Registry 動態組建 System Prompt（Singleton 建構時執行一次）
        // NODE_SPECS 先注入 — 它本身含 PROVIDERS/TOOLS/SKILLS 巢狀 placeholder，
        // 下面幾個 Replace 會把展開後的 markdown 內的 placeholder 再替換一次。
        var providersSection = BuildProvidersSection();
        var toolsSection = BuildToolsSection(tools);
        var skillsSection = BuildSkillsSection(skillRegistry.GetAvailableSkills());
        _systemPrompt = PromptTemplate
            .Replace("{NODE_SPECS}", NodeSpecRegistry.BuildMarkdownSection())
            .Replace("{PROVIDERS_SECTION}", providersSection)
            .Replace("{TOOLS_SECTION}", toolsSection)
            .Replace("{SKILLS_SECTION}", skillsSection);
    }

    /// <summary>
    /// 串流呼叫 LLM，將使用者自然語言轉換為 Workflow JSON 規格。
    /// </summary>
    public async IAsyncEnumerable<string> GenerateFlowAsync(
        FlowBuildRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Credential.ApiKey) && !Providers.IsKeyOptional(request.Provider))
        {
            yield return $"\n\n❌ 尚未設定 API Key，請先在設定中填入 {request.Provider} 的金鑰。";
            yield break;
        }

        var chatClient = CreateChatClient(request);
        var messages = BuildMessages(request);

        _logger.LogInformation("[FlowBuilder] 開始生成 workflow，使用模型 {Model}", request.Model);

        var responseLength = 0;
        await foreach (var update in chatClient.GetStreamingResponseAsync(messages, cancellationToken: cancellationToken))
        {
            if (update.Text is { Length: > 0 } text)
            {
                responseLength += text.Length;
                yield return text;
            }
        }

        _logger.LogInformation("[FlowBuilder] 生成完成，回應長度 {Length}", responseLength);
    }

    private IChatClient CreateChatClient(FlowBuildRequest request)
    {
        var provider = request.Provider ?? Providers.OpenAI;
        var (apiKey, endpoint) = AgentContextBuilder.NormalizeCredential(
            provider, request.Credential.ApiKey, request.Credential.Endpoint ?? "");
        var cacheKey = $"{provider}:{endpoint}";
        var baseClient = _clientCache.GetOrAdd(cacheKey, _ =>
        {
            var timeout = TimeSpan.FromMinutes(Timeouts.LlmNetworkTimeoutMinutes);
            return AgentContextBuilder.CreateLlmClient(provider, apiKey, endpoint, timeout);
        });

        return AgentContextBuilder.GetChatClientFromBase(baseClient, request.Model);
    }

    private List<ChatMessage> BuildMessages(FlowBuildRequest request)
    {
        // 根據 locale 注入語言指令到 system prompt 末尾
        var localeInstruction = GetLocaleInstruction(request.Locale);
        var fullSystemPrompt = _systemPrompt + "\n\n" + localeInstruction;

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, fullSystemPrompt)
        };

        // 加入對話歷史
        foreach (var entry in request.History)
        {
            var role = entry.Role == "assistant" ? ChatRole.Assistant : ChatRole.User;
            messages.Add(new ChatMessage(role, entry.Text));
        }

        // 目前畫布狀態
        var userContent = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(request.CurrentWorkflowJson))
        {
            userContent.AppendLine("[Current canvas Workflow (execution payload format)]");
            userContent.AppendLine("```json");
            userContent.AppendLine(request.CurrentWorkflowJson);
            userContent.AppendLine("```");
            userContent.AppendLine("Modify based on this, output the complete new Workflow JSON (use index-based connections, not id-based).");
            userContent.AppendLine();
        }

        userContent.Append(request.UserMessage);
        messages.Add(new ChatMessage(ChatRole.User, userContent.ToString()));

        return messages;
    }

    /// <summary>
    /// 根據使用者 locale 產生語言指令 — 告訴 LLM 用什麼語言回覆和撰寫 agent instructions。
    /// </summary>
    private static string GetLocaleInstruction(string locale) => locale switch
    {
        "en" => """
            ## Language
            - Respond in **English** for your design explanation.
            - Write agent `instructions` in **English**.
            - Do NOT use Chinese or Japanese in your response or in agent instructions.
            """,
        "ja" => """
            ## Language
            - Respond in **Japanese (日本語)** for your design explanation.
            - Write agent `instructions` in **Japanese**, ending with 「日本語で回答してください。」
            - Do NOT use Chinese in your response.
            """,
        _ => """
            ## Language
            - Respond in **Traditional Chinese (繁體中文)** for your design explanation.
            - Write agent `instructions` in **Traditional Chinese**, ending with 「請使用繁體中文回答。」
            - If the user explicitly requests a specific language for an agent, write that agent's instructions in the requested language.
            """
    };

    // ─── System Prompt（動態組建） ───

    /// <summary>
    /// 從 Providers.Catalog 產生 provider/model 列表文字。
    /// </summary>
    private static string BuildProvidersSection()
    {
        var sb = new StringBuilder();
        foreach (var (id, (_, models)) in Providers.Catalog)
        {
            var suffix = id == Providers.OpenAI ? "（預設）" : "";
            sb.AppendLine($"  - `{id}`{suffix}: {string.Join(", ", models)}");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 從 ToolRegistryService 產生工具列表文字。
    /// </summary>
    private static string BuildToolsSection(IReadOnlyList<ToolDefinition> tools)
    {
        var sb = new StringBuilder();
        foreach (var t in tools)
        {
            sb.AppendLine($"  - `{t.Id}` — {t.Description}");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 從 SkillRegistryService 產生 skill 列表文字。
    /// </summary>
    private static string BuildSkillsSection(IReadOnlyList<SkillDefinition> skills)
    {
        var sb = new StringBuilder();
        foreach (var s in skills)
        {
            var toolsSuffix = s.Tools is { Count: > 0 }
                ? $"（自帶 {string.Join(" + ", s.Tools)}）"
                : "";
            sb.AppendLine($"  - `{s.Id}` — {s.DisplayName}{toolsSuffix}");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 取得所有合法的工具 ID（供 JS 端白名單使用）。
    /// </summary>
    public IReadOnlyList<string> GetToolIds() => _toolIds;

    private const string PromptTemplate = """
You are the AgentCraftLab Workflow design assistant. Users describe their desired Agent workflow in natural language, and you design and output a Workflow specification JSON.

## Your Task

1. Understand the user's requirements
2. Design an appropriate Agent Workflow (choose correct node types and connections)
3. Explain your design rationale in natural language first
4. Output a JSON block (wrapped in ```json) that conforms to the spec below

## Node Type Specs

{NODE_SPECS}

## Output JSON Format (Schema v2)

Node fields go directly on the node object (do NOT wrap in `"data": {...}`).
**IMPORTANT**: `model` MUST be a nested object `{ "provider": "...", "model": "..." }`, NOT a string.

```json
{
  "nodes": [
    { "type": "agent", "name": "Researcher", "instructions": "...", "model": { "provider": "openai", "model": "gpt-4o" }, "tools": ["web_search"] },
    { "type": "agent", "name": "Writer", "instructions": "...", "model": { "provider": "openai", "model": "gpt-4o" } }
  ],
  "connections": [
    { "from": 0, "to": 1 },
    { "from": 1, "to": 2, "fromOutput": "output_2" }
  ]
}
```

### Connection Rules
- `from` and `to` are 0-based indices into the nodes array
- `fromOutput` defaults to "output_1". Multi-output nodes need explicit specification:
  - condition: output_1=True branch, output_2=False branch
  - loop: output_1=Body (continue), output_2=Exit (break out)
  - router: output_1/output_2/output_3 correspond to routes
  - human (approval): output_1=Approve, output_2=Reject
- Start and End nodes are auto-generated — do NOT include them in nodes
- The system auto-connects Start → first node and last node → End
- Do NOT output `workflowSettings` — the system handles it

## Examples

### Example 1: Sequential Pipeline
User: "Build a research → draft → edit pipeline"
```json
{
  "nodes": [
    { "type": "agent", "name": "Researcher", "instructions": "You are a researcher. Produce a key summary on the user's topic." },
    { "type": "agent", "name": "Drafter", "instructions": "You are a writer. Write a well-structured article draft based on the research summary." },
    { "type": "agent", "name": "Editor", "instructions": "You are an editor. Polish the text, ensure clarity and correct grammar, output the final version." }
  ],
  "connections": [
    { "from": 0, "to": 1 },
    { "from": 1, "to": 2 }
  ]
}
```

### Example 2: Review Loop
User: "Write an article, then review it, rewrite if not approved, max 3 times"
```json
{
  "nodes": [
    { "type": "agent", "name": "Writer", "instructions": "You are a writer. Write content on the given topic." },
    { "type": "loop", "name": "ReviewLoop", "condition": { "kind": "contains", "value": "APPROVED" }, "maxIterations": 3 },
    { "type": "agent", "name": "Reviewer", "instructions": "You are a reviewer. Review the draft. Reply APPROVED if good, otherwise provide revision suggestions." },
    { "type": "agent", "name": "Publisher", "instructions": "You are a publisher. Format and publish the approved content." }
  ],
  "connections": [
    { "from": 0, "to": 1 },
    { "from": 1, "to": 2, "fromOutput": "output_1" },
    { "from": 1, "to": 3, "fromOutput": "output_2" }
  ]
}
```

### Example 3: Customer Service Router
User: "Build a customer service system with billing, technical, and general categories"
```json
{
  "nodes": [
    { "type": "agent", "name": "Triage", "instructions": "You are a triage agent. Understand customer needs and route to the appropriate specialist." },
    { "type": "router", "name": "Router", "routes": [{ "name": "billing", "keywords": ["billing", "charge", "refund"], "isDefault": false }, { "name": "technical", "keywords": ["bug", "error", "technical"], "isDefault": false }, { "name": "general", "keywords": [], "isDefault": true }] },
    { "type": "agent", "name": "Billing", "instructions": "You handle billing-related inquiries." },
    { "type": "agent", "name": "Technical", "instructions": "You handle technical support inquiries." },
    { "type": "agent", "name": "General", "instructions": "You handle general inquiries." }
  ],
  "connections": [
    { "from": 0, "to": 1 },
    { "from": 1, "to": 2, "fromOutput": "output_1" },
    { "from": 1, "to": 3, "fromOutput": "output_2" },
    { "from": 1, "to": 4, "fromOutput": "output_3" }
  ]
}
```

## Important Rules

1. **Agent names**: Use English names (e.g., Researcher, Writer, Reviewer)
2. **JSON must be valid**: Ensure JSON can be correctly parsed
3. **Connection logic**: from/to indices must not exceed the nodes array bounds
4. **Keep it simple**: Do not over-engineer. Use the minimum nodes to accomplish the task
5. **Incremental modification**: If the user provides the current canvas state, modify based on it. Note: canvas payload connections use node IDs (e.g., "3", "4"), but your output must use 0-based **indices** into the nodes array. Understand the existing workflow structure and produce the complete modified nodes + connections.
6. **Always include complete JSON**: Every response must end with a ```json block containing `{ "nodes": [...], "connections": [...] }`. Never describe changes without outputting JSON.
7. **One JSON block**: Include exactly one ```json block, placed at the end of your response.

## Design Principles

### Tool Auto-Recommendation
For each Agent, evaluate: does this task need real-time or up-to-date information?
- **Yes** (regulations, financial reports, stock prices, news, market trends, competitor analysis) → MUST include a search tool (e.g., `web_search` or `azure_web_search`). Without tools, the agent relies on stale training data.
- **No** (pure reasoning, writing, translation, formatting, creative work) → do NOT add tools.

### Parallel Must Have Synthesizer
After a parallel node, ALWAYS add a Synthesizer Agent to merge all branch results. The Synthesizer does NOT need search tools (it only processes provided data).

### Parallel Example (with tools + Synthesizer)
User: "Build a parallel analysis with legal, technical, and financial experts"
```json
{
  "nodes": [
    { "type": "parallel", "name": "ExpertAnalysis",
      "branches": [
        { "name": "Legal", "goal": "You are a legal expert. Analyze the input for legal risks and compliance recommendations.", "tools": ["web_search"] },
        { "name": "Technical", "goal": "You are a technical expert. Analyze the input for technical feasibility and challenges.", "tools": ["web_search"] },
        { "name": "Financial", "goal": "You are a financial expert. Analyze the input for financial risks and cost-benefit.", "tools": ["web_search"] }
      ],
      "merge": "labeled"
    },
    { "type": "agent", "name": "Synthesizer", "instructions": "You are a senior analyst. Synthesize the legal, technical, and financial expert analyses into a comprehensive assessment report with key findings, risk matrix, and action recommendations." }
  ],
  "connections": [
    { "from": 0, "to": 1 }
  ]
}
```
""";
}
