# AgentCraftLab 擴充指南

本文件說明如何為 AgentCraftLab 新增各種擴充功能。每個擴充點皆附上步驟與程式碼範例。

---

## 1. 新增節點類型

以新增一個 `timer` 節點為例，需修改四個位置。

### 步驟 1：NodeTypes 加常數

檔案：`AgentCraftLab.Engine/Models/Constants.cs`

```csharp
public static class NodeTypes
{
    // ... 現有常數 ...
    public const string Timer = "timer";  // <-- 新增
}
```

### 步驟 2：NodeTypeRegistry 加一行

同一檔案的 `NodeTypeRegistry.Registry` 字典中加入 metadata：

```csharp
private static readonly Dictionary<string, NodeTypeInfo> Registry = new(StringComparer.OrdinalIgnoreCase)
{
    // ... 現有項目 ...
    [NodeTypes.Timer] = new(NodeTypes.Timer, IsExecutable: true, RequiresImperative: true),
};
```

各旗標說明：
- `IsExecutable`：節點會被執行（非 meta/data 節點）
- `RequiresImperative`：需要 Imperative 策略（有流程控制邏輯的節點都需要）
- `IsAgentLike`：行為類似 Agent（有 LLM 呼叫）
- `IsMeta`：meta 節點（start/end）
- `IsDataNode`：資料節點（rag）

### 步驟 3：NodeExecutorRegistry 加 handler

建立 `AgentCraftLab.Engine/Strategies/NodeExecutors/TimerNodeExecutor.cs`：

```csharp
namespace AgentCraftLab.Engine.Strategies.NodeExecutors;

public sealed class TimerNodeExecutor : INodeExecutor
{
    public string NodeType => NodeTypes.Timer;

    public async IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        string nodeId,
        WorkflowNode node,
        ImperativeExecutionState state,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var delayMs = int.TryParse(node.ConditionExpression, out var ms) ? ms : 1000;

        yield return new ExecutionEvent(EventTypes.AgentStarted, node.Name, $"Timer: waiting {delayMs}ms");
        await Task.Delay(delayMs, cancellationToken);
        yield return new ExecutionEvent(EventTypes.AgentCompleted, node.Name, $"Timer completed after {delayMs}ms");
    }
}
```

在 DI 容器中註冊（通常在 `AddAgentCraftEngine()` 擴展方法中）：

```csharp
services.AddSingleton<INodeExecutor, TimerNodeExecutor>();
```

`NodeExecutorRegistry` 會自動透過 `IEnumerable<INodeExecutor>` 收集所有實作。

### 步驟 4：JS NODE_REGISTRY 加前端渲染

檔案：`AgentCraftLab.Web/src/components/studio/nodes/registry.ts`

```typescript
import { Timer } from 'lucide-react'

export const NODE_REGISTRY: Record<NodeType, NodeTypeConfig> = {
  // ... 現有項目 ...
  timer: {
    type: 'timer',
    labelKey: 'node.timer',
    icon: Timer,
    color: 'orange',
    inputs: 1,
    outputs: 1,
    defaultData: (name) => ({
      type: 'timer', name, conditionExpression: '1000',
    }),
  },
}
```

同時需要在 `AgentCraftLab.Web/src/types/workflow.ts` 的 `NodeType` 聯合型別中加入 `'timer'`，並建立對應的 React 節點元件。

---

## 2. 新增內建工具

內建工具由 `ToolRegistryService` 管理，實作邏輯放在 `ToolImplementations`。

### 步驟 1：ToolImplementations 加方法

檔案：`AgentCraftLab.Engine/Services/ToolImplementations.cs`

```csharp
public static string Base64Encode(
    [Description("要編碼的文字")] string text)
{
    return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text));
}
```

### 步驟 2：ToolRegistryService 加 Register()

在對應的 `RegisterXxxTools()` 方法中加入：

```csharp
private void RegisterUtilityTools()
{
    // ... 現有工具 ...

    Register("base64_encode", "Base64 Encode", "將文字編碼為 Base64 字串",
        () => AIFunctionFactory.Create(
            ToolImplementations.Base64Encode,
            name: "Base64Encode",
            description: "將文字編碼為 Base64 字串"),
        ToolCategory.Utility, "&#x1F511;");
}
```

`Register()` 參數說明：
- `id`：唯一識別碼（snake_case）
- `displayName`：UI 顯示名稱
- `description`：工具描述
- `factory`：`Func<AITool>` 工廠
- `category`：分類（Search / Utility / Web / Data）
- `icon`：HTML 實體圖示
- `requiredCredential`：需要的憑證 provider（選填）
- `credentialFactory`：有憑證時的替代工廠（選填）

若工具需要憑證，提供 `credentialFactory`：

```csharp
Register("my_api", "My API", "呼叫外部 API",
    () => AIFunctionFactory.Create(/* 無憑證版本 */),
    ToolCategory.Web, "&#x1F310;", "my-provider",
    credentialFactory: creds =>
    {
        var key = creds["my-provider"].ApiKey;
        return AIFunctionFactory.Create(/* 有憑證版本 */);
    });
```

AI Build 會自動從 ToolRegistryService 讀取工具清單，無需額外設定。

---

## 3. 新增執行策略

### 步驟 1：實作 IWorkflowStrategy

檔案：`AgentCraftLab.Engine/Strategies/IWorkflowStrategy.cs` 定義介面：

```csharp
public interface IWorkflowStrategy
{
    IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        WorkflowStrategyContext context,
        CancellationToken cancellationToken);
}
```

建立新策略：

```csharp
public class PriorityWorkflowStrategy : IWorkflowStrategy
{
    public async IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        WorkflowStrategyContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // 依優先度排序 agent 節點
        var sorted = context.AgentNodes
            .OrderBy(n => n.Priority)
            .ToList();

        foreach (var node in sorted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new ExecutionEvent(EventTypes.AgentStarted, node.Name);

            var agent = context.AgentContext.Agents[node.Name];
            var response = await agent.GetResponseAsync(/* ... */);
            yield return new ExecutionEvent(EventTypes.AgentCompleted, node.Name, response.Text);
        }
    }
}
```

### 步驟 2：WorkflowStrategyResolver.Resolve() 加 case

檔案：`AgentCraftLab.Engine/Services/WorkflowStrategyResolver.cs`

```csharp
return (workflowType switch
{
    WorkflowTypes.Sequential => new SequentialWorkflowStrategy(),
    WorkflowTypes.Concurrent => new ConcurrentWorkflowStrategy(),
    WorkflowTypes.Handoff => new HandoffWorkflowStrategy(),
    WorkflowTypes.Imperative => CreateImperative(),
    "priority" => new PriorityWorkflowStrategy(),  // <-- 新增
    _ => throw new NotSupportedException(...)
}, $"detected:{workflowType}");
```

也需要在 `WorkflowTypes` 常數類別中加入對應常數。

---

## 4. 新增 Middleware

Middleware 以裝飾者模式包裝 `IChatClient`，在 Agent 的 LLM 呼叫前後注入邏輯。

### 步驟 1：繼承 DelegatingChatClient

```csharp
using Microsoft.Extensions.AI;

public class CachingChatClient : DelegatingChatClient
{
    public CachingChatClient(IChatClient innerClient) : base(innerClient) { }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = ComputeHash(messages);
        if (TryGetCached(cacheKey, out var cached))
            return cached;

        var response = await base.GetResponseAsync(messages, options, cancellationToken);
        SetCache(cacheKey, response);
        return response;
    }
}
```

### 步驟 2：ApplyMiddleware() 加 case

檔案：`AgentCraftLab.Engine/Strategies/AgentContextBuilder.cs`

```csharp
public static IChatClient ApplyMiddleware(IChatClient client, string? middleware,
    Dictionary<string, Dictionary<string, string>>? config = null)
{
    // ... 現有 middleware ...

    if (set.Contains("caching"))
        client = new CachingChatClient(client);  // <-- 新增

    return client;
}
```

Middleware 名稱即為 UI 上 Agent 節點 `middleware` 欄位中逗號分隔的值。

### 替換現有偵測引擎（進階）

GuardRails 和 PII 都透過介面解耦，可替換偵測邏輯而不修改 Middleware：

**替換 GuardRails 規則引擎：**

```csharp
// 實作 IGuardRailsPolicy 介面
public class AzureContentSafetyPolicy : IGuardRailsPolicy
{
    public IReadOnlyList<GuardRailsMatch> Evaluate(string text, GuardRailsDirection direction)
    {
        // 呼叫 Azure Content Safety API
    }
}

// DI 替換
services.AddSingleton<IGuardRailsPolicy, AzureContentSafetyPolicy>();
```

**替換 PII 偵測器：**

```csharp
// 實作 IPiiDetector 介面
public class PresidioPiiDetector : IPiiDetector
{
    public IReadOnlyList<PiiEntity> Detect(string text, double confidenceThreshold = 0.5)
    {
        // 呼叫 Presidio REST API
    }
}

// DI 替換
services.AddSingleton<IPiiDetector, PresidioPiiDetector>();
```

**替換 PII Token 保管庫（例如 Redis）：**

```csharp
public class RedisPiiTokenVault : IPiiTokenVault
{
    public string Tokenize(string sessionKey, string originalValue, PiiEntityType type) { /* Redis SET */ }
    public string Detokenize(string sessionKey, string text) { /* Redis GET */ }
    public void ClearSession(string sessionKey) { /* Redis DEL */ }
}

services.AddSingleton<IPiiTokenVault, RedisPiiTokenVault>();
```

---

## 5. 新增 Flow 節點

Flow 節點用於 Autonomous Flow 的結構化執行（LLM 規劃節點序列）。

### 步驟 1：FlowNodeRunner 加 case

檔案：`AgentCraftLab.Autonomous.Flow/Services/FlowNodeRunner.cs`

```csharp
public async IAsyncEnumerable<ExecutionEvent> ExecuteNodeAsync(
    PlannedNode node, string input, GoalExecutionRequest request,
    CancellationToken cancellationToken)
{
    switch (node.NodeType)
    {
        // ... 現有 case ...

        case NodeTypes.Timer:
            yield return ExecuteTimerNode(node, input);
            break;
    }
}

private static ExecutionEvent ExecuteTimerNode(PlannedNode node, string input)
{
    var delayMs = node.Config.MaxIterations ?? 1000;
    Thread.Sleep(delayMs);  // Flow 節點中的簡易實作
    return new ExecutionEvent(EventTypes.NodeCompleted, node.Name, input);
}
```

### 步驟 2：FlowPlanValidator.SupportedNodeTypes

檔案：`AgentCraftLab.Autonomous.Flow/Services/FlowPlanValidator.cs`

```csharp
private static readonly HashSet<string> SupportedNodeTypes =
[
    NodeTypes.Agent, NodeTypes.Code, NodeTypes.Condition,
    NodeTypes.Iteration, NodeTypes.Parallel, NodeTypes.Loop,
    NodeTypes.HttpRequest,
    NodeTypes.Timer,  // <-- 新增
];
```

### 步驟 3：FlowPlannerPrompt 更新

在 Planner 的 system prompt 中告訴 LLM 新節點的用途與約束，讓 LLM 能正確規劃使用。

### 步驟 4：WorkflowCrystallizer.StepToNode

檔案：`AgentCraftLab.Autonomous.Flow/Services/WorkflowCrystallizer.cs`

在 `FromConfig()` 方法中加入新節點的映射邏輯，確保 Flow 執行軌跡能正確凍結為 Workflow JSON。

---

## 6. 替換策略物件（Autonomous）

Autonomous Agent 的 ReAct 迴圈透過五個策略介面拆分職責，可獨立替換。

### 可替換的介面

| 介面 | 職責 | 預設實作 |
|------|------|----------|
| `IBudgetPolicy` | Token/ToolCall 預算檢查 | `DefaultBudgetPolicy` |
| `IHistoryManager` | 對話歷史管理與壓縮 | `HybridHistoryManager` |
| `IReflectionEngine` | 自我反思與審計 | `AuditorReflectionEngine` |
| `IToolDelegationStrategy` | 工具白名單與安全過濾 | `SafeWhitelistToolDelegation` |
| `IHumanInteractionHandler` | 人工互動處理 | `AgUiHumanInteractionHandler` |

### 替換範例

```csharp
// 1. 實作介面
public class StrictBudgetPolicy : IBudgetPolicy
{
    public ExecutionEvent? CheckBudget(
        TokenTracker tokenTracker, ToolCallTracker toolCallTracker)
    {
        if (tokenTracker.TotalTokens > 5000)
            return new ExecutionEvent(EventTypes.Error, "Budget", "Token limit exceeded");
        return null;
    }

    public void InjectBudgetReminder(
        List<ChatMessage> messages, ReactLoopState loopState,
        int iteration, int maxIterations,
        TokenTracker tokenTracker, ToolCallTracker toolCallTracker)
    {
        // 自訂 budget reminder 邏輯
    }

    public void InjectMidExecutionCheck(
        List<ChatMessage> messages, int iteration, int maxIterations)
    {
        // 自訂事中檢查邏輯
    }
}

// 2. DI Replace 註冊
services.Replace(ServiceDescriptor.Singleton<IBudgetPolicy, StrictBudgetPolicy>());
```

注意使用 `Replace` 而非 `Add`，確保覆蓋預設實作。預設實作在 `AddAutonomousAgentCore()` 中註冊。

---

## 7. 替換腳本引擎 / OCR 引擎

### 替換腳本引擎

介面定義於 `AgentCraftLab.Script/IScriptEngine.cs`：

```csharp
public interface IScriptEngine
{
    Task<ScriptResult> ExecuteAsync(string code, string input,
        ScriptOptions? options = null, CancellationToken cancellationToken = default);
}
```

**內建��擎：**

| 引擎 | 語言 | 說明 |
|------|------|------|
| `JintScriptEngine` | JavaScript | Jint JS 沙箱，天然隔離 + 四道資源限制 |
| `RoslynScriptEngine` | C# | 低階 CSharpCompilation + collectible ALC，AST 安全掃描 + References 白名單 |

**多語言工廠：** `IScriptEngineFactory` 按語言分派到對應引擎：

```csharp
// 新增語言支援
var factory = new ScriptEngineFactory()
    .Register("javascript", new JintScriptEngine())
    .Register("csharp", new RoslynScriptEngine())
    .Register("python", new PythonScriptEngine()); // 自訂引擎
```

**DI 註冊（推薦使用多語言模式）：**

```csharp
// 同時註冊 Jint + Roslyn，向後相容 IScriptEngine
builder.Services.AddMultiLanguageScript();
```

**替換單一引擎：**

```csharp
services.Replace(ServiceDescriptor.Singleton<IScriptEngine, PythonScriptEngine>());
```

**Roslyn C# 安全機制：** `RoslynCodeSanitizer` 在編譯前掃描 AST，攔截危險 API（File/Process/HttpClient/Assembly/Environment 等）。`BuildSafeReferences()` 只引用安全的 assembly（不含 System.IO.FileSystem、System.Net.Http）。每次執行使用 collectible `AssemblyLoadContext`，執行完即 Unload 避免記憶體洩漏。

### 替換 OCR 引擎

介面定義於 `AgentCraftLab.Ocr/IOcrEngine.cs`：

```csharp
public interface IOcrEngine
{
    Task<OcrResult> RecognizeAsync(byte[] imageData,
        IReadOnlyList<string>? languages = null,
        CancellationToken cancellationToken = default);
}
```

實作替代引擎：

```csharp
public class AzureVisionOcrEngine : IOcrEngine
{
    public async Task<OcrResult> RecognizeAsync(
        byte[] imageData, IReadOnlyList<string>? languages = null,
        CancellationToken cancellationToken = default)
    {
        // 呼叫 Azure Computer Vision API
        // 回傳 OcrResult { Text, Confidence }
    }
}
```

---

## 8. 擴展沙箱 API

沙箱 API 讓 Code 節點的 JS 腳本可呼叫受控的外部功能。

### 實作 ISandboxApi

介面定義於 `AgentCraftLab.Script/ISandboxApi.cs`：

```csharp
public interface ISandboxApi
{
    string Name { get; }
    IReadOnlyDictionary<string, Delegate> GetMethods();
}
```

實作範例 -- 加入 `crypto` 沙箱 API：

```csharp
public class CryptoSandboxApi : ISandboxApi
{
    public string Name => "crypto";

    public IReadOnlyDictionary<string, Delegate> GetMethods()
    {
        return new Dictionary<string, Delegate>
        {
            ["sha256"] = (string input) =>
            {
                var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
                return Convert.ToHexStringLower(bytes);
            },
            ["md5"] = (string input) =>
            {
                var bytes = MD5.HashData(Encoding.UTF8.GetBytes(input));
                return Convert.ToHexStringLower(bytes);
            },
        };
    }
}
```

DI 註冊：

```csharp
services.AddSingleton<ISandboxApi, CryptoSandboxApi>();
```

腳本引擎會自動透過 DI 收集所有 `ISandboxApi` 實作，並注入到腳本的全域範圍。在 JS 腳本中即可使用：

```javascript
var hash = crypto.sha256(input);
result = hash;
```

`Name` 屬性決定腳本中的全域物件名稱，`GetMethods()` 回傳的 key 則是該物件上的方法名稱。

---

## 速查表

| 擴充類型 | 修改檔案 |
|----------|----------|
| 新節點 | `Constants.cs` + `NodeExecutor` + `registry.ts` |
| 新工具 | `ToolImplementations.cs` + `ToolRegistryService.cs` |
| 新策略 | `IWorkflowStrategy` 實作 + `WorkflowStrategyResolver.cs` |
| 新 Middleware | `DelegatingChatClient` 子類 + `AgentContextBuilder.cs` |
| 新 Flow 節點 | `FlowNodeRunner.cs` + `FlowPlanValidator.cs` + `WorkflowCrystallizer.cs` |
| 替換 Autonomous 策略 | 實作介面 + `services.Replace(...)` |
| 替換引擎 | 實作 `IScriptEngine` / `IOcrEngine` + DI 替換 |
| 擴展沙箱 | 實作 `ISandboxApi` + DI 註冊 |
| 新清洗規則 | 實作 `ICleaningRule` + `services.AddCleaningRule<T>()` |
| 新 Partitioner | 實作 `IPartitioner` + `services.AddPartitioner<T>()` |
| 新 Schema 模板 | 在 `Data/schema-templates/` 放 JSON 檔案 |

---

## 9. CraftCleaner 擴充（AgentCraftLab.Cleaner）

### 9.1 新增清洗規則

實作 `ICleaningRule` 介面：

```csharp
public sealed class MyCustomRule : ICleaningRule
{
    public string Name => "my_custom_rule";
    public int Order => 500;  // 執行順序（數字越小越先）

    public bool ShouldApply(DocumentElement element) =>
        element.Type == ElementType.NarrativeText;

    public void Apply(DocumentElement element)
    {
        element.Text = element.Text.Replace("舊", "新");
    }
}
```

DI 註冊：

```csharp
services.AddCraftCleaner();
services.AddCleaningRule<MyCustomRule>();
```

### 9.2 新增 Partitioner（支援新格式）

實作 `IPartitioner` 介面：

```csharp
public sealed class RtfPartitioner : IPartitioner
{
    public bool CanPartition(string mimeType) =>
        mimeType == "application/rtf";

    public Task<IReadOnlyList<DocumentElement>> PartitionAsync(
        byte[] data, string fileName,
        PartitionOptions? options = null, CancellationToken ct = default)
    {
        // 解析 RTF → DocumentElement[]
    }
}
```

DI 註冊：

```csharp
services.AddPartitioner<RtfPartitioner>();
```

### 9.3 新增 Schema 模板

在 `Data/schema-templates/` 目錄放入 JSON 檔案，零程式碼修改：

```json
{
  "id": "meeting-summary",
  "name": "會議記錄摘要",
  "description": "從會議記錄整理出結構化摘要",
  "category": "Business",
  "extraction_guidance": "...",
  "json_schema": {
    "$schema": "https://json-schema.org/draft/2020-12/schema",
    "type": "object",
    "properties": {
      "meeting_info": { ... },
      "decisions": { ... },
      "action_items": { ... }
    }
  }
}
```

### 9.4 替換 OCR Provider

實作 `IOcrProvider` 介面或用 `AddCraftCleanerOcr()` 橋接：

```csharp
services.AddCraftCleanerOcr(async (imageData, langs, ct) =>
{
    var result = await myOcrEngine.RecognizeAsync(imageData, langs, ct);
    return (result.Text, result.Confidence);
});
```
