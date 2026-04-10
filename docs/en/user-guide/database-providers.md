# Database Providers

AgentCraftLab supports multiple database backends through a **Data Layer Extraction** architecture. All 15 Store interfaces live in a pure abstractions project (`AgentCraftLab.Data`), and each provider is an independent project under `extensions/data/`. The Engine has no direct database dependency.

---

## Architecture

```
extensions/data/
├── AgentCraftLab.Data/              # Pure abstractions (15 interfaces, DTOs, zero dependencies)
├── AgentCraftLab.Data.Sqlite/       # SQLite provider (EF Core)
├── AgentCraftLab.Data.MongoDB/      # MongoDB provider
├── AgentCraftLab.Data.PostgreSQL/   # PostgreSQL provider (EF Core + optional PgVector)
└── AgentCraftLab.Data.SqlServer/    # SQL Server provider (EF Core)
```

**DI composition pattern:**

```csharp
// 1. Register Engine core (no data layer)
builder.Services.AddAgentCraftEngine();

// 2. Register data provider separately
builder.Services.AddSqliteDataProvider("Data/agentcraftlab.db");
// -- or --
builder.Services.AddMongoDbProvider(connectionString, databaseName);
```

The Engine depends only on `AgentCraftLab.Data` interfaces (`IWorkflowStore`, `ICredentialStore`, etc.) and has **no EF Core dependency**. The actual database implementation is composed at the host level.

---

## Supported Providers

| Provider | Project | Status | Use Case |
|----------|---------|--------|----------|
| **SQLite** | `extensions/data/AgentCraftLab.Data.Sqlite` | Default | Local development, single-user deployment |
| **MongoDB** | `extensions/data/AgentCraftLab.Data.MongoDB` | Available | Multi-user, cloud deployment, Azure Cosmos DB |
| **PostgreSQL** | `extensions/data/AgentCraftLab.Data.PostgreSQL` | Available | Cloud-native deployments, optional PgVector search |
| **SQL Server** | `extensions/data/AgentCraftLab.Data.SqlServer` | Available | Enterprise environments, Windows-centric deployments |

---

## 15 Store Interfaces (AgentCraftLab.Data)

All providers must implement these 15 interfaces from the `AgentCraftLab.Data` namespace:

| Interface | Data |
|-----------|------|
| `IWorkflowStore` | Workflow definitions |
| `ICredentialStore` | Encrypted API keys |
| `ISkillStore` | Custom agent skills |
| `ITemplateStore` | Workflow templates |
| `IRequestLogStore` | Execution logs |
| `IScheduleStore` | Scheduled tasks |
| `IDataSourceStore` | Data source metadata |
| `IKnowledgeBaseStore` | Knowledge base metadata |
| `IExecutionMemoryStore` | Autonomous execution memory |
| `ICraftMdStore` | Markdown document store |
| `ICheckpointStore` | ReAct/Flow checkpoint snapshots |
| `IEntityMemoryStore` | Entity fact memory |
| `IContextualMemoryStore` | User pattern memory |
| `IApiKeyStore` | Published API keys |
| `IRefineryStore` | DocRefinery projects and outputs |

---

## SQLite (Default)

No configuration needed. Data is stored locally in `Data/agentcraftlab.db`.

---

## MongoDB

### Prerequisites

- MongoDB Atlas, Azure Cosmos DB for MongoDB, or self-hosted MongoDB 6.0+
- Connection string with read/write access

### Configuration

Add the following to `appsettings.json` (or `appsettings.Development.json`):

```json
{
  "Database": {
    "Provider": "mongodb",
    "ConnectionString": "mongodb+srv://user:password@host/?tls=true&authMechanism=SCRAM-SHA-256",
    "DatabaseName": "agentcraftlab"
  }
}
```

Restart the API server. You should see in the startup log:

```
info: AgentCraftLab  Database Provider: mongodb
```

### What Gets Stored in MongoDB

When MongoDB is enabled, all 15 stores are moved to MongoDB, replacing the default SQLite implementations.

### Switching Back to SQLite

Remove or comment out the `Database` section in `appsettings.json`:

```json
{
  // No "Database" section = SQLite (default)
}
```

### Azure Cosmos DB for MongoDB

AgentCraftLab is compatible with Azure Cosmos DB for MongoDB API. Use the connection string from Azure Portal:

```json
{
  "Database": {
    "Provider": "mongodb",
    "ConnectionString": "mongodb+srv://user:password@yourcluster.mongocluster.cosmos.azure.com/?tls=true&authMechanism=SCRAM-SHA-256&retrywrites=false&maxIdleTimeMS=120000",
    "DatabaseName": "agentcraftlab"
  }
}
```

### MongoDB Atlas Search (Optional)

If you're using MongoDB Atlas, you can also replace the built-in SQLite search engine with Atlas Vector Search + Atlas Search for RAG:

```csharp
// In Program.cs, add after AddMongoDbProvider:
builder.Services.AddMongoSearch();
```

This enables:
- **Atlas Vector Search** for semantic similarity search
- **Atlas Search** for full-text search
- **RRF hybrid** combining both

> Self-hosted MongoDB without Atlas will automatically fall back to regex-based text search.

---

## PostgreSQL

### Prerequisites

- PostgreSQL 14+ (self-hosted or cloud-managed: AWS RDS, Azure Database for PostgreSQL, etc.)
- Connection string with read/write access

### Configuration

Add the following to `appsettings.json` (or `appsettings.Development.json`):

```json
{
  "Database": {
    "Provider": "postgresql",
    "ConnectionString": "Host=localhost;Port=5432;Database=agentcraftlab;Username=user;Password=pass"
  }
}
```

Restart the API server. You should see in the startup log:

```
info: AgentCraftLab  Database Provider: postgresql
```

### PgVector Search (Optional)

If you have the [pgvector](https://github.com/pgvector/pgvector) extension installed, you can add a PgVector data source via **Settings → Data Sources** in the frontend, then bind your knowledge base to it. No additional `appsettings.json` configuration is needed.

---

## SQL Server

### Prerequisites

- SQL Server 2019+ or Azure SQL Database
- Connection string with read/write access

### Configuration

Add the following to `appsettings.json` (or `appsettings.Development.json`):

```json
{
  "Database": {
    "Provider": "sqlserver",
    "ConnectionString": "Server=localhost;Database=agentcraftlab;Trusted_Connection=True;TrustServerCertificate=True"
  }
}
```

Restart the API server. You should see in the startup log:

```
info: AgentCraftLab  Database Provider: sqlserver
```

### What Gets Stored in SQL Server

When SQL Server is enabled, all 15 stores are moved to SQL Server, replacing the default SQLite implementations.

---

## Multi-Provider RAG Search Routing

AgentCraftLab supports routing RAG search queries to different search engines on a per-Knowledge Base basis. This means different KBs can use different search backends simultaneously.

### How It Works

- Users must first create a Data Source connection in **Settings → Data Sources**, then select it when creating a Knowledge Base.
- **New KBs** require a DataSource to be selected (cannot be empty).
- **Existing KBs** with no DataSource (legacy) automatically use the default SQLite engine for backward compatibility.
- `SearchEngineFactory` routes search requests based on the `DataSourceId`, selecting the appropriate search provider for each KB.
- When a query spans multiple KBs, searches execute **in parallel** across different providers, and results are **merged** before being returned.

### Supported Search Providers

| Provider | Engine | Use Case |
|----------|--------|----------|
| **SQLite FTS5** | `SqliteSearchEngine` | Default, local development |
| **PgVector** | `PgVectorSearchEngine` | PostgreSQL deployments with pgvector extension |
| **Qdrant** | `QdrantSearchEngine` | Dedicated vector database, large-scale deployments |
| **MongoDB Atlas** | `MongoSearchEngine` | MongoDB Atlas with Vector Search + Atlas Search |

This architecture allows you to mix and match search backends — for example, using SQLite FTS5 for local KBs while routing cloud KBs to PgVector or Qdrant — all within the same running instance.

---

## Adding a New Provider

See the [Extension Guide](../developer-guide/extending.md#9-adding-a-new-database-provider) for step-by-step instructions on creating a new database provider project.
