# Node Types Reference Guide

This document describes all available node types in the AgentCraftLab Workflow Studio, including their purpose, main configuration fields, and applicable scenarios.

## Overview Table

| Node Type | Category | Description |
|-----------|----------|-------------|
| `start` | Meta | Workflow entry point |
| `end` | Meta | Workflow exit point |
| `agent` | Executable / Agent | Local LLM Agent that calls a language model to process tasks |
| `a2a-agent` | Executable / Agent | Remote A2A Agent that calls external services via the Agent-to-Agent protocol |
| `autonomous` | Executable / Agent | ReAct loop autonomous Agent capable of creating sub-agents for collaboration |
| `condition` | Executable / Control Flow | Conditional branching that follows different paths based on expressions |
| `loop` | Executable / Control Flow | Loop that repeats execution until a condition is met or max iterations reached |
| `router` | Executable / Control Flow | Multi-route classifier where an LLM determines which path the input should take |
| `human` | Executable / Control Flow | Pauses the workflow and waits for user input |
| `code` | Executable / Transform | Deterministic text transformation that does not consume LLM tokens |
| `iteration` | Executable / Control Flow | Foreach loop that splits input and processes each item individually |
| `parallel` | Executable / Control Flow | Parallel fan-out/fan-in with multiple branches executing simultaneously |
| `http-request` | Executable / Integration | Deterministic HTTP call to an external API |
| `rag` | Data Node | Attaches a RAG knowledge source (uploaded files or knowledge base) |

---

## Meta Nodes

### start -- Entry Point

**Purpose:** Marks the entry point of the workflow. The user's input message begins propagating from this node.

**Configuration Fields:** No configuration required.

**Applicable Scenarios:** Every workflow must have exactly one start node.

### end -- Exit Point

**Purpose:** Marks the end of the workflow. When the last Agent's output reaches this node, the entire workflow completes.

**Configuration Fields:** No configuration required.

**Applicable Scenarios:** Every workflow must have at least one end node. Workflows with branches may have multiple end nodes.

---

## Agent Nodes

### agent -- Local LLM Agent

**Purpose:** Calls a language model to execute tasks. This is the most commonly used node type in workflows and can have tools, RAG knowledge, and Middleware attached.

**Main Configuration Fields:**

| Field | Description | Default |
|-------|-------------|---------|
| `provider` | LLM provider (openai / azure-openai / ollama / foundry / github-copilot / anthropic / aws-bedrock) | openai |
| `model` | Model name | gpt-4o |
| `instructions` | System instructions defining the Agent's behavior and role | Empty |
| `temperature` | Generation temperature (0~2); higher values produce more creative output | Not set (uses model default) |
| `topP` | Top-P sampling | Not set |
| `maxOutputTokens` | Maximum output token count | Not set |
| `tools` | List of attached built-in tool IDs | Empty |
| `mcpServers` | List of attached MCP Server names | Empty |
| `httpApis` | List of attached HTTP API names | Empty |
| `outputFormat` | Output format (text / json / json_schema) | text |
| `middleware` | Enabled Middleware (GuardRails / PII / RateLimit / Retry / Logging) | Empty |

**Applicable Scenarios:** Text summarization, translation, analysis, code generation, customer service responses, and all other tasks requiring LLM reasoning.

### a2a-agent -- Remote A2A Agent

**Purpose:** Calls a remote Agent service via the Agent-to-Agent (A2A) protocol. Suitable for cross-service and cross-organization Agent collaboration.

**Main Configuration Fields:**

| Field | Description | Default |
|-------|-------------|---------|
| `a2aUrl` | A2A endpoint URL of the remote Agent | Empty (required) |
| `a2aFormat` | Protocol format (auto / google / microsoft) | auto |

**Applicable Scenarios:** Calling external Agents deployed as A2A services, such as specialized enterprise Agents or third-party Agent services. The `auto` mode tries both formats sequentially.

### autonomous -- ReAct Loop Autonomous Agent

**Purpose:** An autonomous Agent that operates in a ReAct (Reasoning + Acting) loop. It can create sub-agents, assign tasks, and collect results on its own, making it suitable for complex multi-step reasoning tasks.

**Main Configuration Fields:** Configured through the basic Agent node fields (provider, model, instructions); execution is handled by ReactExecutor at runtime.

**Applicable Scenarios:** Complex tasks requiring multi-step reasoning and dynamic decision-making. Examples: research report writing (automatically decomposing subtasks), multi-source data aggregation, analytical work requiring iterative verification. Includes 12 built-in meta-tools for sub-agent management.

---

## Control Flow Nodes

### condition -- Conditional Branching

**Purpose:** Directs the flow to different paths based on a condition expression. Supports two output ports: `output_1` (condition is true) and `output_2` (condition is false).

**Main Configuration Fields:**

| Field | Description | Default |
|-------|-------------|---------|
| `condition.kind` | Condition evaluation method | Empty |
| `condition.value` | Condition expression (applied to the previous node's output) | Empty |

**Applicable Scenarios:** Determining the subsequent flow based on the previous Agent's output. Example: if sentiment analysis returns positive, take path A; if negative, take path B.

### loop -- Loop

**Purpose:** Repeatedly executes nodes within the loop until a condition is met or the maximum iteration count is reached.

**Main Configuration Fields:**

| Field | Description | Default |
|-------|-------------|---------|
| `maxIterations` | Maximum number of loop iterations | 5 |
| `conditionExpression` | Termination condition expression | Empty |

**Applicable Scenarios:** Tasks requiring iterative refinement. Examples: polishing an article until quality standards are met, proofreading a translation until error-free.

### router -- Multi-Route Classifier

**Purpose:** Uses an LLM to classify the input content and direct the flow to the corresponding branch path. Supports multiple output ports.

**Main Configuration Fields:**

| Field | Description | Default |
|-------|-------------|---------|
| `instructions` | Routing instructions describing the classification criteria for each path | Empty |

**Applicable Scenarios:** Intelligent routing. Example: a customer service system that routes by issue type (billing / technical / general) to different Agents for handling.

### human -- Human Input

**Purpose:** Pauses workflow execution and waits for the user to provide input before continuing. Supports three interaction modes.

**Main Configuration Fields:**

| Field | Description | Default |
|-------|-------------|---------|
| `prompt` | Prompt message displayed to the user | Empty |
| `inputType` | Input mode: `text` (free text) / `choice` (options) / `approval` (approve/reject) | text |
| `choices` | List of options (comma-separated), used only in choice mode | Empty |
| `timeoutSeconds` | Timeout in seconds for waiting (0 = wait indefinitely) | 0 |

**Applicable Scenarios:** Human-in-the-loop workflows. Examples: AI generates a draft and asks the user to confirm, a process requires manual approval, or the user needs to choose a direction from options.

### iteration -- Foreach Loop

**Purpose:** Splits the input into multiple items and sends each item to child nodes for processing. Similar to a foreach loop in programming languages.

**Main Configuration Fields:**

| Field | Description | Default |
|-------|-------------|---------|
| `split` | Split method: `json-array` (JSON array) / `delimiter` (delimiter-based) | json-array |
| `iterationDelimiter` | Delimiter (only for delimiter mode) | Newline |
| `maxItems` | Maximum number of items to process | 50 |

**Applicable Scenarios:** Batch processing. Examples: generating marketing copy for each product name in a list, analyzing each record in a JSON array individually.

### parallel -- Parallel Execution

**Purpose:** Fan-out/fan-in pattern where multiple branches execute simultaneously in parallel, with results merged after all branches complete.

**Main Configuration Fields:**

| Field | Description | Default |
|-------|-------------|---------|
| `branches` | Branch names (comma-separated) | Branch1,Branch2 |
| `merge` | Result merge strategy: `labeled` (with labels) / `join` (concatenate) / `json` (JSON object) | labeled |

**Applicable Scenarios:** Processing multiple independent subtasks simultaneously. Examples: translating into multiple languages at once, analyzing the same data from multiple perspectives simultaneously.

---

## Transform Nodes

### code -- Deterministic Transform

**Purpose:** Transforms text using deterministic rules without calling an LLM. Zero token consumption, suitable for formatting, data extraction, and other pre/post-processing tasks.

**Main Configuration Fields:**

| Field | Description |
|-------|-------------|
| `kind` | Transform mode (see the nine modes below) |
| `template` | Template string (used in template / script modes) |
| `pattern` | Regular expression (used in regex-extract / regex-replace / json-path) |
| `replacement` | Replacement string (used in regex-replace) |
| `maxLength` | Truncation length (used in trim) |
| `delimiter` | Delimiter (used in split-take) |
| `splitIndex` | Index of the segment to take (used in split-take) |
| `scriptLanguage` | Script language: `javascript` (default) or `csharp` (used in script mode) |

**Nine Transform Modes:**

| Mode | Description |
|------|-------------|
| `template` | Template substitution; `{{input}}` is replaced with the previous node's output |
| `regex-extract` | Extract content matching a regular expression |
| `regex-replace` | Search and replace using a regular expression |
| `json-path` | Extract a value at a specified path from JSON |
| `trim` | Truncate to a specified length |
| `split-take` | Split by delimiter and take the segment at the specified index |
| `upper` | Convert to uppercase |
| `lower` | Convert to lowercase |
| `script` | Execute a sandbox script (JavaScript or C#, requires AgentCraftLab.Script) |

**Script Mode — Dual Language Support:**

| Language | Engine | Usage |
|----------|--------|-------|
| JavaScript | Jint sandbox | Use the `input` variable to read input; set `result` variable for output |
| C# | Roslyn runtime compilation | Parameter `input` is a string; use `return` to return the result. LINQ, JsonSerializer, and Regex are available |

Both languages run in a secure sandbox: File/Network/Process operations are blocked, with timeout and memory limits enforced.

**Script Studio (Full-screen Editor):**

The side panel shows a read-only code preview; clicking it opens the Script Studio full-screen modal:

- **Top** — AI Generate: describe what you need in natural language, and the LLM generates both script code and test data
- **Center** — Monaco Editor (VS Code core): syntax highlighting, bracket matching, auto-indent, minimap
- **Bottom** — Test Run: enter test input, execute instantly, and view results
- **Format button** — auto-format code (Shift+Alt+F)
- Click "Apply" to save the code back to the node settings

**Applicable Scenarios:** Post-processing of Agent output (extracting JSON fields, formatting templates, regex cleanup), data transformation between nodes, complex LINQ queries and data processing (C#).

---

## Integration Nodes

### http-request -- HTTP Call

**Purpose:** Sends a deterministic HTTP request to an external API without going through an LLM. Suitable for calling REST APIs with known formats.

**Main Configuration Fields:**

| Field | Description | Default |
|-------|-------------|---------|
| `httpApiId` | Referenced HTTP API definition ID | Empty (required) |
| `httpArgsTemplate` | JSON parameter template; `{input}` is replaced with the previous node's output | `{}` |

The HTTP API definition (configured at the workflow level) includes: URL, Method (GET/POST/PUT/DELETE), Headers, and BodyTemplate.

**Applicable Scenarios:** Calling third-party REST APIs, webhook notifications, fetching data from external systems.

---

## Data Nodes

Data nodes do not execute logic themselves; instead, they provide additional capabilities to Agent nodes. Connect a data node to an Agent node via an edge to activate it.

### rag -- RAG Knowledge Source

**Purpose:** Attaches a Retrieval-Augmented Generation (RAG) knowledge source to an Agent. When answering, the Agent first searches for relevant document chunks, injects them as context, and then generates a response.

**Main Configuration Fields (RagConfig):**

| Field | Description | Default |
|-------|-------------|---------|
| `dataSource` | Data source type | upload |
| `chunkSize` | Chunk size (in characters) | 1000 |
| `chunkOverlap` | Chunk overlap region | 100 |
| `topK` | Number of top relevant chunks to retrieve | 5 |
| `embeddingModel` | Embedding model | text-embedding-3-small |

You can also connect to an existing knowledge base via `knowledgeBaseIds`.

**Applicable Scenarios:** Enabling Agents to answer questions based on specific documents. Examples: uploading a product manual for customer service Q&A, generating reports based on internal company documents.

### tool -- Tool

**Purpose:** Attaches built-in tools to an Agent. The Agent can decide during reasoning whether to invoke these tools.

**Main Configuration Fields:** Select tools to attach from the tool catalog via the UI.

Tool sources include four layers: Tool Catalog (built-in tools), MCP Servers, A2A Agents, and HTTP APIs.

**Applicable Scenarios:** Giving Agents capabilities such as web search, weather lookup, math calculation, and more. Example: attaching the Web Search tool so the Agent can search for the latest information.

---

## Strategy Auto-Detection

The workflow execution strategy is automatically selected based on the node composition:

1. If the workflow contains any node requiring Imperative (condition / loop / router / human / code / iteration / parallel / http-request / a2a-agent / autonomous) -> uses the **Imperative** strategy
2. If any Agent has multiple outgoing connections -> uses the **Handoff** strategy
3. Otherwise -> uses the **Sequential** strategy

You can also manually specify the strategy in the workflow settings (auto / sequential / concurrent / handoff / imperative).
