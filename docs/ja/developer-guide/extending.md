# AgentCraftLab 拡張ガイド

本ドキュメントでは、AgentCraftLab にさまざまな拡張機能を追加する方法を説明します。各拡張ポイントには手順とコード例を付記しています。

---

## 1. 新しいノードタイプの追加

`timer` ノードの追加を例に、4 箇所の変更が必要です。

### 手順 1：NodeTypes に定数を追加

ファイル：`AgentCraftLab.Engine/Models/Constants.cs`

```csharp
public static class NodeTypes
{
    // ... 既存の定数 ...
    public const string Timer = "timer";  // <-- 追加
}
```

### 手順 2：NodeTypeRegistry に 1 行追加

同一ファイルの `NodeTypeRegistry.Registry` ディクショナリにメタデータを追加します：

```csharp
private static readonly Dictionary<string, NodeTypeInfo> Registry = new(StringComparer.OrdinalIgnoreCase)
{
    // ... 既存の項目 ...
    [NodeTypes.Timer] = new(NodeTypes.Timer, IsExecutable: true, RequiresImperative: true),
};
```

各フラグの説明：
- `IsExecutable`：ノードが実行される（meta/data ノードではない）
- `RequiresImperative`：Imperative ストラテジーが必要（フロー制御ロジックを持つノードはすべて必要）
- `IsAgentLike`：Agent に類似した動作（LLM 呼び出しあり）
- `IsMeta`：meta ノード（start/end）
- `IsDataNode`：データノード（rag）

### 手順 3：NodeExecutorRegistry に handler を追加

`AgentCraftLab.Engine/Strategies/NodeExecutors/TimerNodeExecutor.cs` を作成します：

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

DI コンテナに登録します（通常は `AddAgentCraftEngine()` 拡張メソッド内）：

```csharp
services.AddSingleton<INodeExecutor, TimerNodeExecutor>();
```

`NodeExecutorRegistry` は `IEnumerable<INodeExecutor>` を通じてすべての実装を自動的に収集します。

### 手順 4：JS NODE_REGISTRY にフロントエンドレンダリングを追加

ファイル：`AgentCraftLab.Web/src/components/studio/nodes/registry.ts`

```typescript
import { Timer } from 'lucide-react'

export const NODE_REGISTRY: Record<NodeType, NodeTypeConfig> = {
  // ... 既存の項目 ...
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

同時に `AgentCraftLab.Web/src/types/workflow.ts` の `NodeType` ユニオン型に `'timer'` を追加し、対応する React ノードコンポーネントを作成する必要があります。

---

## 2. 新しいビルトインツールの追加

ビルトインツールは `ToolRegistryService` で管理され、実装ロジックは `ToolImplementations` に配置されます。

### 手順 1：ToolImplementations にメソッドを追加

ファイル：`AgentCraftLab.Engine/Services/ToolImplementations.cs`

```csharp
public static string Base64Encode(
    [Description("エンコードするテキスト")] string text)
{
    return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text));
}
```

### 手順 2：ToolRegistryService に Register() を追加

対応する `RegisterXxxTools()` メソッドに追加します：

```csharp
private void RegisterUtilityTools()
{
    // ... 既存のツール ...

    Register("base64_encode", "Base64 Encode", "テキストを Base64 文字列にエンコードする",
        () => AIFunctionFactory.Create(
            ToolImplementations.Base64Encode,
            name: "Base64Encode",
            description: "テキストを Base64 文字列にエンコードする"),
        ToolCategory.Utility, "&#x1F511;");
}
```

`Register()` パラメーターの説明：
- `id`：一意の識別子（snake_case）
- `displayName`：UI 表示名
- `description`：ツールの説明
- `factory`：`Func<AITool>` ファクトリ
- `category`：カテゴリ（Search / Utility / Web / Data）
- `icon`：HTML エンティティアイコン
- `requiredCredential`：必要な認証情報 provider（オプション）
- `credentialFactory`：認証情報がある場合の代替ファクトリ（オプション）

ツールに認証情報が必要な場合は、`credentialFactory` を提供します：

```csharp
Register("my_api", "My API", "外部 API を呼び出す",
    () => AIFunctionFactory.Create(/* 認証情報なしバージョン */),
    ToolCategory.Web, "&#x1F310;", "my-provider",
    credentialFactory: creds =>
    {
        var key = creds["my-provider"].ApiKey;
        return AIFunctionFactory.Create(/* 認証情報ありバージョン */);
    });
```

AI Build は ToolRegistryService からツールリストを自動的に読み取るため、追加の設定は不要です。

---

## 3. 新しい実行ストラテジーの追加

### 手順 1：IWorkflowStrategy を実装

ファイル：`AgentCraftLab.Engine/Strategies/IWorkflowStrategy.cs` でインターフェースが定義されています：

```csharp
public interface IWorkflowStrategy
{
    IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        WorkflowStrategyContext context,
        CancellationToken cancellationToken);
}
```

新しいストラテジーを作成します：

```csharp
public class PriorityWorkflowStrategy : IWorkflowStrategy
{
    public async IAsyncEnumerable<ExecutionEvent> ExecuteAsync(
        WorkflowStrategyContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // 優先度順に agent ノードをソート
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

### 手順 2：WorkflowStrategyResolver.Resolve() に case を追加

ファイル：`AgentCraftLab.Engine/Services/WorkflowStrategyResolver.cs`

```csharp
return (workflowType switch
{
    WorkflowTypes.Sequential => new SequentialWorkflowStrategy(),
    WorkflowTypes.Concurrent => new ConcurrentWorkflowStrategy(),
    WorkflowTypes.Handoff => new HandoffWorkflowStrategy(),
    WorkflowTypes.Imperative => CreateImperative(),
    "priority" => new PriorityWorkflowStrategy(),  // <-- 追加
    _ => throw new NotSupportedException(...)
}, $"detected:{workflowType}");
```

`WorkflowTypes` 定数クラスにも対応する定数を追加する必要があります。

---

## 4. 新しいミドルウェアの追加

ミドルウェアはデコレーターパターンで `IChatClient` をラップし、Agent の LLM 呼び出しの前後にロジックを注入します。

### 手順 1：DelegatingChatClient を継承

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

### 手順 2：ApplyMiddleware() に case を追加

ファイル：`AgentCraftLab.Engine/Strategies/AgentContextBuilder.cs`

```csharp
public static IChatClient ApplyMiddleware(IChatClient client, string? middleware,
    Dictionary<string, Dictionary<string, string>>? config = null)
{
    // ... 既存のミドルウェア ...

    if (set.Contains("caching"))
        client = new CachingChatClient(client);  // <-- 追加

    return client;
}
```

ミドルウェア名は UI 上の Agent ノードの `middleware` フィールドでカンマ区切りで指定される値です。

### 既存の検出エンジンの置換（上級者向け）

GuardRails と PII はいずれもインターフェースで分離されており、ミドルウェアを変更せずに検出ロジックを置換できます：

**GuardRails ルールエンジンの置換：**

```csharp
// IGuardRailsPolicy インターフェースを実装
public class AzureContentSafetyPolicy : IGuardRailsPolicy
{
    public IReadOnlyList<GuardRailsMatch> Evaluate(string text, GuardRailsDirection direction)
    {
        // Azure Content Safety API を呼び出し
    }
}

// DI 置換
services.AddSingleton<IGuardRailsPolicy, AzureContentSafetyPolicy>();
```

**PII 検出器の置換：**

```csharp
// IPiiDetector インターフェースを実装
public class PresidioPiiDetector : IPiiDetector
{
    public IReadOnlyList<PiiEntity> Detect(string text, double confidenceThreshold = 0.5)
    {
        // Presidio REST API を呼び出し
    }
}

// DI 置換
services.AddSingleton<IPiiDetector, PresidioPiiDetector>();
```

**PII トークン保管庫の置換（例：Redis）：**

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

## 5. 新しい Flow ノードの追加

Flow ノードは Autonomous Flow の構造化実行（LLM がノードシーケンスをプランニング）で使用されます。

### 手順 1：FlowNodeRunner に case を追加

ファイル：`AgentCraftLab.Autonomous.Flow/Services/FlowNodeRunner.cs`

```csharp
public async IAsyncEnumerable<ExecutionEvent> ExecuteNodeAsync(
    PlannedNode node, string input, GoalExecutionRequest request,
    CancellationToken cancellationToken)
{
    switch (node.NodeType)
    {
        // ... 既存の case ...

        case NodeTypes.Timer:
            yield return ExecuteTimerNode(node, input);
            break;
    }
}

private static ExecutionEvent ExecuteTimerNode(PlannedNode node, string input)
{
    var delayMs = node.Config.MaxIterations ?? 1000;
    Thread.Sleep(delayMs);  // Flow ノードでの簡易実装
    return new ExecutionEvent(EventTypes.NodeCompleted, node.Name, input);
}
```

### 手順 2：FlowPlanValidator.SupportedNodeTypes

ファイル：`AgentCraftLab.Autonomous.Flow/Services/FlowPlanValidator.cs`

```csharp
private static readonly HashSet<string> SupportedNodeTypes =
[
    NodeTypes.Agent, NodeTypes.Code, NodeTypes.Condition,
    NodeTypes.Iteration, NodeTypes.Parallel, NodeTypes.Loop,
    NodeTypes.HttpRequest,
    NodeTypes.Timer,  // <-- 追加
];
```

### 手順 3：FlowPlannerPrompt の更新

Planner の system prompt に新しいノードの用途と制約を記述し、LLM が正しくプランニングに使用できるようにします。

### 手順 4：WorkflowCrystallizer.StepToNode

ファイル：`AgentCraftLab.Autonomous.Flow/Services/WorkflowCrystallizer.cs`

`FromConfig()` メソッドに新しいノードのマッピングロジックを追加し、Flow 実行軌跡が正しく Workflow JSON に固定化（Crystallize）されることを保証します。

---

## 6. ストラテジーオブジェクトの置換（Autonomous）

Autonomous Agent の ReAct ループは 5 つのストラテジーインターフェースで責務を分離しており、個別に置換可能です。

### 置換可能なインターフェース

| インターフェース | 責務 | デフォルト実装 |
|------|------|----------|
| `IBudgetPolicy` | Token/ToolCall の予算チェック | `DefaultBudgetPolicy` |
| `IHistoryManager` | 会話履歴の管理と圧縮 | `HybridHistoryManager` |
| `IReflectionEngine` | 自己反省と監査 | `AuditorReflectionEngine` |
| `IToolDelegationStrategy` | ツールホワイトリストとセキュリティフィルタリング | `SafeWhitelistToolDelegation` |
| `IHumanInteractionHandler` | ヒューマンインタラクション処理 | `AgUiHumanInteractionHandler` |

### 置換例

```csharp
// 1. インターフェースを実装
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
        // カスタム budget reminder ロジック
    }

    public void InjectMidExecutionCheck(
        List<ChatMessage> messages, int iteration, int maxIterations)
    {
        // カスタム実行中チェックロジック
    }
}

// 2. DI Replace で登録
services.Replace(ServiceDescriptor.Singleton<IBudgetPolicy, StrictBudgetPolicy>());
```

`Add` ではなく `Replace` を使用し、デフォルト実装を確実にオーバーライドしてください。デフォルト実装は `AddAutonomousAgentCore()` で登録されています。

---

## 7. スクリプトエンジン / OCR エンジンの置換

### スクリプトエンジンの置換

インターフェースは `AgentCraftLab.Script/IScriptEngine.cs` で定義されています：

```csharp
public interface IScriptEngine
{
    Task<ScriptResult> ExecuteAsync(string code, string input,
        ScriptOptions? options = null, CancellationToken cancellationToken = default);
}
```

**組み込みエンジン：**

| エンジン | 言語 | 説明 |
|---------|------|------|
| `JintScriptEngine` | JavaScript | Jint JS サンドボックス、自然な分離 + 4 段階のリソース制限 |
| `RoslynScriptEngine` | C# | 低レベル CSharpCompilation + collectible ALC、AST セキュリティスキャン + References ホワイトリスト |

**マルチ言語ファクトリ：** `IScriptEngineFactory` が言語に応じて適切なエンジンにディスパッチします：

```csharp
// 言語サポートの追加
var factory = new ScriptEngineFactory()
    .Register("javascript", new JintScriptEngine())
    .Register("csharp", new RoslynScriptEngine())
    .Register("python", new PythonScriptEngine()); // カスタムエンジン
```

**DI 登録（推奨：マルチ言語モード）：**

```csharp
// Jint + Roslyn を同時登録、IScriptEngine との後方互換性を維持
builder.Services.AddMultiLanguageScript();
```

**単一エンジンの置換：**

```csharp
services.Replace(ServiceDescriptor.Singleton<IScriptEngine, PythonScriptEngine>());
```

**Roslyn C# セキュリティ：** `RoslynCodeSanitizer` がコンパイル前に AST をスキャンし、危険な API（File/Process/HttpClient/Assembly/Environment 等）をブロックします。`BuildSafeReferences()` は安全なアセンブリのみを含みます（System.IO.FileSystem、System.Net.Http は除外）。各実行では collectible `AssemblyLoadContext` を使用し、実行後に Unload してメモリリークを防止します。

### OCR エンジンの置換

インターフェースは `AgentCraftLab.Ocr/IOcrEngine.cs` で定義されています：

```csharp
public interface IOcrEngine
{
    Task<OcrResult> RecognizeAsync(byte[] imageData,
        IReadOnlyList<string>? languages = null,
        CancellationToken cancellationToken = default);
}
```

代替エンジンの実装：

```csharp
public class AzureVisionOcrEngine : IOcrEngine
{
    public async Task<OcrResult> RecognizeAsync(
        byte[] imageData, IReadOnlyList<string>? languages = null,
        CancellationToken cancellationToken = default)
    {
        // Azure Computer Vision API を呼び出し
        // OcrResult { Text, Confidence } を返却
    }
}
```

---

## 8. サンドボックス API の拡張

サンドボックス API により、Code ノードの JS スクリプトから制御された外部機能を呼び出すことができます。

### ISandboxApi の実装

インターフェースは `AgentCraftLab.Script/ISandboxApi.cs` で定義されています：

```csharp
public interface ISandboxApi
{
    string Name { get; }
    IReadOnlyDictionary<string, Delegate> GetMethods();
}
```

実装例 -- `crypto` サンドボックス API の追加：

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

DI 登録：

```csharp
services.AddSingleton<ISandboxApi, CryptoSandboxApi>();
```

スクリプトエンジンは DI を通じてすべての `ISandboxApi` 実装を自動的に収集し、スクリプトのグローバルスコープに注入します。JS スクリプト内では以下のように使用できます：

```javascript
var hash = crypto.sha256(input);
result = hash;
```

`Name` プロパティはスクリプト内のグローバルオブジェクト名を決定し、`GetMethods()` が返すキーはそのオブジェクト上のメソッド名になります。

---

## クイックリファレンス表

| 拡張タイプ | 変更ファイル |
|----------|----------|
| 新しいノード | `Constants.cs` + `NodeExecutor` + `registry.ts` |
| 新しいツール | `ToolImplementations.cs` + `ToolRegistryService.cs` |
| 新しいストラテジー | `IWorkflowStrategy` 実装 + `WorkflowStrategyResolver.cs` |
| 新しいミドルウェア | `DelegatingChatClient` サブクラス + `AgentContextBuilder.cs` |
| 新しい Flow ノード | `FlowNodeRunner.cs` + `FlowPlanValidator.cs` + `WorkflowCrystallizer.cs` |
| Autonomous ストラテジーの置換 | インターフェース実装 + `services.Replace(...)` |
| エンジンの置換 | `IScriptEngine` / `IOcrEngine` 実装 + DI 置換 |
| サンドボックスの拡張 | `ISandboxApi` 実装 + DI 登録 |
| 新しいクリーニングルール | `ICleaningRule` 実装 + `services.AddCleaningRule<T>()` |
| 新しい Partitioner | `IPartitioner` 実装 + `services.AddPartitioner<T>()` |
| 新しいスキーマテンプレート | `Data/schema-templates/` に JSON ファイルを配置 |
| 新しい DB Provider | `extensions/data/` に新規プロジェクト + 15 個の Store インターフェース実装 + `Program.cs` に switch case 追加 |

---

## 9. 新しいデータベース Provider の追加

AgentCraftLab は**データ層分離アーキテクチャ**を採用しています。15 個の Store インターフェースは純粋な抽象プロジェクト（`AgentCraftLab.Data`、依存関係ゼロ）に定義され、各データベース Provider は `extensions/data/` 配下の独立プロジェクトとして実装されます。

### プロジェクト構成

```
extensions/data/
├── AgentCraftLab.Data/              # 純粋な抽象（15 インターフェース、DTO）
├── AgentCraftLab.Data.Sqlite/       # SQLite Provider（EF Core）
└── AgentCraftLab.Data.MongoDB/      # MongoDB Provider
```

> **重要な設計方針：** `AgentCraftLab.Engine` は **EF Core に依存しません**。Engine は `AgentCraftLab.Data`（インターフェースのみ）にのみ依存します。実際のデータベース実装はホストレベルで `AddSqliteDataProvider()` または `AddMongoDbProvider()` を通じて合成されます。

### 15 個の Store インターフェース（AgentCraftLab.Data 名前空間）

| インターフェース | データ内容 |
|------|------|
| `IWorkflowStore` | Workflow 定義 |
| `ICredentialStore` | 暗号化された API キー |
| `ISkillStore` | カスタム Agent スキル |
| `ITemplateStore` | Workflow テンプレート |
| `IRequestLogStore` | 実行ログ |
| `IScheduleStore` | スケジュールタスク |
| `IDataSourceStore` | データソースメタデータ |
| `IKnowledgeBaseStore` | ナレッジベースメタデータ |
| `IExecutionMemoryStore` | Autonomous 実行メモリ |
| `ICraftMdStore` | Markdown ドキュメントストア |
| `ICheckpointStore` | ReAct/Flow チェックポイントスナップショット |
| `IEntityMemoryStore` | エンティティファクトメモリ |
| `IContextualMemoryStore` | ユーザーパターンメモリ |
| `IApiKeyStore` | 公開済み API キー |
| `IRefineryStore` | DocRefinery プロジェクトと出力 |

### 手順 1：新しい Provider プロジェクトを作成

`extensions/data/` 配下に新しいプロジェクトを作成します。例として `AgentCraftLab.Data.PostgreSQL`：

```
extensions/data/AgentCraftLab.Data.PostgreSQL/
├── AgentCraftLab.Data.PostgreSQL.csproj
├── ServiceCollectionExtensions.cs
├── PostgreSqlWorkflowStore.cs
├── PostgreSqlCredentialStore.cs
└── ... （インターフェースごとに 1 クラス）
```

`.csproj` で `AgentCraftLab.Data` を参照し、DB ドライバーを追加します：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\AgentCraftLab.Data\AgentCraftLab.Data.csproj" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.*" />
  </ItemGroup>
</Project>
```

### 手順 2：15 個の Store インターフェースを実装

各 Store インターフェースの実装を作成します。`AgentCraftLab.Data.Sqlite` の実装パターンを参考にしてください：

```csharp
namespace AgentCraftLab.Data.PostgreSQL;

public sealed class PostgreSqlWorkflowStore : IWorkflowStore
{
    public async Task<WorkflowDocument> SaveAsync(
        string userId, string name, string description,
        string type, string workflowJson)
    {
        // PostgreSQL 固有の実装
    }

    // ... IWorkflowStore の他のメソッド
}
```

### 手順 3：ServiceCollectionExtensions を追加

DI 登録用の拡張メソッドを作成します：

```csharp
namespace AgentCraftLab.Data.PostgreSQL;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPostgreSqlDataProvider(
        this IServiceCollection services, string connectionString)
    {
        // DbContext の登録
        services.AddDbContext<PostgreSqlDbContext>(options =>
            options.UseNpgsql(connectionString));

        // 15 個の Store を登録
        services.AddSingleton<IWorkflowStore, PostgreSqlWorkflowStore>();
        services.AddSingleton<ICredentialStore, PostgreSqlCredentialStore>();
        // ... 残り 13 個の Store ...

        return services;
    }
}
```

### 手順 4：Program.cs に switch case を追加

ファイル：`AgentCraftLab.Api/Program.cs`

```csharp
var dbProvider = builder.Configuration["Database:Provider"] ?? "sqlite";

switch (dbProvider)
{
    case "sqlite":
        builder.Services.AddSqliteDataProvider("Data/agentcraftlab.db");
        break;
    case "mongodb":
        var connStr = builder.Configuration["Database:ConnectionString"]!;
        var dbName = builder.Configuration["Database:DatabaseName"] ?? "agentcraftlab";
        builder.Services.AddMongoDbProvider(connStr, dbName);
        break;
    case "postgresql":  // <-- 追加
        var pgConnStr = builder.Configuration["Database:ConnectionString"]!;
        builder.Services.AddPostgreSqlDataProvider(pgConnStr);
        break;
}
```

> **注意：** `AddAgentCraftEngine()` と `AddXxxDataProvider()` は別々に呼び出されます。Engine はデータベースの選択を一切知りません — これがデータ層分離の核心です。

---

## 10. CraftCleaner 拡張（AgentCraftLab.Cleaner）

### 10.1 クリーニングルールの追加

`ICleaningRule` インターフェースを実装：

```csharp
public sealed class MyCustomRule : ICleaningRule
{
    public string Name => "my_custom_rule";
    public int Order => 500;  // 実行順序（小さいほど先に実行）

    public bool ShouldApply(DocumentElement element) =>
        element.Type == ElementType.NarrativeText;

    public void Apply(DocumentElement element)
    {
        element.Text = element.Text.Replace("旧", "新");
    }
}
```

DI 登録：

```csharp
services.AddCraftCleaner();
services.AddCleaningRule<MyCustomRule>();
```

### 10.2 Partitioner の追加（新フォーマット対応）

`IPartitioner` インターフェースを実装：

```csharp
public sealed class RtfPartitioner : IPartitioner
{
    public bool CanPartition(string mimeType) =>
        mimeType == "application/rtf";

    public Task<IReadOnlyList<DocumentElement>> PartitionAsync(
        byte[] data, string fileName,
        PartitionOptions? options = null, CancellationToken ct = default)
    {
        // RTF を解析 → DocumentElement[]
    }
}
```

DI 登録：

```csharp
services.AddPartitioner<RtfPartitioner>();
```

### 10.3 スキーマテンプレートの追加

`Data/schema-templates/` ディレクトリに JSON ファイルを配置するだけ — コード変更不要：

```json
{
  "id": "meeting-summary",
  "name": "会議サマリー",
  "description": "会議記録から構造化サマリーを抽出",
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

### 10.4 OCR プロバイダーの置換

`IOcrProvider` インターフェースを実装するか、`AddCraftCleanerOcr()` でブリッジ：

```csharp
services.AddCraftCleanerOcr(async (imageData, langs, ct) =>
{
    var result = await myOcrEngine.RecognizeAsync(imageData, langs, ct);
    return (result.Text, result.Confidence);
});
```
