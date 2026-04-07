# AgentCraftLab システムアーキテクチャガイド

本ドキュメントは、AgentCraftLab の理解や拡張を目指す開発者向けに、コアアーキテクチャ、実行フロー、拡張メカニズムを解説します。

---

## 1. Solution 概要 -- Open Core アーキテクチャ

AgentCraftLab は Open Core モデルを採用しており、コアエンジンはオープンソース、商用機能は独立してパッケージ化されています。

| プロジェクト | 位置づけ |
|------|------|
| `AgentCraftLab.Api` | 純粋なバックエンド API（AG-UI + REST、Minimal API エンドポイント、port 5200） |
| `AgentCraftLab.Web` | React フロントエンド（React Flow + CopilotKit + shadcn/ui、port 5173） |
| `AgentCraftLab.Search` | 独立した検索エンジンクラスライブラリ（FTS5 + ベクトル + RRF ハイブリッド検索） |
| `AgentCraftLab.Engine` | オープンソースコアエンジン（SQLite + シングルユーザーモード、ストラテジー + ノード + ツール + ミドルウェア + Hooks） |
| `AgentCraftLab.Autonomous` | ReAct ループ + Sub-agent 連携 + 12 meta-tools + セキュリティメカニズム |
| `AgentCraftLab.Autonomous.Flow` | Flow 構造化実行（LLM プランニング -> 7 種ノード -> Crystallize） |
| `AgentCraftLab.Autonomous.Playground` | CLI テストコンソール（Spectre.Console） |
| `AgentCraftLab.Script` | マルチ言語サンドボックスエンジン（Jint JS + Roslyn C#、IScriptEngine / IScriptEngineFactory インターフェース） |
| `AgentCraftLab.Ocr` | OCR エンジン（Tesseract、IOcrEngine インターフェース） |
| `AgentCraftLab.Commercial` | 商用レイヤー（MongoDB + OAuth、非公開） |
| `AgentCraftLab` | Blazor Web App（旧版 UI、Drawflow キャンバス） |

**技術スタック：** .NET 10 + LangVersion 13.0、`Microsoft.Agents.AI` シリーズ API を使用（Semantic Kernel は禁止）。

**機能の帰属判断：** 新機能はまず「シングルユーザーで必要か？」と問う -- 必要なら Engine へ、マルチユーザー/課金/SSO なら Commercial へ、検索/抽出/チャンク分割なら Search へ配置します。

---

## 2. Open Core モード切替

システムは `ConnectionStrings:MongoDB` の存在を検出し、起動時にモードを自動切替します：

```
                  ConnectionStrings:MongoDB が存在するか？
                          |
               +----------+----------+
               |                     |
              いいえ                はい
               |                     |
        オープンソースモード        商用モード
        （デフォルト）
        - SQLite               - MongoDB (Azure DocumentDB)
        - 認証なし             - Google/GitHub OAuth
        - userId="local"       - マルチユーザー
        - Sqlite*Store         - Mongo*Store
```

すべての Store インターフェース（IWorkflowStore、ICredentialStore など）には SQLite と MongoDB の 2 つの実装があり、DI コンテナが起動時に設定に応じて対応する実装を登録します。

---

## 3. Workflow 実行の三層アーキテクチャ

Workflow 実行はシステムのコアパスであり、3 層に分かれています：

```
WorkflowExecutionService.ExecuteAsync(request)        <-- 簡潔なオーケストレーター（約 180 行）
  |
  +-> ParseAndValidatePayload                         <-- JSON payload の検証
  +-> Hook(OnInput)                                   <-- 入力インターセプト
  +-> WorkflowPreprocessor.PrepareAsync                <-- 第二層：ノード分類 + RAG + AgentContext
  |     |
  |     +-> ノード分類（executable / data / meta）
  |     +-> RAG ノードの解析、ingest の実行
  |     +-> AgentContextBuilder で各 agent の context を構築
  |
  +-> WorkflowStrategyResolver.Resolve                 <-- 第三層：ストラテジー選択
  +-> IWorkflowStrategy.ExecuteAsync                   <-- ストラテジー実行
  +-> Hook(OnComplete / OnError)                       <-- 完了/エラーコールバック
  +-> yield IAsyncEnumerable<ExecutionEvent>            <-- ストリーミング出力
```

### 各層の責務

| 層 | クラス | 責務 |
|------|------|------|
| オーケストレーション層 | `WorkflowExecutionService` | パイプラインの組み立て、エラーハンドリング、Hooks の呼び出し |
| 前処理層 | `WorkflowPreprocessor` | ノード分類、RAG インデックス、AgentContext の構築 |
| ストラテジー層 | `IWorkflowStrategy` | 具体的な実行ロジック（トポロジーに基づく実行順序の決定） |

---

## 4. 5 種の実行ストラテジーと自動検出

### ストラテジー一覧

| ストラテジー | 説明 | 適用シナリオ |
|------|------|----------|
| Single | 単一 agent の直接実行 | agent ノードが 1 つのみ |
| Sequential | トポロジカルソート順に逐次実行 | リニアパイプライン |
| Concurrent | 複数 agent の並行実行 | 独立した agent、依存関係なし |
| Handoff | Agent 間の制御権の引き渡し | いずれかの agent に複数の outgoing edge がある |
| Imperative | 命令的な逐次実行（分岐/ループをサポート） | condition/loop/code 等の制御フローノードを含む |

### 自動検出ロジック

```
NodeTypeRegistry.HasAnyRequiringImperative() == true ?
  |-- はい --> Imperative ストラテジー
  |-- いいえ --> いずれかの agent に複数の outgoing edge がある？
                |-- はい --> Handoff ストラテジー
                |-- いいえ --> agent 数 == 1 ?
                              |-- はい --> Single ストラテジー
                              |-- いいえ --> Sequential ストラテジー
```

`WorkflowStrategyResolver.Resolve()` にこのロジックがカプセル化されています。新しいストラテジーの拡張方法：`IWorkflowStrategy` インターフェースを実装 + Resolve に case を追加します。

---

## 5. ノードシステム

### NodeTypeRegistry -- 唯一の信頼できる情報源

すべてのノードのメタデータは `NodeTypeRegistry` に集中管理されており、各ノードタイプは 3 つのフラグで定義されます：

| ノード | IsExecutable | RequiresImperative | IsAgentLike | 説明 |
|------|:---:|:---:|:---:|------|
| `agent` | Y | | Y | ローカル LLM Agent（ChatClientAgent + tools） |
| `a2a-agent` | Y | Y | Y | リモート A2A Agent（URL + format） |
| `autonomous` | Y | Y | Y | ReAct ループ（インターフェース分離） |
| `condition` | Y | Y | | 条件分岐 |
| `loop` | Y | Y | | ループ |
| `router` | Y | Y | | マルチルートルーティング |
| `human` | Y | Y | | ユーザー入力待ちで一時停止 |
| `code` | Y | Y | | 確定的変換（9 種モード + JS/C# デュアル言語サンドボックス） |
| `iteration` | Y | Y | | foreach ループ（SplitMode + MaxItems 50） |
| `parallel` | Y | Y | | fan-out/fan-in 並行処理 |
| `http-request` | Y | Y | | 確定的 HTTP 呼び出し |
| `start` / `end` | | | | Meta ノード（IsMeta） |
| `rag` | | | | データノード（IsDataNode） |

### NodeExecutorRegistry

各実行可能ノードタイプは 1 つの executor handler に対応します。実行エンジンは `NodeExecutorRegistry` を参照してディスパッチします。

### ノード追加の手順

1. `NodeTypes` クラスに定数文字列を追加
2. `NodeTypeRegistry` にメタデータ定義を 1 行追加
3. `NodeExecutorRegistry` に対応する handler を追加
4. （フロントエンド）JS `NODE_REGISTRY` にノードレンダリング定義を追加

---

## 6. Agent ツール解決 -- 4 層のツールソース

`AgentContextBuilder.ResolveToolsAsync()` がすべてのツールソースを統合します：

```
+------------------------------------------------------+
|              AgentContextBuilder                      |
|                                                       |
|  Layer 1: Tool Catalog（ビルトインツール）              |
|    - ToolRegistryService に登録された静的ツール         |
|    - web_search, file_read, calculator 等              |
|                                                       |
|  Layer 2: MCP Servers（動的外部ツール）                 |
|    - MCP プロトコルで外部 Tool Server に接続            |
|    - 利用可能なツールを動的に列挙                       |
|                                                       |
|  Layer 3: A2A Agents（Agent-to-Agent 相互呼び出し）    |
|    - リモート Agent をツールとして公開                   |
|    - Google / Microsoft の 2 種フォーマットをサポート    |
|                                                       |
|  Layer 4: HTTP APIs + OCR + Script                    |
|    - http-request ノードの API 呼び出し                 |
|    - OCR（IOcrEngine、Tesseract）                      |
|    - Script（IScriptEngineFactory、Jint JS + Roslyn C# サンドボックス）|
|                                                       |
|  --> 統一された AITool[] に統合し ChatClientAgent へ     |
+------------------------------------------------------+
```

各 agent ノードの `tools` フィールドは tool ID リストを参照し、ResolveToolsAsync が ID に基づいて 4 つのレイヤーから対応するツールインスタンスを取得します。

---

## 7. ミドルウェアパイプライン

ミドルウェアは `DelegatingChatClient`（デコレーターパターン）を採用し、`AgentContextBuilder.ApplyMiddleware()` が順次ラップします：

```
外側                                              内側
  |                                                 |
  v                                                 v
GuardRails --> PII --> RateLimit --> Retry --> Logging --> ChatClient
```

RAG はこのパイプラインとは独立してマウントされます（`RagChatClient` 経由）。

### 7.1 GuardRails — エンタープライズグレードのコンテンツセキュリティ

`IGuardRailsPolicy` インターフェースで分離されており、デフォルト実装は `DefaultGuardRailsPolicy` です。ML 分類器、Azure Content Safety、NVIDIA NeMo Guardrails に置換可能です。

| 機能 | 説明 |
|------|------|
| キーワード + Regex ルール | `text.Contains()`（CJK 安全）+ `RegexOptions.Compiled` |
| 3 段階のアクション | Block（ブロックし拒否メッセージを返却）、Warn（警告するが通過）、Log（サイレント記録） |
| Prompt Injection 検出 | 9 種のビルトインパターン（中英文対応）、opt-in で有効化 |
| トピック制限 | Agent が議論できるトピックをホワイトリストに限定 |
| 全メッセージスキャン | デフォルトですべての User メッセージをスキャン（最後の 1 件だけでなく）、マルチターン攻撃を防止 |
| Output スキャン | LLM 応答のスキャンをオプションで有効化（ストリーミングモードではバッファリング後にスキャン） |
| 監査ログ | `[GUARD] Direction=Input, Action=Block, Rule="hack", Match="hack"` |
| フロントエンド設定 | Blocked/Warn Terms、Regex Rules、Allowed Topics、Injection Detection、カスタムブロック応答 |

### 7.2 PII 保護 — エンタープライズグレードの個人情報検出と匿名化

`IPiiDetector` + `IPiiTokenVault` インターフェースで分離されており、ONNX NER モデル、Microsoft Presidio、Azure AI Language に置換可能です。

| 機能 | 説明 |
|------|------|
| 35 件の Regex ルール × 6 Locale | Global / TW / JP / KR / US / UK、GDPR/HIPAA/PCI-DSS をカバー |
| 7 種の Checksum 検証 | Luhn（クレジットカード）、mod97（IBAN）、台湾身分証/統一編号、JP マイナンバー、KR RRN、UK NHS |
| Context-aware 重み付け | 前後のコンテキストキーワードをスキャンし信頼度を向上、誤検出を低減 |
| 可逆 Tokenization | `[EMAIL_1]`、`[PHONE_1]` 等のタイプ別トークン、LLM 応答後に自動復元 |
| 不可逆モード | `***` 固定置換（下位互換性あり） |
| 双方向スキャン | Input の匿名化 + Output のトークン復元 |
| 監査ログ | `[PII] Direction=Input, Entities=[Global.Email:1, TW.Phone:1], Count=2`（元の PII は絶対に記録しない） |
| フロントエンド設定 | Mode（reversible/irreversible）、Locale 複数選択、Confidence Threshold、Scan Output |

### 7.3 RateLimit — Token Bucket レート制限

`System.Threading.RateLimiting.TokenBucketRateLimiter` を使用（デフォルト毎秒 5 回）。Queue 容量 10、FIFO 順序。`AcquireAsync` には 30 秒のタイムアウト保護があります。

### 7.4 Retry — 指数バックオフリトライ

デフォルトで最大 3 回リトライ、指数バックオフ（500ms → 1s → 2s）。`IsTransient` は `HttpRequestException.StatusCode` のパターンマッチング（429/502/503/504）+ `TaskCanceledException` + `TimeoutException` を使用。リトライ回数を使い切った場合は `LogError` を記録。ストリーミングモードでは最初のチャンク前の失敗のみリトライします。

### 7.5 Logging — 構造化ログ

入力（100 文字に切り詰め）+ 経過時間を記録。非ストリーミング/ストリーミングの両方で try-catch による例外記録（経過時間を含む）を行います。

### 7.6 RAG — 検索拡張生成

独立してマウント（ApplyMiddleware パイプラインには含まれない）。臨時インデックス + 複数ナレッジベースの並行検索（`Task.WhenAll`）をサポート。検索失敗時は graceful degradation（中断せず Warning を記録）。

### 設計のハイライト

- **インターフェース分離**：GuardRails（`IGuardRailsPolicy`）と PII（`IPiiDetector` + `IPiiTokenVault`）はいずれもインターフェースで抽象化されており、ミドルウェアを変更せずに ML/クラウドサービスに置換可能
- **DI のインテリジェントな再利用**：`ApplyMiddleware` は DI singleton を優先使用し、フロントエンドでカスタムルールが指定された場合のみ新しいインスタンスを作成
- **デュアルコンストラクタ**：強化された各ミドルウェアは新版（DI インターフェース注入）+ 旧版（config dictionary）の両コンストラクタを持ち、完全な下位互換性を確保
- **防御的プログラミング**：RateLimit にはタイムアウト保護、Retry には StatusCode パターンマッチング、Logging は例外を記録、RAG は graceful degradation
- **エンタープライズコンプライアンス**：PII 監査ログは GDPR Art.30 に準拠（元の PII は絶対に記録しない）、GuardRails は Prompt Injection 検出とトピック制限をサポート

**拡張方法：** `DelegatingChatClient` を継承して新しいミドルウェアを実装し、`ApplyMiddleware()` に対応する case を追加。または `IGuardRailsPolicy` / `IPiiDetector` を実装して検出エンジンを置換。

---

## 8. Workflow Hooks

Hooks は 6 つのライフサイクル挿入ポイントを提供し、workflow 実行の各段階でトリガーされます：

```
ユーザー入力
    |
    v
 OnInput ---------> 入力のインターセプト/変換が可能
    |
 PreExecute ------> workflow 開始前
    |
    +-- ノードループ --+
    |              |
    | PreAgent     | --> 各 agent 実行前
    | PostAgent    | --> 各 agent 実行後
    |              |
    +--------------+
    |
 OnComplete ------> 正常完了
 OnError ---------> 実行失敗
```

### Hook タイプ

| タイプ | メカニズム |
|------|------|
| `code` | TransformHelper 経由で実行（9 種の変換モードをサポート） |
| `webhook` | 指定 URL へ HTTP POST |

`BlockPattern` は regex インターセプトをサポート -- 入力がパターンにマッチした場合、実行を即座に拒否します。

---

## 9. Credentials バックエンド暗号化ストレージ

API キーはすべてバックエンドで処理され、フロントエンドは平文に触れません：

```
React /settings ページ
    |
    | POST /api/credentials { provider, apiKey }
    v
ICredentialStore.SaveAsync()
    |
    | DPAPI 暗号化（Windows Data Protection API）
    v
SQLite / MongoDB ストレージ（暗号文）

--- 実行時 ---

WorkflowExecutionService
    |
    | ResolveCredentialsAsync()
    v
ICredentialStore.GetDecryptedCredentialsAsync()
    |
    | DPAPI 復号
    v
平文 API Key --> ChatClient に注入
```

フロントエンドは `saved` フラグでキーが設定済みかどうかを判定し、`localStorage` には平文を一切保存しません。

---

## 10. Chat 添付ファイルアップロードパイプライン

CopilotKit のネイティブ機能は画像アップロードのみサポートしており実装も不完全なため、独立したアップロードパイプラインを採用しています：

```
ユーザーがファイルを選択
    |
    | POST /api/upload（multipart/form-data）
    v
バックエンドで一時保存（1 時間 TTL）--> { fileId } を返却
    |
    | CopilotKit forwardedProps.fileId
    v
AG-UI エンドポイントが fileId を受信
    |
    | GetAndRemove(fileId) --> ファイルを取得、一時保存を削除
    v
WorkflowPreprocessor で処理
    |
    +-- ドキュメント類 --> RAG ingest（抽出 + チャンク分割 + インデックス）
    +-- ZIP ファイル --> 解凍 + バッチ処理
    +-- 画像/その他 --> マルチモーダル DataContent（LLM に直接注入）
```

フロントエンドでは `StableChatInput`（module-scope で定義）+ `chatInputFileRef` により component identity の安定性を確保し、CopilotChat が Input コンポーネントを再構築するのを防止しています。

---

## 11. Autonomous Agent -- ReAct + Flow デュアルモード

### 三層実行ファネル

```
+----------------------------------------------------------+
|  Engine Workflow（人間が設計）                              |
|  - 開発者が手動でノードをドラッグ＆ドロップし、フローを定義  |
|  - 完全に確定的                                           |
+----------------------------------------------------------+
                        |
                        v
+----------------------------------------------------------+
|  Flow（AI プランニング + 構造化実行 + Crystallize）         |
|  - IGoalExecutor インターフェース                           |
|  - LLM が FlowPlan を生成 --> 7 種ノードの構造化実行        |
|  - 完了後に編集可能な Workflow として Crystallize            |
+----------------------------------------------------------+
                        |
                        v
+----------------------------------------------------------+
|  ReAct（完全自律）                                         |
|  - ReactExecutor（約 540 行）                              |
|  - 観察 -> 思考 -> 行動 ループ                              |
|  - 12 meta-tools + Sub-agent 連携                         |
+----------------------------------------------------------+
```

### ReAct モードのコア

**ストラテジーオブジェクトの分離：**

| インターフェース | 責務 |
|------|------|
| `IBudgetPolicy` | Token/ステップ数の予算管理 |
| `IHumanInteractionHandler` | ヒューマンインタラクション（ask_user） |
| `IHistoryManager` | 会話履歴の管理 |
| `IReflectionEngine` | 自己反省（Reflexion） |
| `IToolDelegationStrategy` | ツール選択と委譲 |

**デュアルモデルアーキテクチャ：** TaskPlanner は強力なモデル（gpt-4o）でプランニング、ReactExecutor は軽量モデル（gpt-4o-mini）で実行し、コストを削減。

**12 meta-tools（MetaToolFactory）：**
- Sub-agent 管理：create / ask / spawn / collect / stop / send / list
- 共有状態：shared_state
- ヒューマンインタラクション：ask_user
- 品質管理：peer_review / challenge

**セキュリティレベル：** P0 Risk 承認 -> P1 Transparency -> P2 Self-Reflection -> S1~S8 隔離保護

### Flow モードのコア

`IGoalExecutor` インターフェースにより ReAct と分離。DI 切替：

```csharp
// ReAct モード
services.AddAutonomousAgent();

// Flow モード
services.AddAutonomousFlowAgent();
```

**ファネル接続：** ReactTraceConverter が spawn/collect の軌跡を FlowPlan JSON に変換し、ExecutionMemoryService に保存。次回の Flow プランニング時に Reference Plan として注入されます。

**Crystallize：** 実行完了後、ExecutionTrace を Studio buildFromAiSpec JSON に変換し、`Data/flow-outputs/` に保存。Workflow Studio に直接ロードして編集できます。

---

## 12. CraftSearch 検索エンジン

`AgentCraftLab.Search` は独立したクラスライブラリであり、Engine や Autonomous に依存しません。

### コアインターフェース

```
ISearchEngine          --> 検索エントリ（query + options）
IDocumentExtractor     --> ドキュメント内容の抽出（PDF/DOCX/HTML/TXT...）
ITextChunker           --> テキストチャンク分割（固定サイズ + オーバーラップ）
```

### 3 種の検索モード

```
+------------------+     +------------------+     +------------------+
|   FullText       |     |   Vector         |     |   Hybrid         |
|   (FTS5)         |     |   (SIMD Cosine)  |     |   (RRF k=60)    |
|                  |     |                  |     |                  |
|  SQLite FTS5     |     |  1536 次元ベクトル |     |  FullText ランク  |
|  トークン化+BM25 |     |  コサイン類似度    |     |  + Vector ランク  |
|                  |     |  SIMD 高速化      |     |  RRF 融合         |
+------------------+     +------------------+     +------------------+
```

**RRF（Reciprocal Rank Fusion）：** k=60 で全文検索とベクトル検索の 2 つのランキングを融合し、キーワードの完全一致と意味的類似度を両立させます。

**Provider 実装：**
- `SqliteSearchEngine` -- 本番環境（オープンソース）
- `InMemorySearchEngine` -- ユニットテスト

---

## 13. RAG パイプライン

RAG 機能は CraftSearch の上に構築されており、完全な検索拡張生成パイプラインを提供します。

### Ingest フロー

```
ファイルアップロード
    |
    v
IDocumentExtractor.ExtractAsync()     --> テキスト抽出（複数フォーマット対応）
    |
    v
ITextChunker.ChunkAsync()            --> チャンク分割（固定サイズ + オーバーラップウィンドウ）
    |
    v
Embedding（1536 次元）                --> ベクトル化
    |
    v
ISearchEngine.IndexAsync()           --> インデックス構築
```

### クエリフロー

```
ユーザーの質問
    |
    v
RagService.SearchAsync()             --> ハイブリッド検索（FTS5 + Vector + RRF）
    |
    v
RagChatClient（DelegatingChatClient） --> 検索結果を system message に注入
    |
    v
LLM 応答（検索されたコンテキストに基づく）
```

### indexName の命名規則

| フォーマット | 用途 |
|------|------|
| `{userId}_rag_{guid}` | 臨時インデックス（単発アップロード） |
| `{userId}_kb_{id}` | ナレッジベースインデックス（永続化） |

---

## 14. CopilotKit フロントエンドアーキテクチャ

### システム全体像

```
+-------------------+     +-------------------+     +-------------------+
|   React           |     |  CopilotKit       |     |  .NET API         |
|   フロントエンド   |     |  Runtime          |     |  バックエンド      |
|   (port 5173)     |     |  (port 4000)      |     |  (port 5200)      |
|                   |     |                   |     |                   |
|  React Flow       | --> |  Node.js          | --> |  Minimal API      |
|  CopilotKit SDK   |     |  server.mjs       |     |  AG-UI エンドポイント |
|  shadcn/ui        |     |  AG-UI プロトコル  |     |  WorkflowEngine   |
|  i18n (en/zh-TW)  |     |  転送              |     |  CraftSearch      |
+-------------------+     +-------------------+     +-------------------+
      Vite dev                  中間層                   バックエンドサービス
```

### AG-UI プロトコル

CopilotKit Runtime は中間層として機能し、フロントエンドの CopilotKit フォーマットを AG-UI（Agent-UI）プロトコルに変換して .NET バックエンドと通信します。バックエンドは `IAsyncEnumerable<ExecutionEvent>` でイベントをフロントエンドにストリーミングします。

### フロントエンドの主要モジュール

| モジュール | 説明 |
|------|------|
| Workflow Studio | React Flow キャンバス、ドラッグ＆ドロップで workflow を構築 |
| Chat Panel | CopilotChat 統合、添付ファイルアップロードをサポート |
| Settings | 個人設定、Credentials、デフォルトモデル |
| Skill Manager | スキル管理（ビルトイン + カスタム） |
| Service Tester | デュアルパネル + Chat、5 種のプロトコルテスト |
| KB Manager | ナレッジベースファイルアップロード + SSE 進捗ストリーミング |

### 主要な設計上の決定事項

- **独立アップロードパイプライン：** CopilotKit のネイティブアップロードには制約が多いため、`POST /api/upload` による独立アップロードを採用
- **ErrorBoundary：** グローバルエラーバウンダリ、React コンポーネントのクラッシュによるホワイトスクリーンを防止
- **i18n：** en + zh-TW をサポート、common / studio / chat の 3 つの namespace に分割
- **StableChatInput：** module-scope で定義、CopilotChat の再構築による入力フィールドの状態消失を防止

---

## 付録：拡張クイックリファレンス表

| 拡張項目 | 手順 |
|----------|------|
| 新しい実行ストラテジー | `IWorkflowStrategy` を実装 + `WorkflowStrategyResolver.Resolve()` に case を追加 |
| 新しいノードタイプ | `NodeTypes` 定数 + `NodeTypeRegistry` メタデータ + `NodeExecutorRegistry` handler + JS `NODE_REGISTRY` |
| 新しいビルトインツール | `ToolImplementations.cs` にメソッド追加 + `ToolRegistryService.Register()` |
| 新しいミドルウェア | `DelegatingChatClient` を継承 + `ApplyMiddleware()` に case を追加 |
| 新しい Flow ノード | `FlowNodeRunner` case + `FlowPlannerPrompt` + `FlowPlanValidator` + `WorkflowCrystallizer` |
| スクリプトエンジンの置換 | `IScriptEngine` を実装 + DI 置換（Jint JS / Roslyn C# / Python） |
| スクリプト言語の追加 | `ScriptEngineFactory.Register("language", engine)` + フロントエンド CodeForm SCRIPT_LANGUAGES に���プション追加 |
| OCR エンジンの置換 | `IOcrEngine` を実装 + DI 置換 |
| 新しいツールモジュール | `AgentCraftLab.Ocr` / `AgentCraftLab.Script` の `AddXxx()` + `UseXxxTools()` パターンを参照 |
| Autonomous ストラテジーの置換 | 対応するインターフェース（IBudgetPolicy 等）を実装 + DI `Replace` 登録 |
