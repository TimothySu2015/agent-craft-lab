# API Endpoint Reference

The AgentCraftLab backend API runs at `http://localhost:5200`, providing AG-UI protocol streaming endpoints and REST CRUD endpoints.

All REST endpoints return JSON. Error responses use a unified format: `{ "code": "ERROR_CODE", "message": "..." }`.

---

## 1. AG-UI Endpoints

AG-UI protocol endpoints return execution events as SSE (Server-Sent Events) streams.

### POST /ag-ui

Execute a Workflow. Credentials are read from the backend `ICredentialStore` (DPAPI encrypted); the frontend does not need to send API Keys.

**Request Body (RunAgentInput):**
```json
{
  "threadId": "string",
  "runId": "string",
  "messages": [{ "role": "user", "content": "..." }],
  "forwardedProps": {
    "workflowJson": "string (JSON)",
    "fileId": "string (optional, obtained after upload)"
  }
}
```

**Response:** `text/event-stream`, streaming `ExecutionEvent` events.

### POST /ag-ui/goal

Execute an Autonomous Agent (ReAct or Flow mode, switched based on startup configuration).

**Request Body (RunAgentInput):**
```json
{
  "threadId": "string",
  "runId": "string",
  "messages": [{ "role": "user", "content": "Goal description" }],
  "forwardedProps": {
    "provider": "openai",
    "model": "gpt-4o-mini",
    "tools": "web_search,calculator"
  }
}
```

**Response:** `text/event-stream`, streaming execution events.

### POST /ag-ui/human-input

Submit Human-in-the-loop input, responding to a paused human node.

**Request Body:**
```json
{
  "threadId": "string",
  "runId": "string",
  "response": "User's reply text"
}
```

**Response:** `200 { "success": true }` or `404 { "error": "No pending human input for this session" }`

---

## 2. Workflow CRUD

Base path: `/api/workflows`

| Method | Path | Description |
|------|------|------|
| POST | `/api/workflows` | Create a Workflow |
| GET | `/api/workflows` | List all Workflows for the current user |
| GET | `/api/workflows/{id}` | Get a single Workflow |
| PUT | `/api/workflows/{id}` | Update a Workflow |
| DELETE | `/api/workflows/{id}` | Delete a Workflow |
| PATCH | `/api/workflows/{id}/publish` | Set publish status |

**POST / PUT Request Body:**
```json
{
  "name": "string (required)",
  "description": "string",
  "type": "string",
  "workflowJson": "string (JSON)"
}
```

**PATCH publish Request Body:**
```json
{
  "isPublished": true,
  "inputModes": ["text", "file"]
}
```

**Response:** `WorkflowDocument` object containing `id`, `name`, `description`, `type`, `workflowJson`, `createdAt`, `updatedAt`.

---

## 3. Tools

### GET /api/tools

List all available built-in tools (Tool Catalog).

**Response:**
```json
[
  {
    "id": "web_search",
    "name": "Web Search",
    "description": "...",
    "category": "Search",
    "icon": "search"
  }
]
```

---

## 4. Discovery and Testing

### POST /api/mcp/discover

Discover the tool list provided by an MCP Server.

**Request:** `{ "url": "http://localhost:3001/mcp" }`

**Response:** `{ "healthy": true, "tools": [{ "name": "...", "description": "..." }] }`

### POST /api/a2a/discover

Discover an A2A Agent's Agent Card.

**Request:** `{ "url": "http://...", "format": "auto|google|microsoft" }`

**Response:** `{ "healthy": true, "agent": { ... } }`

### POST /api/a2a/test

Send a test message to an A2A Agent.

**Request:** `{ "url": "http://...", "message": "Hello", "format": "auto" }`

**Response:** `{ "success": true, "response": "..." }`

### POST /api/http-tools/test

Test an HTTP API tool definition.

**Request:**
```json
{
  "name": "test",
  "url": "https://api.example.com/data",
  "method": "GET",
  "headers": "Authorization: Bearer xxx",
  "body": "",
  "input": ""
}
```

**Response:** `{ "success": true, "response": "..." }`

---

## 5. Knowledge Bases

Base path: `/api/knowledge-bases`

| Method | Path | Description |
|------|------|------|
| POST | `/api/knowledge-bases` | Create a knowledge base |
| GET | `/api/knowledge-bases` | List the user's knowledge bases |
| GET | `/api/knowledge-bases/{id}` | Get a single knowledge base |
| PUT | `/api/knowledge-bases/{id}` | Update knowledge base name/description |
| DELETE | `/api/knowledge-bases/{id}` | Delete a knowledge base |
| GET | `/api/knowledge-bases/{id}/files` | List knowledge base files |
| POST | `/api/knowledge-bases/{id}/files` | Upload a file and ingest |
| DELETE | `/api/knowledge-bases/{kbId}/files/{fileId}` | Delete a single file |

**POST Create Request:**
```json
{
  "name": "string (required)",
  "description": "string",
  "embeddingModel": "text-embedding-3-small",
  "chunkSize": 512,
  "chunkOverlap": 50
}
```

**POST files:** `multipart/form-data`, field name `file`. File size limit 50MB. Requires OpenAI or Azure OpenAI credentials to be configured.

**POST files Response:** `text/event-stream`, SSE streaming ingest progress events `{ "type": "progress|complete|error", "text": "..." }`.

---

## 6. Skills

Base path: `/api/skills`

| Method | Path | Description |
|------|------|------|
| GET | `/api/skills` | List built-in + custom Skills |
| POST | `/api/skills` | Create a custom Skill |
| PUT | `/api/skills/{id}` | Update a custom Skill |
| DELETE | `/api/skills/{id}` | Delete a custom Skill |

**GET Response:**
```json
{
  "builtin": [{ "id": "...", "name": "...", "description": "...", "instructions": "...", "category": "...", "icon": "...", "tools": [], "isBuiltin": true }],
  "custom": [{ "id": "...", "name": "...", ... }]
}
```

**POST / PUT Request:**
```json
{
  "name": "string (required)",
  "description": "string",
  "category": "string",
  "icon": "string",
  "instructions": "string",
  "tools": ["tool_id_1", "tool_id_2"]
}
```

---

## 7. Templates

Base path: `/api/templates`

| Method | Path | Description |
|------|------|------|
| GET | `/api/templates` | List the user's custom templates |
| POST | `/api/templates` | Create a template |
| PUT | `/api/templates/{id}` | Update a template |
| DELETE | `/api/templates/{id}` | Delete a template |

**POST / PUT Request:**
```json
{
  "name": "string (required)",
  "description": "string",
  "category": "string",
  "icon": "string",
  "tags": ["tag1", "tag2"],
  "workflowJson": "string (JSON)"
}
```

---

## 8. API Keys

Base path: `/api/keys`

| Method | Path | Description |
|------|------|------|
| POST | `/api/keys` | Create an API Key |
| GET | `/api/keys` | List the user's API Keys |
| DELETE | `/api/keys/{id}` | Revoke an API Key |

**POST Request:**
```json
{
  "name": "string (required)",
  "scopedWorkflowIds": "wf1,wf2 (optional, comma-separated)",
  "expiresAt": "2026-12-31T00:00:00Z (optional)"
}
```

**POST Response (full rawKey returned only at creation time):**
```json
{
  "id": "string",
  "name": "string",
  "keyPrefix": "acl_xxxx...",
  "scopedWorkflowIds": "...",
  "expiresAt": "...",
  "createdAt": "...",
  "rawKey": "acl_xxxxxxxxxxxxxxxx (returned only this once)"
}
```

---

## 9. Credentials

Base path: `/api/credentials`

| Method | Path | Description |
|------|------|------|
| POST | `/api/credentials` | Save a Provider Credential (DPAPI encrypted) |
| GET | `/api/credentials` | List Credentials (excludes plaintext API Key) |
| PUT | `/api/credentials/{id}` | Update a Credential |
| DELETE | `/api/credentials/{id}` | Delete a Credential |
| GET | `/api/credentials/runtime-keys` | Get decrypted Credentials (localhost only) |

**POST / PUT Request:**
```json
{
  "provider": "openai (required)",
  "name": "string",
  "apiKey": "sk-...",
  "endpoint": "string (required for Azure OpenAI)",
  "model": "string"
}
```

**GET List Response (safe version, excludes plaintext key):**
```json
[{ "id": "...", "provider": "openai", "name": "...", "hasApiKey": true, "endpoint": "", "model": "", "createdAt": "...", "updatedAt": "..." }]
```

**GET runtime-keys:** Accessible only from localhost, with rate limiting (10 requests per minute). Returns decrypted credentials for Runtime use.

---

## 10. Upload

### POST /api/upload

Temporary file upload for attaching files during AG-UI execution.

**Request:** `multipart/form-data`, field name `file`. Limit 32MB.

**Response:**
```json
{
  "fileId": "upload-xxxxxxxx",
  "fileName": "document.pdf",
  "size": 12345
}
```

Temporary files are automatically cleaned up after 1 hour. After obtaining the `fileId`, the frontend places it in `forwardedProps.fileId`, which is automatically injected during AG-UI execution.

---

## 11. Analytics

### GET /api/analytics/summary

Get a usage summary.

**Query Parameters:** `from` (DateTime, defaults to last 24 hours), `userId` (optional).

**Response:** Summary statistics object.

### GET /api/analytics/logs

Query request logs.

**Query Parameters:** `from`, `to` (DateTime), `protocol` (string), `limit` (int, default 100).

**Response:** Array of log records.

---

## 12. Schedules (Commercial Mode)

Requires commercial mode to be enabled (set `ConnectionStrings:MongoDB`).

Base path: `/api/schedules`

| Method | Path | Description |
|------|------|------|
| GET | `/api/schedules` | List the user's schedules |
| GET | `/api/schedules/{id}` | Get a single schedule |
| POST | `/api/schedules` | Create or update a schedule |
| PATCH | `/api/schedules/{id}/toggle` | Enable/disable a schedule |
| DELETE | `/api/schedules/{id}` | Delete a schedule |
| GET | `/api/schedules/{id}/logs` | Query execution logs |

**POST Request:**
```json
{
  "id": "string (optional, updates if provided)",
  "workflowId": "string (required, must be published)",
  "cronExpression": "0 9 * * * (required)",
  "timeZone": "UTC",
  "enabled": true,
  "defaultInput": "string"
}
```

**GET logs Query Parameters:** `limit` (int, default 20).

---

## 13. AI Build

### POST /api/flow-builder

Convert natural language description to Workflow JSON. Returns generated results as an SSE stream.

**Request Body:**
```json
{
  "message": "string (required, natural language description)",
  "provider": "openai",
  "model": "gpt-4o",
  "apiKey": "string (optional, preferentially read from CredentialStore)",
  "endpoint": "string (optional)",
  "currentPayload": "string (current workflow JSON, for incremental modifications)",
  "history": [{ "role": "user|assistant", "content": "..." }],
  "mode": "legacy (optional, defaults to enhanced Flow Planner version)"
}
```

**Response:** `text/event-stream`, streaming JSON string fragments. The last event is metadata:
```json
{ "type": "__metadata", "durationMs": 1234, "estimatedTokens": 500, "model": "gpt-4o", "estimatedCost": "$0.01" }
```
Ends with `[DONE]`.

---

## 14. Script Generator

### POST /api/script-generator

LLM generates sandbox-compliant scripts (JavaScript or C#), along with test data.

**Request Body:**
```json
{
  "prompt": "string (required, script requirement description)",
  "provider": "openai",
  "model": "gpt-4o-mini",
  "apiKey": "string (optional, reads from backend ICredentialStore first)",
  "endpoint": "string (optional)",
  "language": "javascript | csharp (optional, defaults to javascript)"
}
```

**Response:**
```json
{
  "code": "const data = JSON.parse(input); ...",
  "testInput": "[{\"Name\":\"Alice\",\"Score\":95}]"
}
```

Automatically switches the system prompt based on the `language` parameter (JS rules vs C# rules). `testInput` is an LLM-generated sample test data.

### POST /api/script-test

Test a script in the sandbox (JavaScript or C#).

**Request Body:**
```json
{
  "code": "string (required, script code)",
  "input": "string (simulated input variable value)",
  "language": "javascript | csharp (optional, defaults to javascript)"
}
```

**Response:**
```json
{
  "success": true,
  "output": "Execution result",
  "error": null,
  "consoleOutput": "console.log output",
  "elapsedMs": 12.5
}
```

---

## 15. Diagnostics

### GET /api/traces/latest

Get the latest execution Trace.

**Response:** `{ "runId": "...", "path": "...", "entries": ["jsonl line 1", "..."] }`

### GET /api/traces/{runId}

Get the execution Trace for a specific runId.

**Response:** Same as above. Trace files are stored at `Data/traces/{runId}.jsonl`.

### GET /info

API server information.

**Response:** `{ "name": "AgentCraftLab API", "protocol": "AG-UI", "version": "1.0.0", "mode": "react|flow", "endpoints": [...] }`
