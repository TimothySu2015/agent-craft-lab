# AgentCraftLab -- クイックスタートガイド

本ガイドでは、数分で AgentCraftLab を起動し、最初のマルチエージェントワークフローを作成・実行する方法をご案内します。

---

## 1. システム要件

| 項目 | 最低バージョン |
|------|----------|
| .NET SDK | 10.0 Preview |
| Node.js | 20 LTS 以上 |
| npm | Node.js に同梱のもので可 |
| OS | Windows 10+、macOS、Linux |

> AgentCraftLab はデフォルトで **SQLite** を使用するため、別途データベースのインストールは不要です。

---

## 2. Docker デプロイ（推奨）

Docker を使えば最速で起動できます。ローカルに .NET や Node.js をインストールする必要はありません。

### 2.1 前提条件

| 項目 | 最低バージョン |
|------|----------|
| Docker | 20.10+ |
| Docker Compose | v2.0+ |

### 2.2 ワンコマンドで起動

```bash
git clone https://github.com/TimothySu2015/agent-craft-lab.git
cd agent-craft-lab
cp .env.example .env
# .env を編集して LLM API Key を追加（例: OPENAI_API_KEY）
docker compose up --build
```

ビルド完了後、**http://localhost:3000** を開いて Workflow Studio にアクセスできます。

### 2.3 設定

`.env` ファイルを編集してカスタマイズ：

| 変数 | デフォルト | 説明 |
|------|-----------|------|
| `WEB_PORT` | 3000 | Web UI ポート |
| `API_PORT` | 5200 | API ポート |
| `DATABASE_PROVIDER` | sqlite | データベース Provider（sqlite / postgresql / mongodb / sqlserver） |
| `OPENAI_API_KEY` | - | OpenAI API Key |
| `AZURE_OPENAI_API_KEY` | - | Azure OpenAI API Key |
| `AZURE_OPENAI_ENDPOINT` | - | Azure OpenAI Endpoint |

### 2.4 データの永続化

すべてのデータは `Data/` ディレクトリ（Docker ボリュームとしてマウント）に保存されます：
- SQLite データベース
- 暗号化された認証情報（Data Protection キー）
- アップロードされたファイル

コンテナを再起動してもデータは保持されます。

### 2.5 PostgreSQL の使用（オプション）

```bash
DATABASE_PROVIDER=postgresql \
DATABASE_CONNECTION_STRING="Host=postgres;Port=5432;Database=agentcraftlab;Username=agentcraftlab;Password=changeme" \
docker compose --profile postgres up --build
```

---

## 3. ローカル開発セットアップ

Docker を使わずにローカルで開発する場合：

### 3.1 ソースコードの取得

```bash
git clone https://github.com/your-org/AgentCraftLab.git
cd AgentCraftLab
```

### 3.2 フロントエンドの依存パッケージをインストール

```bash
cd AgentCraftLab.Web
npm install
cd ..
```

### 3.3 3つのサービスを起動

AgentCraftLab はフロントエンドとバックエンドが分離したアーキテクチャを採用しており、3つのターミナルを同時に起動する必要があります。

**Terminal 1 -- .NET API バックエンド（port 5200）**

```bash
dotnet run --project AgentCraftLab.Api
```

`Now listening on: http://localhost:5200` と表示されたら、次のターミナルを開きます。

**Terminal 2 -- CopilotKit Runtime（port 4000）**

```bash
cd AgentCraftLab.Web
node server.mjs
```

**Terminal 3 -- React 開発サーバー（port 5173）**

```bash
cd AgentCraftLab.Web
npm run dev:vite
```

### 3.4 ブラウザを開く

**http://localhost:5173** にアクセスしてください。Workflow Studio のインターフェースが表示されます。

> ログイン不要で、システムは `local` ユーザーとして動作します。

---

## 4. API Credentials の設定

LLM エージェントを含むワークフローを実行する前に、少なくとも1つの AI モデルの API Key を設定する必要があります。

1. 左側ナビゲーションバーの **Settings** をクリックします（または `/settings` に直接アクセス）。
2. **Credentials** セクションを見つけます。
3. API Key を入力します。例：
   - **OpenAI API Key** -- GPT-4o、GPT-4o-mini などのモデルに使用
   - **Azure OpenAI** -- Endpoint と Deployment Name の追加入力が必要
   - **Anthropic API Key** -- Claude シリーズのモデルに使用
   - **Google AI API Key** -- Gemini シリーズのモデルに使用
4. **Save** をクリックして保存します。

すべての API Key は DPAPI 暗号化によりバックエンドに保存され、フロントエンドに平文は残りません。

---

## 4. 最初のワークフローを作成する

### 方法1：テンプレートから作成

1. Workflow Studio ページで **Templates** をクリックします。
2. **Basic** カテゴリの **Simple Chat** テンプレートを選択します。
3. テンプレートが自動的にキャンバスに読み込まれ、`start` ノード、`agent` ノード、`end` ノードが配置されます。
4. `agent` ノードをクリックし、右側パネルでモデル設定を確認します（例：`gpt-4o-mini`）。

### 方法2：AI Build で自然言語から作成

1. Workflow Studio ページで **AI Build** パネルを開きます。
2. 自然言語でワークフローを記述します。例：

   ```
   翻訳エージェントを作成してください。ユーザーが入力した中国語を英語と日本語に翻訳し、最後に結果をまとめて返してください。
   ```

3. AI が対応するノードと接続を自動生成し、キャンバスに読み込みます。
4. ノード設定を手動で微調整してから実行できます。

---

## 5. テスト実行

1. **Execute** タブに切り替えます（キャンバス右側のチャットパネル）。
2. 入力ボックスにメッセージを入力します。例：`こんにちは、自己紹介をお願いします`。
3. 送信を押して、エージェントのストリーミング応答を確認します。
4. ワークフローに複数のノードが含まれている場合、実行中に各ノードの実行状態を確認できます。

> API Key 関連のエラーが表示された場合は、Settings ページに戻り、Credentials が正しく設定されているか確認してください。

---

## 6. 主要コンセプト一覧

| コンセプト | 説明 |
|------|------|
| **ノード（Node）** | ワークフローの基本単位。`agent` ノードは LLM を呼び出し、`code` ノードはデータ変換を行い、`condition` ノードは条件分岐を行います。 |
| **接続（Edge）** | ノード間の実行順序とデータフローを定義します。 |
| **ツール（Tool）** | エージェントが使用できる外部機能。ビルトインツール、MCP Server、A2A Agent、HTTP API の4層のソースがあります。 |
| **ストラテジー（Strategy）** | システムがノードタイプに基づいて自動的に実行戦略を選択します：Sequential、Handoff、Imperative など。 |
| **ミドルウェア（Middleware）** | GuardRails、PII フィルタリング、Rate Limit などのミドルウェア層を適用できます。 |

---

## 7. よく使うページ

| ページ | パス | 用途 |
|------|------|------|
| Workflow Studio | `/` | ワークフローの視覚的な設計と実行 |
| Settings | `/settings` | API Credentials、言語設定、デフォルトモデル |
| Skills | `/skills` | エージェントスキルの管理 |
| Service Tester | `/tester` | MCP / A2A / HTTP など外部サービスのテスト |
| Schedules | `/schedules` | スケジュール管理 |

---

## 8. 次のステップ

- **高度なノードタイプ**：ワークフローに `condition`（条件分岐）、`iteration`（ループ）、`parallel`（並列処理）などのノードを追加してみてください。
- **外部ツール統合**：エージェントノードに MCP Server や HTTP API を接続して、エージェントの能力を拡張できます。
- **ナレッジベース（RAG）**：ドキュメントをアップロードしてナレッジベースを構築し、エージェントに専門知識を持たせることができます。
- **Autonomous Agent**：`autonomous` ノードを使用して、AI に複雑なタスクの計画と実行を自律的に行わせることができます。
- **Export デプロイ**：完成したワークフローをスタンドアロンのデプロイパッケージとしてエクスポートできます。

ご不明な点がございましたら、`docs/` ディレクトリ内のその他の設計ドキュメントをご参照いただくか、プロジェクトの `CLAUDE.md` でアーキテクチャの詳細をご確認ください。
