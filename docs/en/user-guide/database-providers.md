# Database Providers

AgentCraftLab supports multiple database backends. By default, it uses **SQLite** with zero configuration. You can switch to MongoDB, and more providers (MSSQL, PostgreSQL) are coming soon.

---

## Supported Providers

| Provider | Status | Use Case |
|----------|--------|----------|
| **SQLite** | Default | Local development, single-user deployment |
| **MongoDB** | Available | Multi-user, cloud deployment, Azure Cosmos DB |
| **MSSQL** | Planned | Enterprise environments |
| **PostgreSQL** | Planned | Cloud-native deployments |

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

When MongoDB is enabled, the following 8 stores are moved to MongoDB:

| Store | Data |
|-------|------|
| WorkflowStore | Workflow definitions |
| CredentialStore | Encrypted API keys |
| SkillStore | Custom agent skills |
| TemplateStore | Workflow templates |
| RequestLogStore | Execution logs |
| KnowledgeBaseStore | Knowledge base metadata |
| ApiKeyStore | Published API keys |
| ScheduleStore | Scheduled tasks |

> **Note:** Some internal stores (execution memory, checkpoints, etc.) currently remain in SQLite. A startup warning will list any stores not yet covered by MongoDB.

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
