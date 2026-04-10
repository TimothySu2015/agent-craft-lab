# データベース Provider

AgentCraftLab は**データ層分離アーキテクチャ**を採用しています。15 個の Store インターフェースは純粋な抽象プロジェクト `AgentCraftLab.Data`（依存関係ゼロ）に定義され、各データベース Provider は `extensions/data/` 配下の独立プロジェクトとして実装されます。

デフォルトでは **SQLite** を使用し、設定不要で動作します。MongoDB、PostgreSQL、SQL Server に切り替え可能です。

---

## プロジェクト構成

```
extensions/data/
├── AgentCraftLab.Data/              # 純粋な抽象（15 インターフェース、DTO、依存関係ゼロ）
├── AgentCraftLab.Data.Sqlite/       # SQLite Provider（EF Core）
├── AgentCraftLab.Data.MongoDB/      # MongoDB Provider
├── AgentCraftLab.Data.PostgreSQL/   # PostgreSQL Provider（EF Core + Npgsql）
└── AgentCraftLab.Data.SqlServer/    # SQL Server Provider（EF Core）
```

> **設計方針：** `AgentCraftLab.Engine` は **EF Core に依存しません**。Engine は `AgentCraftLab.Data`（インターフェースのみ）に依存し、実際のデータベース実装はホストレベルで合成されます。

DI パターン：

```csharp
builder.Services.AddAgentCraftEngine();           // Engine コア（DB 非依存）
builder.Services.AddSqliteDataProvider();          // または AddMongoDbProvider() / AddPostgreSqlDataProvider() / AddSqlServerDataProvider()
```

---

## サポートされる Provider

| Provider | ステータス | プロジェクト | ユースケース |
|----------|-----------|-------------|-------------|
| **SQLite** | デフォルト | `AgentCraftLab.Data.Sqlite` | ローカル開発、シングルユーザーデプロイ |
| **MongoDB** | 利用可能 | `AgentCraftLab.Data.MongoDB` | マルチユーザー、クラウドデプロイ、Azure Cosmos DB |
| **PostgreSQL** | 利用可能 | `AgentCraftLab.Data.PostgreSQL` | クラウドネイティブデプロイ、PgVector 検索対応 |
| **SQL Server** | 利用可能 | `AgentCraftLab.Data.SqlServer` | エンタープライズ環境、Windows / Azure SQL |

---

## SQLite（デフォルト）

設定不要。データはローカルの `Data/agentcraftlab.db` に保存されます。

実装プロジェクト：`extensions/data/AgentCraftLab.Data.Sqlite/`（EF Core + 15 個の Store 実装）

---

## MongoDB

### 前提条件

- MongoDB Atlas、Azure Cosmos DB for MongoDB、またはセルフホスト MongoDB 6.0+
- 読み書き権限のある接続文字列

### 設定方法

実装プロジェクト：`extensions/data/AgentCraftLab.Data.MongoDB/`

`appsettings.json`（または `appsettings.Development.json`）に以下を追加：

```json
{
  "Database": {
    "Provider": "mongodb",
    "ConnectionString": "mongodb+srv://user:password@host/?tls=true&authMechanism=SCRAM-SHA-256",
    "DatabaseName": "agentcraftlab"
  }
}
```

API サーバーを再起動すると、起動ログに表示されます：

```
info: AgentCraftLab  Database Provider: mongodb
```

### MongoDB に保存されるデータ

MongoDB を有効にすると、**15 個すべての Store インターフェース**が MongoDB に移行されます。Workflow、Credential、Template などのビジネスデータだけでなく、実行メモリ、Checkpoint、Entity メモリなどの内部 Store もすべて MongoDB で管理されます。

### SQLite に戻す

`appsettings.json` の `Database` セクションを削除またはコメントアウト：

```json
{
  // "Database" セクションなし = SQLite（デフォルト）
}
```

### Azure Cosmos DB for MongoDB

AgentCraftLab は Azure Cosmos DB for MongoDB API と互換性があります。Azure Portal の接続文字列を使用：

```json
{
  "Database": {
    "Provider": "mongodb",
    "ConnectionString": "mongodb+srv://user:password@yourcluster.mongocluster.cosmos.azure.com/?tls=true&authMechanism=SCRAM-SHA-256&retrywrites=false&maxIdleTimeMS=120000",
    "DatabaseName": "agentcraftlab"
  }
}
```

### MongoDB Atlas Search（オプション）

MongoDB Atlas を使用している場合、内蔵の SQLite 検索エンジンを Atlas Vector Search + Atlas Search に置き換えることもできます：

```csharp
// Program.cs の AddMongoDbProvider の後に追加：
builder.Services.AddMongoSearch();
```

有効化される機能：
- **Atlas Vector Search** — セマンティック類似性検索
- **Atlas Search** — 全文検索
- **RRF ハイブリッド検索** — ベクトルと全文の組み合わせ

> Atlas なしのセルフホスト MongoDB では、自動的に regex 全文検索にフォールバックします。

---

## PostgreSQL

### 前提条件

- PostgreSQL 14 以上（セルフホストまたはクラウドマネージド）
- 読み書き権限のある接続文字列
- （オプション）PgVector 拡張機能がインストール済み（ベクトル検索を使用する場合）

### 設定方法

実装プロジェクト：`extensions/data/AgentCraftLab.Data.PostgreSQL/`

`appsettings.json`（または `appsettings.Development.json`）に以下を追加：

```json
{
  "Database": {
    "Provider": "postgresql",
    "ConnectionString": "Host=localhost;Port=5432;Database=agentcraftlab;Username=user;Password=pass"
  }
}
```

API サーバーを再起動すると、起動ログに表示されます：

```
info: AgentCraftLab  Database Provider: postgresql
```

### PgVector 検索（オプション）

PostgreSQL で PgVector 拡張機能を使用している場合、フロントエンドの **Settings → Data Sources** から PgVector データソースを追加し、ナレッジベースにバインドすることでベクトル検索を有効化できます。`appsettings.json` での追加設定は不要です。

---

## SQL Server

### 前提条件

- SQL Server 2019 以上、Azure SQL Database、または Azure SQL Managed Instance
- 読み書き権限のある接続文字列

### 設定方法

実装プロジェクト：`extensions/data/AgentCraftLab.Data.SqlServer/`

`appsettings.json`（または `appsettings.Development.json`）に以下を追加：

```json
{
  "Database": {
    "Provider": "sqlserver",
    "ConnectionString": "Server=localhost;Database=agentcraftlab;Trusted_Connection=True;TrustServerCertificate=True"
  }
}
```

API サーバーを再起動すると、起動ログに表示されます：

```
info: AgentCraftLab  Database Provider: sqlserver
```

> **Azure SQL の場合**：接続文字列を Azure Portal から取得し、`Trusted_Connection` の代わりに `User ID` と `Password` を使用してください。

---

## マルチプロバイダー RAG 検索ルーティング

AgentCraftLab では、異なるナレッジベース（KB）が異なる検索エンジンを使用できます。`SearchEngineFactory` が各 KB の `DataSourceId` に基づいて適切な検索エンジンにルーティングします。

### 仕組み

ユーザーはまず **Settings → Data Sources** でデータソース接続を作成し、ナレッジベース作成時にそれを選択する必要があります。**新規 KB** は DataSource の選択が必須です。**既存の KB**（DataSource 未設定）は後方互換性のためデフォルトの SQLite エンジンが自動的に使用されます。

検索リクエストが来ると、`SearchEngineFactory` が DataSourceId を参照し、対応する検索エンジンにルーティングします。これにより、同一システム内で複数の検索バックエンドを混在させることが可能です。

### サポートされる検索プロバイダー

| 検索プロバイダー | 説明 |
|-----------------|------|
| **SQLite FTS5** | デフォルト。全文検索 + trigram + BM25 スコアリング |
| **PgVector** | PostgreSQL + PgVector 拡張機能によるベクトル検索 |
| **Qdrant** | 外部 Qdrant サーバーによるベクトル検索 |
| **MongoDB Atlas** | Atlas Vector Search + Atlas Search |

### 並列検索とマージ

複数の KB が検索対象に含まれる場合、各 KB の検索は**並列で実行**されます。結果は RRF（Reciprocal Rank Fusion）アルゴリズムでマージされ、統一されたランキングとして返されます。

```
ユーザークエリ
  ├── KB-A（SQLite FTS5）   ──┐
  ├── KB-B（PgVector）       ──┼── 並列検索 → RRF マージ → 統一ランキング
  └── KB-C（MongoDB Atlas）  ──┘
```

> **設計方針：** データベース Provider（データ保存）と検索プロバイダー（RAG 検索）は独立した概念です。例えば、データ保存に PostgreSQL を使いつつ、特定の KB では Qdrant で検索するといった組み合わせが可能です。

---

## カスタム Provider の追加

新しいデータベース Provider を追加する手順は、開発者ガイドを参照してください：

→ [拡張ガイド - 新しいデータベース Provider の追加](../developer-guide/extending.md#9-新しいデータベース-provider-の追加)
