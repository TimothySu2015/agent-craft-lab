# Workflow Execution and Service Publishing

This document covers AgentCraftLab's workflow execution methods, service publishing process, and related settings.

---

## Part 1: Executing Workflows

### 1.1 Execute Chat

Switch to the **Execute** tab in Workflow Studio to run the current workflow. After entering a message and pressing send, the system streams the execution process and results in real time via the AG-UI Protocol using SSE.

The execution flow is as follows:

1. The user enters a message in Execute Chat
2. The backend `WorkflowExecutionService.ExecuteAsync()` receives the request
3. The request passes through Hook (OnInput), preprocessing (WorkflowPreprocessor), and strategy selection before execution
4. The execution process streams to the frontend as `IAsyncEnumerable<ExecutionEvent>`
5. The frontend displays each Agent's response text, tool call results, etc. in real time

### 1.2 Chat Attachment Upload

Execute Chat supports file attachment functionality:

1. Click the attachment button next to the chat input box
2. Select a file (supports PDF, DOCX, images, etc., up to 32MB)
3. The file is uploaded to `POST /api/upload`, returning a `fileId` (temporarily stored for 1 hour)
4. When the message is sent, the `fileId` is transmitted to the backend along with the request
5. The backend determines how to process the file based on its type: RAG ingest, ZIP extraction, or as multimodal DataContent passed to the Agent

Since CopilotKit natively supports only image uploads and its implementation is incomplete, the system uses an independent upload pipeline to bypass this limitation.

### 1.3 Five Execution Strategies

The system automatically selects the execution strategy based on the workflow's node composition; no manual configuration is required.

| Strategy | Description | Auto-Detection Condition |
|----------|-------------|-------------------------|
| **Single** | Single Agent direct execution | Workflow contains only one executable node |
| **Sequential** | Executes multiple Agents in sequence | Multiple Agents, each with only one outgoing connection |
| **Concurrent** | Multiple Agents execute in parallel simultaneously | Node groups explicitly marked for parallel execution |
| **Handoff** | Agents hand off control to one another | Any Agent has multiple outgoing connections |
| **Imperative** | Imperative flow control | Contains control flow nodes such as condition, loop, human |

Auto-detection logic: `NodeTypeRegistry.HasAnyRequiringImperative()` checks whether there are node types requiring Imperative (such as condition, loop, human, code, iteration, parallel, etc.). If so, Imperative is used; if any Agent has multiple outgoing connections, Handoff is used; otherwise, Single or Sequential is selected based on the number of nodes.

### 1.4 Human Input Node

The Human node pauses execution and waits for user input before continuing. Three interaction modes are supported:

- **text** -- Free text input where the user can enter any response
- **choice** -- Multiple choice where the user selects from predefined options
- **approval** -- Approve/reject for approval workflows

When execution reaches a Human node, the system emits a `WaitingForInput` event and the frontend displays the corresponding input interface. After the user submits, a `UserInputReceived` event is emitted and the flow continues.

### 1.5 Execution Events

During execution, the system reports progress via an Event Stream. Main event types:

| Event | Description |
|-------|-------------|
| `AgentStarted` | Agent started executing (includes Agent name) |
| `TextChunk` | Streaming text chunk (real-time output) |
| `AgentCompleted` | Agent finished executing (includes full response) |
| `ToolCall` | Tool invocation (includes tool name and parameters) |
| `ToolResult` | Tool return result |
| `WaitingForInput` | Waiting for user input (Human node paused) |
| `UserInputReceived` | User input received |
| `RagProcessing` / `RagReady` | RAG pipeline processing status |
| `HookExecuted` / `HookBlocked` | Hook execution result |
| `WorkflowCompleted` | Entire workflow execution completed |
| `Error` | Error event |

These events are converted to AG-UI Protocol format by `AgUiEventConverter` and pushed to the frontend via SSE.

### 1.6 Autonomous Mode

The Autonomous node provides AI self-directed execution capabilities in two modes:

**ReAct Mode (Fully Autonomous):** ReactExecutor operates in a Reasoning-Acting loop. The AI independently decides the next action, calls tools, observes results, and repeats until the task is complete. Supports 12 meta-tools (create/ask/spawn sub-agents, shared state, request user confirmation, etc.). Uses a dual-model architecture -- TaskPlanner uses a strong model for planning, ReactExecutor uses a weaker model for execution.

**Flow Mode (Structured Execution):** The LLM first creates an execution plan (FlowPlan), then executes it step by step through 7 node types (agent / code / condition / iteration / parallel / loop / http-request). After execution completes, the result can be crystallized into a reusable Workflow via Crystallize.

Three-layer funnel relationship: Engine Workflow (human-designed) > Flow (AI-planned + structured execution) > ReAct (fully autonomous).

---

## Part 2: Publishing Services

### 2.1 Publish Workflow

Go to the `/published-services` page to manage workflow publishing status.

**Enable/Disable Publishing:** Each workflow can be independently enabled or disabled for publishing. Once enabled, the workflow can be accessed via API calls or other protocols.

**Input Modes:** When publishing, you can configure the accepted input formats:

- **text/plain** -- Plain text input (default)
- **application/pdf**, **application/vnd.openxmlformats-officedocument.wordprocessingml.document**, etc. -- File input
- **application/json** -- Structured JSON input

Input Modes determine which data formats external callers can send to the workflow.

### 2.2 API Keys Management

Go to the `/api-keys` page to manage API keys.

**Creating an API Key:**

1. Click the create button
2. Enter a name/description (for easy identification)
3. Optionally select a Scope to restrict this key to accessing specific workflows only
4. The system generates the key, displayed only once -- please save it securely

**Scope Restriction:** When creating a key, you can specify which published workflows it is allowed to access, implementing the principle of least privilege. If no Scope is specified, the key can access all published workflows.

**Revoking a Key:** Keys can be revoked at any time from the API Keys list. Revocation takes effect immediately, and the key can no longer be used for any requests.

### 2.3 Service Tester

Go to the `/service-tester` page to test published services or external endpoints. It features a dual-panel design (settings panel + conversation panel) and supports 5 protocols:

| Protocol | Description |
|----------|-------------|
| **AG-UI** | AgentCraftLab's native Agent-UI streaming protocol |
| **A2A** | Google Agent-to-Agent protocol for cross-service Agent communication |
| **MCP** | Model Context Protocol for tool integration testing |
| **HTTP** | Standard REST API calls |
| **Teams** | Microsoft Teams Bot protocol |

Select a published workflow or enter an external endpoint URL to conduct interactive testing in the Chat panel.

### 2.4 Request Logs

Go to the `/request-logs` page to view execution records and analytics. You can inspect the request content, response results, and execution time of each API call for debugging and performance analysis.

---

## Part 3: Settings

### 3.1 Settings Page

Go to the `/settings` page for personal settings, which includes the following sections:

**Profile:** Personal profile settings.

**Language:** Switch the interface language. Currently supports English (en) and Traditional Chinese (zh-TW).

**Default Model:** Set the default LLM model for Agents. Supported providers include OpenAI, Azure OpenAI, Ollama, Azure Foundry, GitHub Copilot, Anthropic, and AWS Bedrock.

### 3.2 Credentials Management

Manage API keys for various AI services in the Credentials section of the Settings page:

1. Select a Provider (e.g., OpenAI, Azure OpenAI, etc.)
2. Enter the corresponding API Key, Endpoint, and other authentication information
3. After saving, the credentials are encrypted with DPAPI and stored in the backend `ICredentialStore`
4. The frontend does not store plaintext keys; it only records a "saved" status

During workflow execution, the backend reads decrypted credentials from `ICredentialStore` via `ResolveCredentialsAsync()`. The frontend no longer transmits API Keys.

### 3.3 Budget Settings

Set token usage budget limits to avoid unexpectedly high costs. Budget limits can be configured per model or globally.

