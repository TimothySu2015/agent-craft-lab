# 資料庫 Provider

AgentCraftLab 支援多種資料庫後端。預設使用 **SQLite**，零設定即可運作。可依需求切換為 MongoDB、PostgreSQL 或 SQL Server。

---

## 架構概覽

資料層採用三層分離設計，所有專案位於 `extensions/data/` 目錄：

| 專案 | 定位 | 依賴 |
|------|------|------|
| `AgentCraftLab.Data` | 純抽象層（Store 介面 + Document 模型） | 零依賴 |
| `AgentCraftLab.Data.Sqlite` | SQLite 實作（EF Core + DPAPI 加密） | `AgentCraftLab.Data` |
| `AgentCraftLab.Data.MongoDB` | MongoDB 實作 | `AgentCraftLab.Data` |
| `AgentCraftLab.Data.PostgreSQL` | PostgreSQL 實作（EF Core + Npgsql） | `AgentCraftLab.Data` |
| `AgentCraftLab.Data.SqlServer` | SQL Server 實作（EF Core） | `AgentCraftLab.Data` |

```
extensions/data/
├─ AgentCraftLab.Data/                  ← 純抽象（命名空間：AgentCraftLab.Data）
├─ AgentCraftLab.Data.Sqlite/           ← SQLite 實作
├─ AgentCraftLab.Data.MongoDB/          ← MongoDB 實作
├─ AgentCraftLab.Data.PostgreSQL/       ← PostgreSQL 實作
└─ AgentCraftLab.Data.SqlServer/        ← SQL Server 實作
```

**設計原則：** Engine 核心（`AgentCraftLab.Engine`）僅參考 `AgentCraftLab.Data` 的介面，不依賴 EF Core 或任何特定資料庫。DI 註冊時分開呼叫：

```csharp
builder.Services.AddAgentCraftEngine();      // Engine 核心（不含 DB 依賴）
builder.Services.AddSqliteDataProvider();    // 或 AddMongoDbProvider() / AddPostgreSqlDataProvider() / AddSqlServerDataProvider()
```

---

## 支援的 Provider

| Provider | 狀態 | 適用場景 |
|----------|------|---------|
| **SQLite** | 預設 | 本機開發、單人部署 |
| **MongoDB** | 可用 | 多人協作、雲端部署、Azure Cosmos DB |
| **PostgreSQL** | 可用 | 雲原生部署、向量搜尋（PgVector） |
| **SQL Server** | 可用 | 企業環境、Azure SQL |

---

## 15 個 Store 介面

所有介面定義於 `AgentCraftLab.Data` 命名空間：

| Store 介面 | 資料內容 | SQLite | MongoDB | PostgreSQL | SQL Server |
|------------|---------|--------|---------|------------|------------|
| `IWorkflowStore` | Workflow 定義 | V | V | V | V |
| `ICredentialStore` | 加密的 API 金鑰 | V | V | V | V |
| `ISkillStore` | 自訂 Agent 技能 | V | V | V | V |
| `ITemplateStore` | Workflow 範本 | V | V | V | V |
| `IRequestLogStore` | 執行記錄 | V | V | V | V |
| `IKnowledgeBaseStore` | 知識庫元資料 | V | V | V | V |
| `IApiKeyStore` | 已發布的 API 金鑰 | V | V | V | V |
| `IScheduleStore` | 排程任務 | V | V | V | V |
| `IDataSourceStore` | 資料來源設定 | V | V | V | V |
| `IExecutionMemoryStore` | 執行記憶（ReAct 經驗） | V | V | V | V |
| `IEntityMemoryStore` | 實體記憶（事實） | V | V | V | V |
| `IContextualMemoryStore` | 情境記憶（使用者模式） | V | V | V | V |
| `ICraftMdStore` | craft.md 內容 | V | V | V | V |
| `ICheckpointStore` | Checkpoint 快照 | V | V | V | V |
| `IRefineryStore` | DocRefinery 專案/檔案/輸出 | V | V | V | V |

> **四個 Provider 皆實作完整的 15 個 Store 介面。** 切換 Provider 時不需要混用 SQLite。

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

---

## PostgreSQL

### 前置條件

- PostgreSQL 14+ 或相容服務（Azure Database for PostgreSQL、AWS RDS、Supabase 等）
- 具有讀寫權限的連線字串
- 若需向量搜尋，需安裝 [pgvector](https://github.com/pgvector/pgvector) 擴充套件

### 設定方式

在 `appsettings.json`（或 `appsettings.Development.json`）中加入：

```json
{
  "Database": {
    "Provider": "postgresql",
    "ConnectionString": "Host=localhost;Port=5432;Database=agentcraftlab;Username=user;Password=pass"
  }
}
```

重啟 API 伺服器，啟動時會看到：

```
info: AgentCraftLab  Database Provider: postgresql
```

### PgVector 搜尋（選用）

若 PostgreSQL 已安裝 pgvector 擴充套件，可將 RAG 搜尋引擎從預設的 SQLite FTS5 切換為 PgVector，享受向量搜尋能力：

```json
{
  "Database": {
    "Provider": "postgresql",
    "ConnectionString": "Host=localhost;Port=5432;Database=agentcraftlab;Username=user;Password=pass",
    "UsePgVectorSearch": true
  }
}
```

啟用後，PgVector 搜尋引擎會搶先註冊，取代 Engine 預設的 SQLite 搜尋。支援：

- **向量搜尋** — 語意相似度（cosine similarity）
- **全文搜尋** — PostgreSQL tsvector
- **RRF 混合搜尋** — 結合向量與全文（k=60）

---

## SQL Server

### 前置條件

- SQL Server 2019+ 或相容服務（Azure SQL Database、Azure SQL Managed Instance 等）
- 具有讀寫權限的連線字串

### 設定方式

在 `appsettings.json`（或 `appsettings.Development.json`）中加入：

```json
{
  "Database": {
    "Provider": "sqlserver",
    "ConnectionString": "Server=localhost;Database=agentcraftlab;Trusted_Connection=True;TrustServerCertificate=True"
  }
}
```

重啟 API 伺服器，啟動時會看到：

```
info: AgentCraftLab  Database Provider: sqlserver
```

### Azure SQL Database

使用 Azure SQL 時，連線字串範例：

```json
{
  "Database": {
    "Provider": "sqlserver",
    "ConnectionString": "Server=tcp:yourserver.database.windows.net,1433;Initial Catalog=agentcraftlab;Persist Security Info=False;User ID=user;Password=pass;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30"
  }
}
```

---

## RAG 多 Provider 搜尋路由

AgentCraftLab 的 RAG 搜尋引擎支援多 Provider 路由。不同的知識庫（KB）可以綁定不同的搜尋引擎，透過 DataSource 設定自動分派。

### 運作原理

`SearchEngineFactory` 根據知識庫綁定的 `DataSourceId` 解析對應的搜尋引擎：

- **DataSourceId 為空** → 使用全域預設搜尋引擎（SQLite FTS5，或啟用 `UsePgVectorSearch` 時為 PgVector）
- **DataSourceId 有值** → 從 `IDataSourceStore` 查詢 DataSource 設定，根據 `Provider` 欄位建立對應引擎

### 支援的搜尋 Provider

| 搜尋 Provider | 說明 | 適用場景 |
|--------------|------|---------|
| **SQLite FTS5** | 內建預設，全文搜尋 + trigram BM25 | 本機開發、單人部署 |
| **PgVector** | PostgreSQL pgvector 擴充，向量 + 全文 + RRF 混合 | 雲原生部署、語意搜尋 |
| **Qdrant** | 獨立向量資料庫，高效能向量搜尋 | 大規模向量搜尋、獨立部署 |
| **MongoDB Atlas** | Atlas Vector Search + Atlas Search | 已使用 MongoDB 的環境 |

### 多知識庫平行搜尋

當 Workflow 掛載多個知識庫時，RAG 引擎會：

1. 依據各知識庫的 DataSource 綁定，透過 `SearchEngineFactory` 解析對應的搜尋引擎
2. 對所有知識庫**平行發送搜尋請求**
3. 合併各引擎回傳的結果，統一注入 Agent 的 system message

這意味著同一個 Workflow 中，KB-A 可以使用 SQLite FTS5，KB-B 可以使用 PgVector，KB-C 可以使用 Qdrant，三者平行搜尋後結果合併。

---

## 新增自訂 Provider

如需支援其他資料庫，請參考 [擴充指南 - 新增資料庫 Provider](../developer-guide/extending.md#10-新增資料庫-provider)。

核心步驟：
1. 在 `extensions/data/` 下建立新專案
2. 實作 `AgentCraftLab.Data` 命名空間的 15 個 Store 介面
3. 提供 `ServiceCollectionExtensions`（`AddXxxDataProvider()` 擴展方法）
4. 在 `Program.cs` 的 switch case 加入新 Provider
