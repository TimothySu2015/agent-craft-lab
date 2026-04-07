# AgentCraftLab Extension Guide

This document explains how to add various extensions to AgentCraftLab. Each extension point includes step-by-step instructions and code examples.

---

## 1. Adding a New Node Type

Using a `timer` node as an example, four locations need to be modified.

### Step 1: Add Constant to NodeTypes

File: `AgentCraftLab.Engine/Models/Constants.cs`

```csharp
public static class NodeTypes
{
    // ... existing constants ...
    public const string Timer = "timer";  // <-- new
}
```

### Step 2: Add Entry to NodeTypeRegistry

Add metadata to the `NodeTypeRegistry.Registry` dictionary in the same file:

```csharp
private static readonly Dictionary<string, NodeTypeInfo> Registry = new(StringComparer.OrdinalIgnoreCase)
{
    // ... existing entries ...
    [NodeTypes.Timer] = new(NodeTypes.Timer, IsExecutable: true, RequiresImperative: true),
};
```

Flag descriptions:
- `IsExecutable`: The node will be executed (not a meta/data node)
- `RequiresImperative`: Requires the Imperative strategy (needed for nodes with control flow logic)
- `IsAgentLike`: Behaves like an Agent (has LLM calls)
- `IsMeta`: Meta node (start/end)
- `IsDataNode`: Data node (rag)

### Step 3: Add Handler to NodeExecutorRegistry

Create `AgentCraftLab.Engine/Strategies/NodeExecutors/TimerNodeExecutor.cs`:

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

Register in the DI container (typically in the `AddAgentCraftEngine()` extension method):

```csharp
services.AddSingleton<INodeExecutor, TimerNodeExecutor>();
```

`NodeExecutorRegistry` automatically collects all implementations via `IEnumerable<INodeExecutor>`.

### Step 4: Add Frontend Rendering to JS NODE_REGISTRY

File: `AgentCraftLab.Web/src/components/studio/nodes/registry.ts`

```typescript
import { Timer } from 'lucide-react'

export const NODE_REGISTRY: Record<NodeType, NodeTypeConfig> = {
  // ... existing entries ...
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

You also need to add `'timer'` to the `NodeType` union type in `AgentCraftLab.Web/src/types/workflow.ts` and create the corresponding React node component.

---

## 2. Adding a New Built-in Tool

Built-in tools are managed by `ToolRegistryService`, with implementation logic in `ToolImplementations`.

### Step 1: Add Method to ToolImplementations

File: `AgentCraftLab.Engine/Services/ToolImplementations.cs`

```csharp
public static string Base64Encode(
    [Description("The text to encode")] string text)
{
    return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text));
}
```

### Step 2: Add Register() to ToolRegistryService

Add to the corresponding `RegisterXxxTools()` method:

```csharp
private void RegisterUtilityTools()
{
    // ... existing tools ...

    Register("base64_encode", "Base64 Encode", "Encode text as a Base64 string",
        () => AIFunctionFactory.Create(
            ToolImplementations.Base64Encode,
            name: "Base64Encode",
            description: "Encode text as a Base64 string"),
        ToolCategory.Utility, "&#x1F511;");
}
```

`Register()` parameter descriptions:
- `id`: Unique identifier (snake_case)
- `displayName`: UI display name
- `description`: Tool description
- `factory`: `Func<AITool>` factory
- `category`: Category (Search / Utility / Web / Data)
- `icon`: HTML entity icon
- `requiredCredential`: Required credential provider (optional)
- `credentialFactory`: Alternative factory when credentials are available (optional)

If the tool requires credentials, provide a `credentialFactory`:

```csharp
Register("my_api", "My API", "Call an external API",
    () => AIFunctionFactory.Create(/* version without credentials */),
    ToolCategory.Web, "&#x1F310;", "my-provider",
    credentialFactory: creds =>
    {
        var key = creds["my-provider"].ApiKey;
        return AIFunctionFactory.Create(/* version with credentials */);
    });
```

AI Build automatically reads the tool list from ToolRegistryService, requiring no additional configuration.

---

## 3. Adding a New Execution Strategy

### Step 1: Implement IWorkflowStrategy

File: `AgentCraftLab.Engine/Strategies/IWorkflowStrategy.cs` defines the interface:

```csharp
public interface IWorkflowStrategy
{
    IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        WorkflowStrategyContext context,
        CancellationToken cancellationToken);
}
```

Create a new strategy:

```csharp
public class PriorityWorkflowStrategy : IWorkflowStrategy
{
    public async IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        WorkflowStrategyContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Sort agent nodes by priority
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

### Step 2: Add Case to WorkflowStrategyResolver.Resolve()

File: `AgentCraftLab.Engine/Services/WorkflowStrategyResolver.cs`

```csharp
return (workflowType switch
{
    WorkflowTypes.Sequential => new SequentialWorkflowStrategy(),
    WorkflowTypes.Concurrent => new ConcurrentWorkflowStrategy(),
    WorkflowTypes.Handoff => new HandoffWorkflowStrategy(),
    WorkflowTypes.Imperative => CreateImperative(),
    "priority" => new PriorityWorkflowStrategy(),  // <-- new
    _ => throw new NotSupportedException(...)
}, $"detected:{workflowType}");
```

You also need to add the corresponding constant to the `WorkflowTypes` constant class.

---

## 4. Adding New Middleware

Middleware wraps `IChatClient` using the Decorator pattern, injecting logic before and after the Agent's LLM calls.

### Step 1: Inherit from DelegatingChatClient

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

### Step 2: Add Case to ApplyMiddleware()

File: `AgentCraftLab.Engine/Strategies/AgentContextBuilder.cs`

```csharp
public static IChatClient ApplyMiddleware(IChatClient client, string? middleware,
    Dictionary<string, Dictionary<string, string>>? config = null)
{
    // ... existing middleware ...

    if (set.Contains("caching"))
        client = new CachingChatClient(client);  // <-- new

    return client;
}
```

The middleware name is the comma-separated value in the Agent node's `middleware` field in the UI.

### Replacing Existing Detection Engines (Advanced)

GuardRails and PII are both decoupled through interfaces, allowing replacement of detection logic without modifying the Middleware:

**Replacing the GuardRails Rule Engine:**

```csharp
// Implement the IGuardRailsPolicy interface
public class AzureContentSafetyPolicy : IGuardRailsPolicy
{
    public IReadOnlyList<GuardRailsMatch> Evaluate(string text, GuardRailsDirection direction)
    {
        // Call Azure Content Safety API
    }
}

// DI replacement
services.AddSingleton<IGuardRailsPolicy, AzureContentSafetyPolicy>();
```

**Replacing the PII Detector:**

```csharp
// Implement the IPiiDetector interface
public class PresidioPiiDetector : IPiiDetector
{
    public IReadOnlyList<PiiEntity> Detect(string text, double confidenceThreshold = 0.5)
    {
        // Call Presidio REST API
    }
}

// DI replacement
services.AddSingleton<IPiiDetector, PresidioPiiDetector>();
```

**Replacing the PII Token Vault (e.g., Redis):**

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

## 5. Adding a New Flow Node

Flow nodes are used for structured execution in Autonomous Flow (LLM plans a sequence of nodes).

### Step 1: Add Case to FlowNodeRunner

File: `AgentCraftLab.Autonomous.Flow/Services/FlowNodeRunner.cs`

```csharp
public async IAsyncEnumerable<ExecutionEvent> ExecuteNodeAsync(
    PlannedNode node, string input, GoalExecutionRequest request,
    CancellationToken cancellationToken)
{
    switch (node.NodeType)
    {
        // ... existing cases ...

        case NodeTypes.Timer:
            yield return ExecuteTimerNode(node, input);
            break;
    }
}

private static ExecutionEvent ExecuteTimerNode(PlannedNode node, string input)
{
    var delayMs = node.Config.MaxIterations ?? 1000;
    Thread.Sleep(delayMs);  // Simple implementation within Flow node
    return new ExecutionEvent(EventTypes.NodeCompleted, node.Name, input);
}
```

### Step 2: Update FlowPlanValidator.SupportedNodeTypes

File: `AgentCraftLab.Autonomous.Flow/Services/FlowPlanValidator.cs`

```csharp
private static readonly HashSet<string> SupportedNodeTypes =
[
    NodeTypes.Agent, NodeTypes.Code, NodeTypes.Condition,
    NodeTypes.Iteration, NodeTypes.Parallel, NodeTypes.Loop,
    NodeTypes.HttpRequest,
    NodeTypes.Timer,  // <-- new
];
```

### Step 3: Update FlowPlannerPrompt

Add the new node's purpose and constraints to the Planner's system prompt, enabling the LLM to correctly plan its usage.

### Step 4: Update WorkflowCrystallizer.StepToNode

File: `AgentCraftLab.Autonomous.Flow/Services/WorkflowCrystallizer.cs`

Add mapping logic for the new node in the `FromConfig()` method, ensuring that Flow execution traces can be correctly crystallized into Workflow JSON.

---

## 6. Replacing Strategy Objects (Autonomous)

The Autonomous Agent's ReAct loop separates responsibilities through five strategy interfaces, each independently replaceable.

### Replaceable Interfaces

| Interface | Responsibility | Default Implementation |
|------|------|----------|
| `IBudgetPolicy` | Token/ToolCall budget checking | `DefaultBudgetPolicy` |
| `IHistoryManager` | Conversation history management and compression | `HybridHistoryManager` |
| `IReflectionEngine` | Self-reflection and auditing | `AuditorReflectionEngine` |
| `IToolDelegationStrategy` | Tool whitelist and safety filtering | `SafeWhitelistToolDelegation` |
| `IHumanInteractionHandler` | Human interaction handling | `AgUiHumanInteractionHandler` |

### Replacement Example

```csharp
// 1. Implement the interface
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
        // Custom budget reminder logic
    }

    public void InjectMidExecutionCheck(
        List<ChatMessage> messages, int iteration, int maxIterations)
    {
        // Custom mid-execution check logic
    }
}

// 2. DI Replace registration
services.Replace(ServiceDescriptor.Singleton<IBudgetPolicy, StrictBudgetPolicy>());
```

Note the use of `Replace` instead of `Add` to ensure the default implementation is overridden. Default implementations are registered in `AddAutonomousAgentCore()`.

---

## 7. Replacing the Script Engine / OCR Engine

### Replacing the Script Engine

Interface defined in `AgentCraftLab.Script/IScriptEngine.cs`:

```csharp
public interface IScriptEngine
{
    Task<ScriptResult> ExecuteAsync(string code, string input,
        ScriptOptions? options = null, CancellationToken cancellationToken = default);
}
```

**Built-in engines:**

| Engine | Language | Description |
|--------|----------|-------------|
| `JintScriptEngine` | JavaScript | Jint JS sandbox with natural isolation + 4-level resource limits |
| `RoslynScriptEngine` | C# | Low-level CSharpCompilation + collectible ALC, AST security scanning + References whitelist |

**Multi-language factory:** `IScriptEngineFactory` dispatches to the appropriate engine by language:

```csharp
// Add language support
var factory = new ScriptEngineFactory()
    .Register("javascript", new JintScriptEngine())
    .Register("csharp", new RoslynScriptEngine())
    .Register("python", new PythonScriptEngine()); // Custom engine
```

**DI registration (recommended multi-language mode):**

```csharp
// Registers both Jint + Roslyn, backward-compatible with IScriptEngine
builder.Services.AddMultiLanguageScript();
```

**Replace a single engine:**

```csharp
services.Replace(ServiceDescriptor.Singleton<IScriptEngine, PythonScriptEngine>());
```

**Roslyn C# security:** `RoslynCodeSanitizer` scans the AST before compilation, blocking dangerous APIs (File/Process/HttpClient/Assembly/Environment, etc.). `BuildSafeReferences()` only includes safe assemblies (excludes System.IO.FileSystem, System.Net.Http). Each execution uses a collectible `AssemblyLoadContext`, unloaded after execution to prevent memory leaks.

### Replacing the OCR Engine

Interface defined in `AgentCraftLab.Ocr/IOcrEngine.cs`:

```csharp
public interface IOcrEngine
{
    Task<OcrResult> RecognizeAsync(byte[] imageData,
        IReadOnlyList<string>? languages = null,
        CancellationToken cancellationToken = default);
}
```

Implement an alternative engine:

```csharp
public class AzureVisionOcrEngine : IOcrEngine
{
    public async Task<OcrResult> RecognizeAsync(
        byte[] imageData, IReadOnlyList<string>? languages = null,
        CancellationToken cancellationToken = default)
    {
        // Call Azure Computer Vision API
        // Return OcrResult { Text, Confidence }
    }
}
```

---

## 8. Extending Sandbox APIs

Sandbox APIs allow JS scripts in Code nodes to call controlled external functionality.

### Implementing ISandboxApi

Interface defined in `AgentCraftLab.Script/ISandboxApi.cs`:

```csharp
public interface ISandboxApi
{
    string Name { get; }
    IReadOnlyDictionary<string, Delegate> GetMethods();
}
```

Implementation example -- adding a `crypto` sandbox API:

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

DI registration:

```csharp
services.AddSingleton<ISandboxApi, CryptoSandboxApi>();
```

The script engine automatically collects all `ISandboxApi` implementations via DI and injects them into the script's global scope. In JS scripts, you can then use:

```javascript
var hash = crypto.sha256(input);
result = hash;
```

The `Name` property determines the global object name in the script, and the keys returned by `GetMethods()` are the method names on that object.

---

## Quick Reference

| Extension Type | Files to Modify |
|----------|----------|
| New Node | `Constants.cs` + `NodeExecutor` + `registry.ts` |
| New Tool | `ToolImplementations.cs` + `ToolRegistryService.cs` |
| New Strategy | `IWorkflowStrategy` implementation + `WorkflowStrategyResolver.cs` |
| New Middleware | `DelegatingChatClient` subclass + `AgentContextBuilder.cs` |
| New Flow Node | `FlowNodeRunner.cs` + `FlowPlanValidator.cs` + `WorkflowCrystallizer.cs` |
| Replace Autonomous Strategy | Implement interface + `services.Replace(...)` |
| Replace Engine | Implement `IScriptEngine` / `IOcrEngine` + DI replacement |
| Extend Sandbox | Implement `ISandboxApi` + DI registration |
| New Cleaning Rule | Implement `ICleaningRule` + `services.AddCleaningRule<T>()` |
| New Partitioner | Implement `IPartitioner` + `services.AddPartitioner<T>()` |
| New Schema Template | Place JSON file in `Data/schema-templates/` |

---

## 9. CraftCleaner Extensions (AgentCraftLab.Cleaner)

### 9.1 Adding a Cleaning Rule

Implement the `ICleaningRule` interface:

```csharp
public sealed class MyCustomRule : ICleaningRule
{
    public string Name => "my_custom_rule";
    public int Order => 500;  // Execution order (lower = earlier)

    public bool ShouldApply(DocumentElement element) =>
        element.Type == ElementType.NarrativeText;

    public void Apply(DocumentElement element)
    {
        element.Text = element.Text.Replace("old", "new");
    }
}
```

DI registration:

```csharp
services.AddCraftCleaner();
services.AddCleaningRule<MyCustomRule>();
```

### 9.2 Adding a Partitioner (New Format Support)

Implement the `IPartitioner` interface:

```csharp
public sealed class RtfPartitioner : IPartitioner
{
    public bool CanPartition(string mimeType) =>
        mimeType == "application/rtf";

    public Task<IReadOnlyList<DocumentElement>> PartitionAsync(
        byte[] data, string fileName,
        PartitionOptions? options = null, CancellationToken ct = default)
    {
        // Parse RTF → DocumentElement[]
    }
}
```

DI registration:

```csharp
services.AddPartitioner<RtfPartitioner>();
```

### 9.3 Adding a Schema Template

Place a JSON file in the `Data/schema-templates/` directory — zero code changes:

```json
{
  "id": "meeting-summary",
  "name": "Meeting Summary",
  "description": "Extract structured summary from meeting notes",
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

### 9.4 Replacing the OCR Provider

Implement `IOcrProvider` or use `AddCraftCleanerOcr()` to bridge:

```csharp
services.AddCraftCleanerOcr(async (imageData, langs, ct) =>
{
    var result = await myOcrEngine.RecognizeAsync(imageData, langs, ct);
    return (result.Text, result.Confidence);
});
```
