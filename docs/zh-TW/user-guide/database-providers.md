# 資料庫 Provider

AgentCraftLab 支援多種資料庫後端。預設使用 **SQLite**，零設定即可運作。可切換為 MongoDB，未來將支援更多 Provider（MSSQL、PostgreSQL）。

---

## 支援的 Provider

| Provider | 狀態 | 適用場景 |
|----------|------|---------|
| **SQLite** | 預設 | 本機開發、單人部署 |
| **MongoDB** | 可用 | 多人協作、雲端部署、Azure Cosmos DB |
| **MSSQL** | 規劃中 | 企業環境 |
| **PostgreSQL** | 規劃中 | 雲原生部署 |

---

## SQLite（預設）

不需要任何設定。資料儲存在本機 `Data/agentcraftlab.db`。

---

## MongoDB

### 前置條件

- MongoDB Atlas、Azure Cosmos DB for MongoDB、或自架 MongoDB 6.0+
- 具有讀寫權限的連線字串

### 設定方式

在 `appsettings.json`（或 `appsettings.Development.json`）中加入：

```json
{
  "Database": {
    "Provider": "mongodb",
    "ConnectionString": "mongodb+srv://user:password@host/?tls=true&authMechanism=SCRAM-SHA-256",
    "DatabaseName": "agentcraftlab"
  }
}
```

重啟 API 伺服器，啟動時會看到：

```
info: AgentCraftLab  Database Provider: mongodb
```

### 哪些資料會存入 MongoDB

啟用 MongoDB 後，以下 8 個 Store 會改為使用 MongoDB：

| Store | 資料內容 |
|-------|---------|
| WorkflowStore | Workflow 定義 |
| CredentialStore | 加密的 API 金鑰 |
| SkillStore | 自訂 Agent 技能 |
| TemplateStore | Workflow 範本 |
| RequestLogStore | 執行記錄 |
| KnowledgeBaseStore | 知識庫元資料 |
| ApiKeyStore | 已發布的 API 金鑰 |
| ScheduleStore | 排程任務 |

> **注意：** 部分內部 Store（執行記憶、Checkpoint 等）目前仍使用 SQLite。啟動時的警告訊息會列出尚未覆蓋的 Store。

### 切回 SQLite

移除或註解掉 `appsettings.json` 中的 `Database` 區段：

```json
{
  // 沒有 "Database" 區段 = SQLite（預設）
}
```

### Azure Cosmos DB for MongoDB

AgentCraftLab 相容 Azure Cosmos DB for MongoDB API。使用 Azure Portal 提供的連線字串：

```json
{
  "Database": {
    "Provider": "mongodb",
    "ConnectionString": "mongodb+srv://user:password@yourcluster.mongocluster.cosmos.azure.com/?tls=true&authMechanism=SCRAM-SHA-256&retrywrites=false&maxIdleTimeMS=120000",
    "DatabaseName": "agentcraftlab"
  }
}
```

### MongoDB Atlas Search（選用）

如果使用 MongoDB Atlas，可以額外替換內建的 SQLite 搜尋引擎為 Atlas Vector Search + Atlas Search：

```csharp
// 在 Program.cs 的 AddMongoDbProvider 之後加入：
builder.Services.AddMongoSearch();
```

啟用後支援：
- **Atlas Vector Search** — 語意相似度搜尋
- **Atlas Search** — 全文搜尋
- **RRF 混合搜尋** — 結合向量與全文

> 自架 MongoDB（無 Atlas）會自動降級為 regex 全文搜尋。
