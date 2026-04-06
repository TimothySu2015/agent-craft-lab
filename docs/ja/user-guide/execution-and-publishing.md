# ワークフローの実行とサービス公開

本ドキュメントでは、AgentCraftLab のワークフロー実行方法、サービス公開フロー、および関連設定について説明します。

---

## Part 1: ワークフローの実行

### 1.1 Execute Chat

Workflow Studio で **Execute** タブに切り替えると、現在のワークフローを実行できます。メッセージを入力して送信すると、システムが AG-UI Protocol の SSE ストリーミングで実行過程と結果をリアルタイムに返します。

実行フローは以下のとおりです：

1. ユーザーが Execute Chat にメッセージを入力
2. バックエンドの `WorkflowExecutionService.ExecuteAsync()` がリクエストを受信
3. Hook（OnInput）、前処理（WorkflowPreprocessor）、ストラテジー選択を経て実行
4. 実行過程は `IAsyncEnumerable<ExecutionEvent>` としてフロントエンドにストリーミング
5. フロントエンドが各エージェントの応答テキスト、ツール呼び出し結果などをリアルタイム表示

### 1.2 Chat 添付ファイルアップロード

Execute Chat はファイル添付機能をサポートしています。操作方法：

1. チャット入力ボックス横の添付ファイルボタンをクリック
2. ファイルを選択（PDF、DOCX、画像などの形式に対応、上限 32MB）
3. ファイルは `POST /api/upload` にアップロードされ、`fileId` を取得（1時間の一時保存）
4. メッセージ送信時、`fileId` がリクエストとともにバックエンドに送信されます
5. バックエンドがファイルタイプに応じて処理方法を決定：RAG ingest、ZIP 解凍、またはマルチモーダル DataContent としてエージェントに渡す

CopilotKit のネイティブ機能は画像アップロードのみ対応で実装が未完成のため、システムは独立したアップロードパイプラインでこの制限を回避しています。

### 1.3 5つの実行ストラテジー

システムはワークフローのノード構成に基づいて実行戦略を自動的に選択するため、ユーザーが手動で設定する必要はありません。

| ストラテジー | 説明 | 自動検出条件 |
|------|------|-------------|
| **Single** | 単一のエージェントが直接実行 | ワークフローに実行可能なノードが1つのみ |
| **Sequential** | 複数のエージェントを順番に実行 | 複数のエージェントがそれぞれ1つの outgoing 接続のみ |
| **Concurrent** | 複数のエージェントを同時に並列実行 | 明示的に並列とマークされたノードグループ |
| **Handoff** | エージェント間で制御権を受け渡し | いずれかのエージェントに複数の outgoing 接続がある |
| **Imperative** | 命令型フロー制御 | condition、loop、human などのフロー制御ノードを含む |

自動検出ロジック：`NodeTypeRegistry.HasAnyRequiringImperative()` で Imperative が必要なノードタイプ（condition、loop、human、code、iteration、parallel など）の有無を確認します。該当するノードがあれば Imperative を使用し、いずれかのエージェントに複数の outgoing 接続があれば Handoff を使用し、それ以外はノード数に応じて Single または Sequential を選択します。

### 1.4 Human Input ノード

Human ノードは実行中にワークフローを一時停止し、ユーザーの入力を受けてから続行します。3つのインタラクションモードをサポートしています：

- **text** -- 自由テキスト入力。ユーザーは任意の返答を入力できます
- **choice** -- 選択式。プリセットの選択肢から1つを選びます
- **approval** -- 承認/拒否。承認フローに使用します

実行が Human ノードに到達すると、システムは `WaitingForInput` イベントを発行し、フロントエンドが対応する入力インターフェースを表示します。ユーザーが送信すると `UserInputReceived` イベントが発行され、フローの実行が続行されます。

### 1.5 Execution Events

実行中、システムはイベントストリーム（Event Stream）で進捗を報告します。主なイベントタイプ：

| イベント | 説明 |
|------|------|
| `AgentStarted` | エージェントの実行開始（エージェント名を含む） |
| `TextChunk` | ストリーミングテキスト断片（リアルタイム出力） |
| `AgentCompleted` | エージェントの実行完了（完全な応答を含む） |
| `ToolCall` | ツール呼び出し（ツール名とパラメータを含む） |
| `ToolResult` | ツールの返却結果 |
| `WaitingForInput` | ユーザー入力待ち（Human ノードで一時停止） |
| `UserInputReceived` | ユーザー入力を受信 |
| `RagProcessing` / `RagReady` | RAG パイプラインの処理状態 |
| `HookExecuted` / `HookBlocked` | Hook の実行結果 |
| `WorkflowCompleted` | ワークフロー全体の実行完了 |
| `Error` | エラーイベント |

これらのイベントは `AgUiEventConverter` により AG-UI Protocol 形式に変換され、SSE でフロントエンドにプッシュされます。

### 1.6 Autonomous モード

Autonomous ノードは AI の自律実行機能を提供し、2つのモードがあります：

**ReAct モード（完全自律）：** ReactExecutor が Reasoning-Acting ループで動作します。AI が次のアクションを自ら決定し、ツールを呼び出し、結果を観察して、タスクが完了するまで続けます。12種類の meta-tools（サブエージェントの作成/問い合わせ/生成、共有状態、ユーザー確認の要求など）をサポートしています。デュアルモデルアーキテクチャを採用 -- TaskPlanner は強力なモデルで計画を立て、ReactExecutor は軽量モデルで実行します。

**Flow モード（構造化実行）：** LLM がまず実行計画（FlowPlan）を策定し、計画に従って7種類のノード（agent / code / condition / iteration / parallel / loop / http-request）を順次実行します。実行完了後、Crystallize により結果を再利用可能なワークフローに変換できます。

3層ファネルの関係：Engine Workflow（人間が設計）> Flow（AI が計画 + 構造化実行）> ReAct（完全自律）。

---

## Part 2: サービス公開

### 2.1 Publish Workflow

`/published-services` ページでワークフローの公開状態を管理します。

**公開の有効化/無効化：** 各ワークフローは個別に公開を有効化または無効化できます。有効化すると、そのワークフローは API 呼び出しやその他のプロトコルでアクセス可能になります。

**Input Modes：** 公開時に受け付ける入力形式を設定できます：

- **text/plain** -- プレーンテキスト入力（デフォルト）
- **application/pdf**、**application/vnd.openxmlformats-officedocument.wordprocessingml.document** など -- ファイル入力
- **application/json** -- 構造化 JSON 入力

Input Modes は、外部の呼び出し元がそのワークフローにどの形式のデータを送信できるかを決定します。

### 2.2 API Keys 管理

`/api-keys` ページで API キーを管理します。

**API Key の作成：**

1. 作成ボタンをクリック
2. 名前/説明を入力（用途の識別に便利）
3. オプションで Scope を選択し、この Key で特定のワークフローのみアクセス可能に制限
4. システムがキーを生成。表示は一度のみなので、安全に保管してください

**Scope 制限：** Key の作成時に、アクセスを許可する公開済みワークフローを指定でき、最小権限の原則を実現します。Scope を指定しない場合は、すべての公開済みワークフローにアクセスできます。

**Key の取り消し：** API Keys リストでいつでも不要なキーを取り消せます。取り消しは即座に有効になり、そのキーはいかなるリクエストにも使用できなくなります。

### 2.3 スケジュール管理（商用モード）

`/schedules` ページでスケジュールタスクを管理します。この機能は商用モード（MongoDB + OAuth）でのみ利用可能です。

機能には以下が含まれます：

- **スケジュールの作成** -- cron 式、ターゲットワークフロー、入力パラメータを設定
- **有効化/無効化** -- トグルスイッチでスケジュールの有効/無効を制御
- **実行履歴** -- 各スケジュールトリガーの実行結果と時間を確認

### 2.4 Service Tester

`/service-tester` ページで公開済みサービスまたは外部エンドポイントをテストします。デュアルパネル設計（設定パネル + 会話パネル）を採用し、5つのプロトコルに対応しています：

| プロトコル | 説明 |
|------|------|
| **AG-UI** | AgentCraftLab ネイティブの Agent-UI ストリーミングプロトコル |
| **A2A** | Google Agent-to-Agent プロトコル。クロスサービスのエージェント通信に使用 |
| **MCP** | Model Context Protocol。ツール統合テストに使用 |
| **HTTP** | 標準 REST API 呼び出し |
| **Teams** | Microsoft Teams Bot プロトコル |

公開済みのワークフローを選択するか外部エンドポイントの URL を入力して、Chat パネルでインタラクティブなテストを行えます。

### 2.5 Request Logs

`/request-logs` ページで実行ログと分析を確認できます。各 API 呼び出しのリクエスト内容、応答結果、実行時間などの情報を確認でき、デバッグとパフォーマンス分析に活用できます。

---

## Part 3: 設定

### 3.1 Settings ページ

`/settings` ページで個人設定を行います。以下のセクションが含まれます：

**Profile：** 個人情報の設定。商用モードでは OAuth ログイン情報が表示されます。

**言語設定：** インターフェース言語の切り替え。現在、英語（en）と繁体字中国語（zh-TW）をサポートしています。

**デフォルトモデル：** エージェントのデフォルト LLM モデルを設定します。対応プロバイダーは OpenAI、Azure OpenAI、Ollama、Azure Foundry、GitHub Copilot、Anthropic、AWS Bedrock です。

### 3.2 Credentials 管理

Settings ページの Credentials セクションで各 AI サービスの API キーを管理します：

1. プロバイダーを選択（OpenAI、Azure OpenAI など）
2. 対応する API Key、Endpoint などの認証情報を入力
3. 保存後、認証情報は DPAPI 暗号化でバックエンドの `ICredentialStore` に保存されます
4. フロントエンドには平文の Key は保存されず、「保存済み」ステータスのみ記録されます

ワークフロー実行時、バックエンドは `ResolveCredentialsAsync()` を通じて `ICredentialStore` から復号化された認証情報を読み取り、フロントエンドからは API Key が送信されなくなります。

### 3.3 Budget 設定

トークン使用量の予算上限を設定し、予期しない高額費用を防止します。モデル別または全体で予算制限を設定できます。

---

## 付録：オープンソースモードと商用モードの違い

| 項目 | オープンソースモード（デフォルト） | 商用モード |
|------|-----------------|---------|
| データベース | SQLite | MongoDB (Azure DocumentDB) |
| 認証 | ログイン不要（userId="local"） | Google / GitHub OAuth |
| スケジュール管理 | 利用不可 | 利用可能 |
| 有効化方法 | デフォルト | `ConnectionStrings:MongoDB` を設定 |
