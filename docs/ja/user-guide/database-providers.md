# データベース Provider

AgentCraftLab は複数のデータベースバックエンドをサポートしています。デフォルトでは **SQLite** を使用し、設定不要で動作します。MongoDB に切り替え可能で、今後さらに多くの Provider（MSSQL、PostgreSQL）を予定しています。

---

## サポートされる Provider

| Provider | ステータス | ユースケース |
|----------|-----------|-------------|
| **SQLite** | デフォルト | ローカル開発、シングルユーザーデプロイ |
| **MongoDB** | 利用可能 | マルチユーザー、クラウドデプロイ、Azure Cosmos DB |
| **MSSQL** | 予定 | エンタープライズ環境 |
| **PostgreSQL** | 予定 | クラウドネイティブデプロイ |

---

## SQLite（デフォルト）

設定不要。データはローカルの `Data/agentcraftlab.db` に保存されます。

---

## MongoDB

### 前提条件

- MongoDB Atlas、Azure Cosmos DB for MongoDB、またはセルフホスト MongoDB 6.0+
- 読み書き権限のある接続文字列

### 設定方法

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

MongoDB を有効にすると、以下の 8 つの Store が MongoDB に移行されます：

| Store | データ内容 |
|-------|-----------|
| WorkflowStore | Workflow 定義 |
| CredentialStore | 暗号化された API キー |
| SkillStore | カスタム Agent スキル |
| TemplateStore | Workflow テンプレート |
| RequestLogStore | 実行ログ |
| KnowledgeBaseStore | ナレッジベースメタデータ |
| ApiKeyStore | 公開済み API キー |
| ScheduleStore | スケジュールタスク |

> **注意：** 一部の内部 Store（実行メモリ、Checkpoint など）は現在 SQLite のままです。起動時の警告メッセージで未カバーの Store が表示されます。

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
