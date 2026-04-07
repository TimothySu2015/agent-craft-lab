# AgentCraftLab System Architecture Guide

This document is intended for developers who want to understand or extend AgentCraftLab, covering core architecture, execution flow, and extensibility mechanisms.

---

## 1. Solution Overview -- Open Core Architecture

AgentCraftLab adopts an Open Core model, where the core engine is open source and commercial features are independently packaged.

| Project | Purpose |
|------|------|
| `AgentCraftLab.Api` | Pure backend API (AG-UI + REST, Minimal API endpoints, port 5200) |
| `AgentCraftLab.Web` | React frontend (React Flow + CopilotKit + shadcn/ui, port 5173) |
| `AgentCraftLab.Search` | Standalone search engine library (FTS5 + vector + RRF hybrid search) |
| `AgentCraftLab.Engine` | Open source core engine (SQLite + single-user mode, strategies + nodes + tools + Middleware + Hooks) |
| `AgentCraftLab.Autonomous` | ReAct loop + Sub-agent collaboration + 12 meta-tools + safety mechanisms |
| `AgentCraftLab.Autonomous.Flow` | Flow structured execution (LLM planning -> 7 node types -> Crystallize) |
| `AgentCraftLab.Autonomous.Playground` | CLI test console (Spectre.Console) |
| `AgentCraftLab.Script` | Multi-language sandbox engine (Jint JS + Roslyn C#, IScriptEngine / IScriptEngineFactory interfaces) |
| `AgentCraftLab.Ocr` | OCR engine (Tesseract, IOcrEngine interface) |
| `AgentCraftLab.Commercial` | Commercial layer (MongoDB + OAuth, not open source) |
| `AgentCraftLab` | Blazor Web App (legacy UI, Drawflow canvas) |

**Tech Stack:** .NET 10 + LangVersion 13.0, using `Microsoft.Agents.AI` series APIs (Semantic Kernel is prohibited).

**Feature Placement Decision:** For new features, first ask "Is this needed for single-user self-hosting?" -- If yes, put it in Engine; multi-user/billing/SSO goes in Commercial; search/extraction/chunking goes in Search.

---

## 2. Open Core Mode Switching

The system detects whether `ConnectionStrings:MongoDB` is present at startup to automatically switch modes:

```
                  ConnectionStrings:MongoDB present?
                          |
               +----------+----------+
               |                     |
              No                    Yes
               |                     |
        Open Source Mode         Commercial Mode
        (default)
        - SQLite               - MongoDB (Azure DocumentDB)
        - No authentication    - Google/GitHub OAuth
        - userId="local"       - Multi-user
        - Sqlite*Store         - Mongo*Store
```

All Store interfaces (IWorkflowStore, ICredentialStore, etc.) have both SQLite and MongoDB implementations. The DI container registers the appropriate implementation at startup based on configuration.

---

## 3. Workflow Execution Three-Layer Architecture

Workflow execution is the core path of the system, divided into three layers:

```
WorkflowExecutionService.ExecuteAsync(request)        <-- Lean orchestrator (~180 lines)
  |
  +-> ParseAndValidatePayload                         <-- Validate JSON payload
  +-> Hook(OnInput)                                   <-- Input interception
  +-> WorkflowPreprocessor.PrepareAsync                <-- Layer 2: Node classification + RAG + AgentContext
  |     |
  |     +-> Classify nodes (executable / data / meta)
  |     +-> Parse RAG nodes, execute ingest
  |     +-> AgentContextBuilder builds context for each agent
  |
  +-> WorkflowStrategyResolver.Resolve                 <-- Layer 3: Strategy selection
  +-> IWorkflowStrategy.ExecuteAsync                   <-- Strategy execution
  +-> Hook(OnComplete / OnError)                       <-- Completion/error callbacks
  +-> yield IAsyncEnumerable<ExecutionEvent>            <-- Streaming output
```

### Layer Responsibilities

| Layer | Class | Responsibility |
|------|------|------|
| Orchestration Layer | `WorkflowExecutionService` | Pipeline assembly, error handling, Hooks invocation |
| Preprocessing Layer | `WorkflowPreprocessor` | Node classification, RAG indexing, AgentContext construction |
| Strategy Layer | `IWorkflowStrategy` | Concrete execution logic (determines execution order by topology) |

---

## 4. Five Execution Strategies and Auto-Detection

### Strategy Overview

| Strategy | Description | Use Case |
|------|------|----------|
| Single | Single agent direct execution | Only one agent node |
| Sequential | Execute sequentially by topological sort | Linear pipeline |
| Concurrent | Multiple agents execute in parallel | Independent agents, no dependencies |
| Handoff | Control transfer between agents | Any agent has multiple outgoing edges |
| Imperative | Step-by-step imperative execution (supports branching/looping) | Contains control flow nodes such as condition/loop/code |

### Auto-Detection Logic

```
NodeTypeRegistry.HasAnyRequiringImperative() == true ?
  |-- Yes --> Imperative strategy
  |-- No  --> Any agent has multiple outgoing edges?
                |-- Yes --> Handoff strategy
                |-- No  --> Agent count == 1?
                              |-- Yes --> Single strategy
                              |-- No  --> Sequential strategy
```

`WorkflowStrategyResolver.Resolve()` encapsulates this logic. To extend with a new strategy: implement the `IWorkflowStrategy` interface + add a case in Resolve.

---

## 5. Node System

### NodeTypeRegistry -- Single Source of Truth

All node metadata is centralized in `NodeTypeRegistry`. Each node type defines three flags:

| Node | IsExecutable | RequiresImperative | IsAgentLike | Description |
|------|:---:|:---:|:---:|------|
| `agent` | Y | | Y | Local LLM Agent (ChatClientAgent + tools) |
| `a2a-agent` | Y | Y | Y | Remote A2A Agent (URL + format) |
| `autonomous` | Y | Y | Y | ReAct loop (interface-decoupled) |
| `condition` | Y | Y | | Conditional branching |
| `loop` | Y | Y | | Loop |
| `router` | Y | Y | | Multi-route classification |
| `human` | Y | Y | | Pause waiting for user input |
| `code` | Y | Y | | Deterministic transformation (9 modes + JS/C# dual-language sandbox) |
| `iteration` | Y | Y | | foreach loop (SplitMode + MaxItems 50) |
| `parallel` | Y | Y | | fan-out/fan-in parallelism |
| `http-request` | Y | Y | | Deterministic HTTP call |
| `start` / `end` | | | | Meta nodes (IsMeta) |
| `rag` | | | | Data node (IsDataNode) |

### NodeExecutorRegistry

Each executable node type maps to an executor handler. The execution engine dispatches via `NodeExecutorRegistry` lookup.

### Steps to Add a New Node

1. Add a constant string in the `NodeTypes` class
2. Add a metadata definition entry in `NodeTypeRegistry`
3. Add the corresponding handler in `NodeExecutorRegistry`
4. (Frontend) Add node rendering definition in JS `NODE_REGISTRY`

---

## 6. Agent Tool Resolution -- Four-Layer Tool Sources

`AgentContextBuilder.ResolveToolsAsync()` is responsible for merging all tool sources:

```
+------------------------------------------------------+
|              AgentContextBuilder                      |
|                                                       |
|  Layer 1: Tool Catalog (Built-in Tools)               |
|    - Static tools registered by ToolRegistryService   |
|    - web_search, file_read, calculator, etc.          |
|                                                       |
|  Layer 2: MCP Servers (Dynamic External Tools)        |
|    - Connect to external Tool Servers via MCP protocol|
|    - Dynamically enumerate available tools            |
|                                                       |
|  Layer 3: A2A Agents (Agent-to-Agent Invocation)      |
|    - Remote Agents exposed as tools                   |
|    - Supports Google / Microsoft formats              |
|                                                       |
|  Layer 4: HTTP APIs + OCR + Script                    |
|    - API calls from http-request nodes                |
|    - OCR (IOcrEngine, Tesseract)                      |
|    - Script (IScriptEngineFactory, Jint JS + Roslyn C# sandbox)|
|                                                       |
|  --> Merged into a unified AITool[] for ChatClientAgent|
+------------------------------------------------------+
```

Each agent node's `tools` field references a list of tool IDs. ResolveToolsAsync retrieves the corresponding tool instances from the four-layer sources by ID.

---

## 7. Middleware Pipeline

Middleware uses `DelegatingChatClient` (Decorator pattern), wrapped in order by `AgentContextBuilder.ApplyMiddleware()`:

```
Outer                                              Inner
  |                                                 |
  v                                                 v
GuardRails --> PII --> RateLimit --> Retry --> Logging --> ChatClient
```

RAG is mounted independently of this pipeline (via `RagChatClient`).

### 7.1 GuardRails — Enterprise-Grade Content Safety

Decoupled through the `IGuardRailsPolicy` interface, with a default implementation `DefaultGuardRailsPolicy` that can be replaced with an ML classifier, Azure Content Safety, or NVIDIA NeMo Guardrails.

| Feature | Description |
|------|------|
| Keyword + Regex Rules | `text.Contains()` (CJK-safe) + `RegexOptions.Compiled` |
| Three-Level Actions | Block (reject with denial message), Warn (warn but allow), Log (silent logging) |
| Prompt Injection Detection | 9 built-in patterns (Chinese and English), opt-in activation |
| Topic Restriction | Restrict Agent to discuss only whitelisted topics |
| Full Message Scanning | Scans all User messages by default (not just the last one), preventing multi-turn attacks |
| Output Scanning | Optional LLM response scanning (buffered scanning in streaming mode) |
| Audit Logging | `[GUARD] Direction=Input, Action=Block, Rule="hack", Match="hack"` |
| Frontend Configuration | Blocked/Warn Terms, Regex Rules, Allowed Topics, Injection Detection, Custom Block Response |

### 7.2 PII Protection — Enterprise-Grade Personal Data Detection and Anonymization

Decoupled through `IPiiDetector` + `IPiiTokenVault` interfaces, replaceable with ONNX NER models, Microsoft Presidio, or Azure AI Language.

| Feature | Description |
|------|------|
| 35 Regex Rules × 6 Locales | Global / TW / JP / KR / US / UK, covering GDPR/HIPAA/PCI-DSS |
| 7 Checksum Validations | Luhn (credit card), mod97 (IBAN), Taiwan ID/tax ID, JP My Number, KR RRN, UK NHS |
| Context-Aware Weighting | Scans surrounding keywords to boost confidence, reducing false positives |
| Reversible Tokenization | Type-based tokens like `[EMAIL_1]`, `[PHONE_1]`, auto-restored after LLM response |
| Irreversible Mode | Fixed `***` replacement (backward compatible) |
| Bidirectional Scanning | Input anonymize + Output detokenize |
| Audit Logging | `[PII] Direction=Input, Entities=[Global.Email:1, TW.Phone:1], Count=2` (never logs raw PII) |
| Frontend Configuration | Mode (reversible/irreversible), Locale multi-select, Confidence Threshold, Scan Output |

### 7.3 RateLimit — Token Bucket Rate Limiting

Uses `System.Threading.RateLimiting.TokenBucketRateLimiter` (default 5 requests per second). Queue capacity 10, FIFO ordering. `AcquireAsync` has a 30-second timeout protection.

### 7.4 Retry — Exponential Backoff Retry

Default maximum 3 retries with exponential backoff (500ms → 1s → 2s). `IsTransient` uses `HttpRequestException.StatusCode` pattern matching (429/502/503/504) + `TaskCanceledException` + `TimeoutException`. Logs `LogError` when retries are exhausted. In streaming mode, only failures before the first chunk are retried.

### 7.5 Logging — Structured Logging

Logs input (truncated to 100 characters) + duration. Both non-streaming and streaming modes have try-catch exception logging (including duration).

### 7.6 RAG — Retrieval-Augmented Generation

Mounted independently (not in the ApplyMiddleware pipeline). Supports temporary indexes + parallel multi-knowledge-base search (`Task.WhenAll`). Search failures degrade gracefully (non-blocking, logged as Warning).

### Design Highlights

- **Interface Decoupling**: GuardRails (`IGuardRailsPolicy`) and PII (`IPiiDetector` + `IPiiTokenVault`) are both abstracted through interfaces, allowing replacement with ML/cloud services without modifying the Middleware
- **DI Smart Reuse**: `ApplyMiddleware` preferentially uses DI singletons, only creating new instances when the frontend specifies custom rules
- **Dual Constructors**: Each enhanced Middleware has both a new version (DI interface injection) and a legacy version (config dictionary) constructor, fully backward compatible
- **Defensive Programming**: RateLimit has timeout protection, Retry has StatusCode pattern matching, Logging records exceptions, RAG degrades gracefully
- **Enterprise Compliance**: PII audit logs comply with GDPR Art.30 (never logs raw PII); GuardRails supports Prompt Injection detection and Topic restriction

**Extension Method:** Inherit from `DelegatingChatClient` to implement new Middleware, and add the corresponding case in `ApplyMiddleware()`. Or implement `IGuardRailsPolicy` / `IPiiDetector` to replace the detection engine.

---

## 8. Workflow Hooks

Hooks provide 6 lifecycle insertion points, triggered at different stages of workflow execution:

```
User Input
    |
    v
 OnInput ---------> Can intercept/transform input
    |
 PreExecute ------> Before workflow starts
    |
    +-- Node Loop --+
    |              |
    | PreAgent     | --> Before each agent executes
    | PostAgent    | --> After each agent executes
    |              |
    +--------------+
    |
 OnComplete ------> Successfully completed
 OnError ---------> Execution failed
```

### Hook Types

| Type | Mechanism |
|------|------|
| `code` | Executed via TransformHelper (supports 9 transformation modes) |
| `webhook` | HTTP POST to specified URL |

`BlockPattern` supports regex interception -- if input matches the pattern, execution is immediately rejected.

---

## 9. Credentials Backend Encrypted Storage

API keys are handled entirely on the backend; the frontend never touches plaintext:

```
React /settings page
    |
    | POST /api/credentials { provider, apiKey }
    v
ICredentialStore.SaveAsync()
    |
    | DPAPI encryption (Windows Data Protection API)
    v
SQLite / MongoDB storage (ciphertext)

--- At Runtime ---

WorkflowExecutionService
    |
    | ResolveCredentialsAsync()
    v
ICredentialStore.GetDecryptedCredentialsAsync()
    |
    | DPAPI decryption
    v
Plaintext API Key --> Injected into ChatClient
```

The frontend uses a `saved` flag to determine whether a key has been configured; `localStorage` does not store any plaintext.

---

## 10. Chat Attachment Upload Pipeline

CopilotKit natively supports only image uploads and the implementation is incomplete, so a standalone upload pipeline is used:

```
User selects file
    |
    | POST /api/upload (multipart/form-data)
    v
Backend temporary storage (1-hour TTL) --> Returns { fileId }
    |
    | CopilotKit forwardedProps.fileId
    v
AG-UI endpoint receives fileId
    |
    | GetAndRemove(fileId) --> Retrieves file, removes temporary storage
    v
WorkflowPreprocessor processing
    |
    +-- Document type --> RAG ingest (extraction + chunking + indexing)
    +-- ZIP file --> Decompress + batch processing
    +-- Image/other --> Multimodal DataContent (directly injected into LLM)
```

The frontend uses `StableChatInput` (module-scope definition) + `chatInputFileRef` to ensure stable component identity, preventing CopilotChat from rebuilding the Input component.

---

## 11. Autonomous Agent -- ReAct + Flow Dual Mode

### Three-Layer Execution Funnel

```
+----------------------------------------------------------+
|  Engine Workflow (Human-Designed)                          |
|  - Developers manually drag-and-drop nodes, define flows  |
|  - Fully deterministic                                    |
+----------------------------------------------------------+
                        |
                        v
+----------------------------------------------------------+
|  Flow (AI Planning + Structured Execution + Crystallize)  |
|  - IGoalExecutor interface                                |
|  - LLM generates FlowPlan --> 7 node types structured     |
|    execution                                              |
|  - Crystallizes into editable Workflow after completion    |
+----------------------------------------------------------+
                        |
                        v
+----------------------------------------------------------+
|  ReAct (Fully Autonomous)                                 |
|  - ReactExecutor (~540 lines)                             |
|  - Observe -> Think -> Act loop                           |
|  - 12 meta-tools + Sub-agent collaboration                |
+----------------------------------------------------------+
```

### ReAct Mode Core

**Strategy Object Decomposition:**

| Interface | Responsibility |
|------|------|
| `IBudgetPolicy` | Token/step budget control |
| `IHumanInteractionHandler` | Human-machine interaction (ask_user) |
| `IHistoryManager` | Conversation history management |
| `IReflectionEngine` | Self-reflection (Reflexion) |
| `IToolDelegationStrategy` | Tool selection and delegation |

**Dual Model Architecture:** TaskPlanner uses a strong model (gpt-4o) for planning, ReactExecutor uses a weak model (gpt-4o-mini) for execution, reducing costs.

**12 meta-tools (MetaToolFactory):**
- Sub-agent management: create / ask / spawn / collect / stop / send / list
- Shared state: shared_state
- Human interaction: ask_user
- Quality control: peer_review / challenge

**Safety Levels:** P0 Risk Approval -> P1 Transparency -> P2 Self-Reflection -> S1~S8 Isolation Protection

### Flow Mode Core

Decoupled from ReAct through the `IGoalExecutor` interface. DI switching:

```csharp
// ReAct mode
services.AddAutonomousAgent();

// Flow mode
services.AddAutonomousFlowAgent();
```

**Funnel Bridging:** ReactTraceConverter converts spawn/collect traces into FlowPlan JSON, stores them in ExecutionMemoryService, and injects them as a Reference Plan during the next Flow planning session.

**Crystallize:** After execution completes, converts the ExecutionTrace into Studio buildFromAiSpec JSON, stored in `Data/flow-outputs/`, which can be directly loaded into Workflow Studio for editing.

---

## 12. CraftSearch Search Engine

`AgentCraftLab.Search` is a standalone class library with no dependencies on Engine or Autonomous.

### Core Interfaces

```
ISearchEngine          --> Search entry point (query + options)
IDocumentExtractor     --> Document content extraction (PDF/DOCX/HTML/TXT...)
ITextChunker           --> Text chunking (fixed size + overlap)
```

### Three Search Modes

```
+------------------+     +------------------+     +------------------+
|   FullText       |     |   Vector         |     |   Hybrid         |
|   (FTS5)         |     |   (SIMD Cosine)  |     |   (RRF k=60)    |
|                  |     |                  |     |                  |
|  SQLite FTS5     |     |  1536-dim vector |     |  FullText rank   |
|  Tokenizer +     |     |  Cosine          |     |  + Vector rank   |
|  BM25            |     |  similarity      |     |  RRF fusion      |
|                  |     |  SIMD accelerated|     |                  |
+------------------+     +------------------+     +------------------+
```

**RRF (Reciprocal Rank Fusion):** Fuses full-text and vector rankings with k=60, balancing exact keyword matching and semantic similarity.

**Provider Implementations:**
- `SqliteSearchEngine` -- Production (open source)
- `InMemorySearchEngine` -- Unit testing

---

## 13. RAG Pipeline

RAG functionality is built on top of CraftSearch, providing a complete retrieval-augmented generation pipeline.

### Ingest Flow

```
File upload
    |
    v
IDocumentExtractor.ExtractAsync()     --> Extract text (multi-format support)
    |
    v
ITextChunker.ChunkAsync()            --> Chunk (fixed size + overlap window)
    |
    v
Embedding (1536-dim)                  --> Vectorization
    |
    v
ISearchEngine.IndexAsync()           --> Build index
```

### Query Flow

```
User question
    |
    v
RagService.SearchAsync()             --> Hybrid search (FTS5 + Vector + RRF)
    |
    v
RagChatClient (DelegatingChatClient)  --> Inject search results into system message
    |
    v
LLM response (based on retrieved context)
```

### indexName Conventions

| Format | Purpose |
|------|------|
| `{userId}_rag_{guid}` | Temporary index (single upload) |
| `{userId}_kb_{id}` | Knowledge base index (persistent) |

---

## 14. CopilotKit Frontend Architecture

### System Overview

```
+-------------------+     +-------------------+     +-------------------+
|   React Frontend  |     |  CopilotKit       |     |  .NET API Backend |
|   (port 5173)     |     |  Runtime          |     |  (port 5200)      |
|                   |     |  (port 4000)      |     |                   |
|  React Flow       | --> |  Node.js          | --> |  Minimal API      |
|  CopilotKit SDK   |     |  server.mjs       |     |  AG-UI Endpoints  |
|  shadcn/ui        |     |  AG-UI Protocol   |     |  WorkflowEngine   |
|  i18n (en/zh-TW)  |     |  Relay            |     |  CraftSearch      |
+-------------------+     +-------------------+     +-------------------+
      Vite dev               Middle Layer              Backend Services
```

### AG-UI Protocol

CopilotKit Runtime serves as a middle layer, converting the frontend CopilotKit format to the AG-UI (Agent-UI) protocol for communication with the .NET backend. The backend streams events back to the frontend via `IAsyncEnumerable<ExecutionEvent>`.

### Frontend Main Modules

| Module | Description |
|------|------|
| Workflow Studio | React Flow canvas, drag-and-drop workflow construction |
| Chat Panel | CopilotChat integration, supports attachment uploads |
| Settings | Personal settings, Credentials, default model |
| Skill Manager | Skill management (built-in + custom) |
| Service Tester | Dual-panel + Chat, 5 protocol testing modes |
| KB Manager | Knowledge base file upload + SSE progress streaming |

### Key Design Decisions

- **Standalone Upload Pipeline:** CopilotKit native upload has many limitations, replaced with standalone `POST /api/upload`
- **ErrorBoundary:** Global error boundary, React component crashes do not cause white screens
- **i18n:** Supports en + zh-TW, divided into three namespaces: common / studio / chat
- **StableChatInput:** Module-scope definition, prevents input field state loss caused by CopilotChat rebuilding

---

## Appendix: Extensibility Quick Reference

| Extension Item | Steps |
|----------|------|
| New Execution Strategy | Implement `IWorkflowStrategy` + add case in `WorkflowStrategyResolver.Resolve()` |
| New Node Type | `NodeTypes` constant + `NodeTypeRegistry` metadata + `NodeExecutorRegistry` handler + JS `NODE_REGISTRY` |
| New Built-in Tool | Add method in `ToolImplementations.cs` + `ToolRegistryService.Register()` |
| New Middleware | Inherit from `DelegatingChatClient` + add case in `ApplyMiddleware()` |
| New Flow Node | `FlowNodeRunner` case + `FlowPlannerPrompt` + `FlowPlanValidator` + `WorkflowCrystallizer` |
| Replace Script Engine | Implement `IScriptEngine` + DI replacement (Jint JS / Roslyn C# / Python) |
| Add Script Language | `ScriptEngineFactory.Register("language", engine)` + add option to frontend CodeForm SCRIPT_LANGUAGES |
| Replace OCR Engine | Implement `IOcrEngine` + DI replacement |
| New Tool Module | Follow the `AddXxx()` + `UseXxxTools()` pattern from `AgentCraftLab.Ocr` / `AgentCraftLab.Script` |
| Replace Autonomous Strategy | Implement the corresponding interface (IBudgetPolicy, etc.) + DI `Replace` registration |
