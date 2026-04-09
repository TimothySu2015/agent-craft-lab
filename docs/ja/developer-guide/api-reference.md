# API エンドポイントリファレンス

AgentCraftLab バックエンド API は `http://localhost:5200` で動作し、AG-UI プロトコルストリーミングエンドポイントと REST CRUD エンドポイントを提供します。

すべての REST エンドポイントは JSON を返却します。エラーレスポンスは統一フォーマット：`{ "code": "ERROR_CODE", "message": "..." }` です。

---

## 1. AG-UI エンドポイント

AG-UI プロトコルエンドポイントは SSE（Server-Sent Events）で実行イベントをストリーミング返却します。

### POST /ag-ui

Workflow を実行します。Credentials はバックエンドの `ICredentialStore` から読み取られます（DPAPI 暗号化）。フロントエンドから API Key を送信する必要はありません。

**Request Body（RunAgentInput）：**
```json
{
  "threadId": "string",
  "runId": "string",
  "messages": [{ "role": "user", "content": "..." }],
  "forwardedProps": {
    "workflowJson": "string (JSON)",
    "fileId": "string (オプション、アップロード後に取得)"
  }
}
```

**Response：** `text/event-stream`、`ExecutionEvent` イベントをストリーミング。

### POST /ag-ui/goal

Autonomous Agent を実行します（ReAct または Flow モード、起動設定で切替）。

**Request Body（RunAgentInput）：**
```json
{
  "threadId": "string",
  "runId": "string",
  "messages": [{ "role": "user", "content": "目標の説明" }],
  "forwardedProps": {
    "provider": "openai",
    "model": "gpt-4o-mini",
    "tools": "web_search,calculator"
  }
}
```

**Response：** `text/event-stream`、実行イベントをストリーミング。

### POST /ag-ui/human-input

Human-in-the-loop 入力を送信します。一時停止中の human ノードに応答します。

**Request Body：**
```json
{
  "threadId": "string",
  "runId": "string",
  "response": "ユーザーの応答テキスト"
}
```

**Response：** `200 { "success": true }` または `404 { "error": "No pending human input for this session" }`

---

## 2. Workflow CRUD

ベースパス：`/api/workflows`

| メソッド | パス | 説明 |
|------|------|------|
| POST | `/api/workflows` | Workflow を作成 |
| GET | `/api/workflows` | 現在のユーザーの全 Workflow を一覧表示 |
| GET | `/api/workflows/{id}` | 単一の Workflow を取得 |
| PUT | `/api/workflows/{id}` | Workflow を更新 |
| DELETE | `/api/workflows/{id}` | Workflow を削除 |
| PATCH | `/api/workflows/{id}/publish` | 公開状態を設定 |

**POST / PUT Request Body：**
```json
{
  "name": "string (必須)",
  "description": "string",
  "type": "string",
  "workflowJson": "string (JSON)"
}
```

**PATCH publish Request Body：**
```json
{
  "isPublished": true,
  "inputModes": ["text", "file"]
}
```

**Response：** `WorkflowDocument` オブジェクト、`id`、`name`、`description`、`type`、`workflowJson`、`createdAt`、`updatedAt` を含みます。

---

## 3. ツール

### GET /api/tools

利用可能なすべてのビルトインツール（Tool Catalog）を一覧表示します。

**Response：**
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

## 4. ディスカバリーとテスト

### POST /api/mcp/discover

MCP Server が提供するツールリストを探索します。

**Request：** `{ "url": "http://localhost:3001/mcp" }`

**Response：** `{ "healthy": true, "tools": [{ "name": "...", "description": "..." }] }`

### POST /api/a2a/discover

A2A Agent の Agent Card を探索します。

**Request：** `{ "url": "http://...", "format": "auto|google|microsoft" }`

**Response：** `{ "healthy": true, "agent": { ... } }`

### POST /api/a2a/test

A2A Agent にテストメッセージを送信します。

**Request：** `{ "url": "http://...", "message": "Hello", "format": "auto" }`

**Response：** `{ "success": true, "response": "..." }`

### POST /api/http-tools/test

HTTP API ツール定義をテストします。

**Request：**
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

**Response：** `{ "success": true, "response": "..." }`

---

## 5. ナレッジベース

ベースパス：`/api/knowledge-bases`

| メソッド | パス | 説明 |
|------|------|------|
| POST | `/api/knowledge-bases` | ナレッジベースを作成 |
| GET | `/api/knowledge-bases` | ユーザーのナレッジベースを一覧表示 |
| GET | `/api/knowledge-bases/{id}` | 単一のナレッジベースを取得 |
| PUT | `/api/knowledge-bases/{id}` | ナレッジベースの名前/説明を更新 |
| DELETE | `/api/knowledge-bases/{id}` | ナレッジベースを削除 |
| GET | `/api/knowledge-bases/{id}/files` | ナレッジベースのファイルを一覧表示 |
| POST | `/api/knowledge-bases/{id}/files` | ファイルをアップロードして Ingest |
| DELETE | `/api/knowledge-bases/{kbId}/files/{fileId}` | 単一ファイルを削除 |

**POST 作成 Request：**
```json
{
  "name": "string (必須)",
  "description": "string",
  "embeddingModel": "text-embedding-3-small",
  "chunkSize": 512,
  "chunkOverlap": 50
}
```

**POST files：** `multipart/form-data`、フィールド名 `file`。ファイル上限 50MB。OpenAI または Azure OpenAI の credentials が設定済みである必要があります。

**POST files Response：** `text/event-stream`、SSE で ingest 進捗イベント `{ "type": "progress|complete|error", "text": "..." }` をストリーミング。

---

## 6. Skills

ベースパス：`/api/skills`

| メソッド | パス | 説明 |
|------|------|------|
| GET | `/api/skills` | ビルトイン + カスタム Skills を一覧表示 |
| POST | `/api/skills` | カスタム Skill を作成 |
| PUT | `/api/skills/{id}` | カスタム Skill を更新 |
| DELETE | `/api/skills/{id}` | カスタム Skill を削除 |

**GET Response：**
```json
{
  "builtin": [{ "id": "...", "name": "...", "description": "...", "instructions": "...", "category": "...", "icon": "...", "tools": [], "isBuiltin": true }],
  "custom": [{ "id": "...", "name": "...", ... }]
}
```

**POST / PUT Request：**
```json
{
  "name": "string (必須)",
  "description": "string",
  "category": "string",
  "icon": "string",
  "instructions": "string",
  "tools": ["tool_id_1", "tool_id_2"]
}
```

---

## 7. テンプレート

ベースパス：`/api/templates`

| メソッド | パス | 説明 |
|------|------|------|
| GET | `/api/templates` | ユーザーのカスタムテンプレートを一覧表示 |
| POST | `/api/templates` | テンプレートを作成 |
| PUT | `/api/templates/{id}` | テンプレートを更新 |
| DELETE | `/api/templates/{id}` | テンプレートを削除 |

**POST / PUT Request：**
```json
{
  "name": "string (必須)",
  "description": "string",
  "category": "string",
  "icon": "string",
  "tags": ["tag1", "tag2"],
  "workflowJson": "string (JSON)"
}
```

---

## 8. API Keys

ベースパス：`/api/keys`

| メソッド | パス | 説明 |
|------|------|------|
| POST | `/api/keys` | API Key を作成 |
| GET | `/api/keys` | ユーザーの API Keys を一覧表示 |
| DELETE | `/api/keys/{id}` | API Key を取り消し |

**POST Request：**
```json
{
  "name": "string (必須)",
  "scopedWorkflowIds": "wf1,wf2 (オプション、カンマ区切り)",
  "expiresAt": "2026-12-31T00:00:00Z (オプション)"
}
```

**POST Response（作成時のみ完全な rawKey を返却）：**
```json
{
  "id": "string",
  "name": "string",
  "keyPrefix": "acl_xxxx...",
  "scopedWorkflowIds": "...",
  "expiresAt": "...",
  "createdAt": "...",
  "rawKey": "acl_xxxxxxxxxxxxxxxx (この回のみ返却)"
}
```

---

## 9. Credentials

ベースパス：`/api/credentials`

| メソッド | パス | 説明 |
|------|------|------|
| POST | `/api/credentials` | Provider Credential を保存（DPAPI 暗号化） |
| GET | `/api/credentials` | Credentials を一覧表示（平文 API Key を含まない） |
| PUT | `/api/credentials/{id}` | Credential を更新 |
| DELETE | `/api/credentials/{id}` | Credential を削除 |
| GET | `/api/credentials/runtime-keys` | 復号された Credentials を取得（localhost のみ） |

**POST / PUT Request：**
```json
{
  "provider": "openai (必須)",
  "name": "string",
  "apiKey": "sk-...",
  "endpoint": "string (Azure OpenAI の場合必要)",
  "model": "string"
}
```

**GET リスト Response（安全版、平文 key を含まない）：**
```json
[{ "id": "...", "provider": "openai", "name": "...", "hasApiKey": true, "endpoint": "", "model": "", "createdAt": "...", "updatedAt": "..." }]
```

**GET runtime-keys：** localhost からのアクセスのみ許可、レート制限あり（1 分あたり 10 回）。復号された credentials を Runtime 用に返却します。

---

## 10. アップロード

### POST /api/upload

一時ファイルアップロード。AG-UI 実行時にファイルを添付するために使用します。

**Request：** `multipart/form-data`、フィールド名 `file`。上限 32MB。

**Response：**
```json
{
  "fileId": "upload-xxxxxxxx",
  "fileName": "document.pdf",
  "size": 12345
}
```

一時ファイルは 1 時間後に自動クリーンアップされます。フロントエンドは取得した `fileId` を `forwardedProps.fileId` に設定し、AG-UI 実行時に自動的に注入されます。

---

## 11. アナリティクス

### GET /api/analytics/summary

使用量サマリーを取得します。

**Query パラメーター：** `from`（DateTime、デフォルトは過去 24 時間）、`userId`（オプション）。

**Response：** サマリー統計オブジェクト。

### GET /api/analytics/logs

リクエストログを照会します。

**Query パラメーター：** `from`、`to`（DateTime）、`protocol`（string）、`limit`（int、デフォルト 100）。

**Response：** ログレコードの配列。

---

## 12. AI Build

### POST /api/flow-builder

自然言語の説明を Workflow JSON に変換します。SSE で生成結果をストリーミング返却します。

**Request Body：**
```json
{
  "message": "string (必須、自然言語の説明)",
  "provider": "openai",
  "model": "gpt-4o",
  "apiKey": "string (オプション、優先的に CredentialStore から読み取り)",
  "endpoint": "string (オプション)",
  "currentPayload": "string (現在の workflow JSON、インクリメンタル修正用)",
  "history": [{ "role": "user|assistant", "content": "..." }],
  "mode": "legacy (オプション、デフォルトは Flow Planner 強化版を使用)"
}
```

**Response：** `text/event-stream`、JSON 文字列フラグメントをストリーミング。最後のイベントはメタデータです：
```json
{ "type": "__metadata", "durationMs": 1234, "estimatedTokens": 500, "model": "gpt-4o", "estimatedCost": "$0.01" }
```
終端に `[DONE]` を送信します。

---

## 14. Script Generator

### POST /api/script-generator

LLM がサンドボックス準拠のスクリプトとテストデータを生成します（JavaScript または C#）。

**Request Body：**
```json
{
  "prompt": "string (必須、スクリプト要件の説明)",
  "provider": "openai",
  "model": "gpt-4o-mini",
  "apiKey": "string (オプション、バックエンド ICredentialStore から優先読み取り)",
  "endpoint": "string (オプション)",
  "language": "javascript | csharp (オプション、デフォルト javascript)"
}
```

**Response：**
```json
{
  "code": "const data = JSON.parse(input); ...",
  "testInput": "[{\"Name\":\"Alice\",\"Score\":95}]"
}
```

`language` パラメータに基づいてシステムプロンプトが自動的に切り替わります（JS ルール vs C# ルール）。`testInput` は LLM が自動生成したテストデータサンプルです。

### POST /api/script-test

サンドボックス内でスクリプトをテストします（JavaScript または C#）。

**Request Body：**
```json
{
  "code": "string (必須、スクリプトコード)",
  "input": "string (シミュレートする input 変数の値)",
  "language": "javascript | csharp (オプション、デフォルト javascript)"
}
```

**Response：**
```json
{
  "success": true,
  "output": "実行結果",
  "error": null,
  "consoleOutput": "console.log 出力",
  "elapsedMs": 12.5
}
```

---

## 15. 診断

### GET /api/traces/latest

最新の実行 Trace を取得します。

**Response：** `{ "runId": "...", "path": "...", "entries": ["jsonl line 1", "..."] }`

### GET /api/traces/{runId}

指定した runId の実行 Trace を取得します。

**Response：** 上記と同じです。Trace ファイルは `Data/traces/{runId}.jsonl` に保存されます。

### GET /info

API サーバー情報。

**Response：** `{ "name": "AgentCraftLab API", "protocol": "AG-UI", "version": "1.0.0", "mode": "react|flow", "endpoints": [...] }`
