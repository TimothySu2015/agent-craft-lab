# Tool System and RAG Knowledge Base

This document describes the AgentCraftLab tool system, RAG (Retrieval-Augmented Generation) knowledge base features, and the Skill system.

---

## Part 1: Tool System

### 1.1 Agent Four-Layer Tool Sources

Each Agent node can simultaneously use tools from four sources, merged at runtime by `AgentContextBuilder.ResolveToolsAsync()`:

| Layer | Source | Description |
|-------|--------|-------------|
| 1 | Tool Catalog (Built-in Tools) | Platform-provided tools for search, file operations, email, etc. |
| 2 | MCP Servers | External tool servers connected via the Model Context Protocol |
| 3 | A2A Agents | Remote Agents called via the Agent-to-Agent protocol |
| 4 | HTTP APIs | Custom HTTP endpoints used as deterministic tool calls |

Additionally, OCR and JS sandbox script engines are mounted as extension modules, and when enabled, are also merged into the tool list.

In Workflow Studio, you can select built-in tools, enter MCP Server URLs, A2A Agent URLs, or HTTP API endpoints in the Agent node's settings panel.

### 1.2 Built-in Tools (Tool Catalog)

Built-in tools are categorized as follows:

**Search**

| Tool ID | Name | Description | Requires Credentials |
|---------|------|-------------|---------------------|
| `azure_web_search` | Azure Web Search | Search real-time web information via Azure OpenAI Responses API | azure-openai |
| `tavily_search` | Tavily Search | AI-specialized search engine (free 1,000 queries/month) | tavily |
| `tavily_extract` | Tavily Extract | Extract clean web content from URLs, automatically removing ads | tavily |
| `brave_search` | Brave Search | Privacy-focused search engine (free 2,000 queries/month) | brave |
| `serper_search` | Serper (Google) | Google Search API, supports search/news/images/places | serper |
| `web_search` | Web Search (Free) | DuckDuckGo + Wikipedia, free, no API Key required | -- |
| `wikipedia` | Wikipedia | Wikipedia encyclopedia search (auto-detects Chinese/English) | -- |

**Utility**

| Tool ID | Name | Description |
|---------|------|-------------|
| `get_datetime` | Date & Time | Get the current date, time, and timezone |
| `calculator` | Calculator | Evaluate mathematical expressions |
| `uuid_generator` | UUID Generator | Generate a unique UUID / GUID |
| `send_email` | Send Email | Send email via SMTP (requires smtp credentials) |

**Web**

| Tool ID | Name | Description |
|---------|------|-------------|
| `url_fetch` | URL Fetch | Fetch a text content summary from a specified web page |

**Data**

| Tool ID | Name | Description |
|---------|------|-------------|
| `json_parser` | JSON Parser | Parse a JSON string and extract specified fields |
| `csv_log_analyzer` | CSV Log Analyzer | Read CSV files from a directory and merge them for AI analysis |
| `zip_extractor` | ZIP Extractor | Extract a ZIP file to a temporary directory |
| `write_file` | Write File | Write text to a file (csv/json/txt/md/xml/yaml/html, etc.) |
| `write_csv` | Write CSV | Write JSON array data to a CSV file |
| `list_directory` | List Directory | List directory structure (tree format) |
| `read_file` | Read File | Read a specified line range from a file (with line numbers) |
| `search_code` | Search Code | Search for code matching a regex pattern in the codebase |

Tools that require credentials must have their corresponding API Keys configured in the Credentials section of the `/settings` page. Tools without configured credentials will still appear in the list but will return an error message when executed.

### 1.3 MCP Server Integration

MCP (Model Context Protocol) allows Agents to connect to external tool servers via a standard protocol.

**Configuration:**

1. Select an Agent node in Workflow Studio
2. Find the "MCP Servers" section in the node settings panel
3. Enter the MCP Server URL (e.g., `http://localhost:3001/mcp`)
4. You can add multiple MCP Server URLs

**Connection Flow:**

At runtime, the system sends a discovery request to each MCP Server URL to obtain the list of tools provided by that server, which are automatically merged into the Agent's available tools. If the connection fails, the system logs a warning but does not interrupt execution.

**Test MCP Server:**

```bash
npx -y @modelcontextprotocol/server-everything streamableHttp
# Once started, connect via http://localhost:3001/mcp
```

### 1.4 A2A Agent Integration

A2A (Agent-to-Agent) protocol enables local Agents to call remote Agents as tools.

**Configuration:**

1. Find the "A2A Agents" section in the Agent node settings panel
2. Enter the remote Agent's URL
3. Select the format: `auto` (auto-detect), `google` (Google A2A format), `microsoft` (Microsoft format)

**Connection Flow:**

The system first sends a discovery request to the URL to obtain the Agent Card (containing name, description, capabilities, etc.), then wraps the remote Agent as an AITool for the local Agent to call.

Alternatively, you can use the standalone `a2a-agent` node type to include a remote Agent node directly in the workflow.

### 1.5 HTTP API Tools

HTTP API tools allow Agents to call custom HTTP endpoints, suitable for connecting to internal systems or third-party REST APIs.

**Configuration:**

Add HTTP API endpoint information in the Agent node settings; the system wraps it as a tool available for the Agent to call.

There is also a standalone `http-request` node type that can be used as a deterministic HTTP call step in the workflow, bypassing LLM decision-making.

### 1.6 OCR Tool

AgentCraftLab integrates the Tesseract OCR engine, provided as an extension module (`AgentCraftLab.Ocr`).

**Activation Condition:** Automatically enabled when the system detects the `tessdata` directory exists.

**Supported Languages:** Traditional Chinese, Simplified Chinese, English, Japanese, Korean.

Once enabled, the OCR tool is automatically merged into the Agent's tool list, allowing the Agent to recognize text in images.

### 1.7 JS Sandbox Script

The Code node supports a script mode that executes JavaScript scripts in a JS sandbox using the Jint engine.

**Purpose:** Deterministic data transformation without consuming LLM tokens. Examples include JSON field restructuring, formatting, filtering, etc.

**Features:**

- Sandbox isolation that does not affect the main system
- Extendable sandbox APIs via `ISandboxApi`
- AI script generation support: via the `POST /api/script-generator` endpoint, an LLM can automatically generate sandbox-compatible JS scripts based on a description
- Test Run support: test script output before deployment

The engine interface is `IScriptEngine`, which can be swapped via DI for Roslyn or Python engines.

---

## Part 2: RAG and Knowledge Base

### 2.1 RAG Concept Overview

RAG (Retrieval-Augmented Generation) is a technique that combines "retrieval" and "generation." The execution flow is:

1. **Ingest:** Extract text from documents → chunking → embedding (vectorization) → write to search index
2. **Search:** User input → vectorization → search for relevant chunks in the index → Rerank reordering
3. **Augment:** Inject search results (with source metadata) into the LLM's system message as context
4. **Generate:** The LLM generates an answer based on the context and the question

AgentCraftLab's RAG pipeline uses `RagService` for Ingest and `RagChatClient` (DelegatingChatClient) for search and injection. The search engine is provided by the standalone `AgentCraftLab.Search` class library.

### 2.2 Knowledge Base Management

Knowledge bases are the core of RAG, suitable for document collections that need persistent storage and repeated use. Managed via the `/knowledge-bases` page.

**Creating a Knowledge Base:**

1. Go to the knowledge base management page
2. Click "Create Knowledge Base"
3. Fill in the name and description
4. Configure index parameters (immutable after creation):
   - **Embedding Model:** Vectorization model (text-embedding-3-small / large / ada-002)
   - **Chunk Strategy:** Chunking strategy
     - **Fixed Size:** Splits by character count with overlap, suitable for all documents
     - **Structural:** Splits by Markdown/HTML headings and paragraph boundaries, suitable for structured documents
   - **Chunk Size:** Chunk size in characters (default: 512)
   - **Chunk Overlap:** Chunk overlap region in characters (default: 50)
5. After creation, the system creates a corresponding search index (naming convention: `{userId}_kb_{id}`)

> **Note:** The Embedding model, chunk strategy, Chunk Size, and Overlap cannot be modified after creation. To change these settings, delete the knowledge base and create a new one.

**Smart Defaults:**

When switching the Chunk Strategy, the system automatically recommends corresponding Chunk Size and Overlap values:
- Fixed Size → 512 / 50
- Structural → 1024 / 100

**Uploading Files:**

1. Select an existing knowledge base
2. Upload files (supports PDF / DOCX / PPTX / HTML / TXT / MD / CSV / JSON and other formats)
3. The system reports processing progress in real time via SSE (Server-Sent Events):
   - Extracting text... (extracting text, auto-populating metadata: title, author, page count, etc.)
   - Chunking text... (chunking according to the configured strategy)
   - Generating embeddings... (vectorization, dimensions determined dynamically by the model)
   - Ingested X chunks (complete)

**URL Crawl:**

The knowledge base detail panel provides a URL input field for crawling web content directly into the knowledge base:

1. Enter a web page URL
2. The system automatically fetches the page → extracts text (using HtmlExtractor) → chunks according to KB settings → embedding → indexing
3. Supports SSE streaming progress reporting
4. Crawled content is stored in the knowledge base with the filename `{domain}_{path}.html`

**File Replacement:**

When uploading a file with the same name as an existing file, the system automatically deletes the old file's chunks and re-ingests the new file. Users do not need to manually delete the old file before re-uploading. Filename matching is case-insensitive.

**Chunk Preview after Upload:**

After upload completes, the progress area displays a preview of the first 3 chunks, allowing users to immediately verify whether the chunking quality meets expectations. The preview automatically disappears after 10 seconds.

**Managing Files:**

- The knowledge base detail panel displays the list of uploaded files, chunk counts, and creation times
- The panel header shows a summary of index settings (embedding model / chunk strategy / chunk size)
- Individual file deletion is supported (with a confirmation dialog); the system synchronously removes all chunks and vector data corresponding to that file

**KB Statistics:**

The detail panel header displays a file type distribution summary (e.g., PDF: 3 · DOCX: 1 · HTML: 2), providing a quick overview of the knowledge base's content composition.

**Retrieval Test:**

A retrieval test feature is provided at the bottom of the knowledge base detail panel, allowing you to verify search quality before going live:

1. Enter a question and click search
2. View the recalled chunks, including: source filename, section number, relevance score
3. Click a result to expand and view the full chunk content
4. Adjust test parameters (TopK, search mode, minimum score threshold)

### 2.3 Using a Knowledge Base in Workflows

1. Drag a `rag` node into Workflow Studio
2. In the rag node settings, select the knowledge base to use
3. Connect the rag node to an Agent node

**RAG Node Settings:**

- **Knowledge Base Selection:** Select an existing knowledge base (Embedding Model is displayed as read-only, determined by the knowledge base)
- **Search Quality Slider:** Precise <-> Broad, three-level toggle
  - **Precise:** TopK=3, MinScore=0.01 (fewer, high-quality results)
  - **Balanced:** TopK=5, MinScore=0.005 (default)
  - **Broad:** TopK=10, MinScore=0.001 (more results, less omission)
- **Advanced Search Settings (collapsed):** TopK, search mode (Hybrid/Vector/FullText), minimum score threshold
- **Query Expansion:** Enabled by default; auto-generates query variants to improve recall. Can be toggled off.
- **File Filter:** Filter search results by file name (e.g., ".pdf" or "report")
- **Context Compression:** Disabled by default. When enabled, compresses context with LLM if total tokens exceed the token budget
- **Token Budget:** Default 1500, only shown when compression is enabled

At runtime, `RagChatClient` searches the knowledge base index for relevant content, applies Rerank reordering, then injects the results into the Agent's context (with source metadata annotations).

**Citation Tracking:**

During execution, citation sources found by RAG search are sent to the frontend via STATE_SNAPSHOT. The ConsolePanel includes a "Sources" tab that displays each citation's source filename, section number, and relevance score. Click to expand and view the full chunk content.

### 2.4 Search Modes

The search engine (`AgentCraftLab.Search`) supports three search modes:

| Mode | Description | Applicable Scenarios |
|------|-------------|---------------------|
| **FullText** | Full-text search using SQLite FTS5 (trigram, CJK-compatible) | Exact keyword matching, known term searches |
| **Vector** | SIMD-accelerated Cosine Similarity vector search | Semantic similarity search, fuzzy concept matching |
| **Hybrid** (default) | Combines FullText + Vector, fused via RRF (k=60) ranking | Best choice for most scenarios |

### 2.5 Advanced RAG Components

AgentCraftLab implements key components of the Advanced RAG architecture:

| Component | Description |
|-----------|-------------|
| **Relevance Filtering** | `MinScore` threshold filters out low-scoring results, preventing irrelevant content from being injected into the LLM |
| **Reranker** | `IReranker` interface, supports NoOp (default) / Cohere API / LLM reranking |
| **Metadata Enrichment** | Format-specific extractors automatically extract document metadata (title/author/page count) and inject it into search results |
| **Structural Chunker** | Splits by Markdown/HTML headings + paragraph boundaries, preserving document structural semantics |
| **Query Expansion** | `QueryExpander` uses LLM to generate 2 query variants, searches in parallel, then merges and deduplicates results. Improves recall by 30%+. Enabled by default; can be toggled off in RAG node settings. |
| **File Name Filter** | `FileNameFilter` filters search results by file name substring (case-insensitive). e.g., ".pdf" only searches PDF files, "report" only searches files with "report" in the name. |
| **Context Reorder** | Lost in the Middle solution — after Rerank, rearranges chunks so that the highest-scoring ones are placed at the beginning and end (where LLM attention is strongest), while lower-scoring ones go in the middle, improving LLM retention of key information |
| **Context Compression** | Token budget adaptive compression — when search results exceed the configured token budget, uses LLM summarization to compress; skips compression when under budget to avoid unnecessary latency |

The combination of Hybrid search + Rerank + MinScore filtering ensures that the context the LLM receives is both relevant and precise.

---

## Part 3: Skill System

A Skill is a predefined combination of "instructions + tools" that gives an Agent capabilities in a specific domain.

### 3.1 Built-in Skills

The system provides multiple built-in Skills by default, organized into five categories:

| Category | Examples | Description |
|----------|----------|-------------|
| **Domain Knowledge** | Code Review, Legal Contract Review | Injects professional review guidelines and evaluation criteria |
| **Methodology** | Structured Reasoning | Injects thinking frameworks (e.g., Chain-of-Thought) |
| **Output Format** | -- | Standardizes the Agent's output structure |
| **Persona** | -- | Sets a specific role personality for the Agent |
| **Tool Preset** | -- | Pre-selects specific tool combinations |

Each Skill includes:
- **Instructions:** Injected into the Agent's system prompt
- **Tools:** Automatically included tool list (checks credential availability, skipping tools without configured credentials)

In Workflow Studio, both individual Agent nodes and entire Flows can have Skills attached.

### 3.2 Custom Skills

Manage custom Skills via the `/skills` page (Skill Manager):

**Creating a Custom Skill:**

1. Go to the Skill Manager page
2. Click "Add Skill"
3. Fill in:
   - **Name**
   - **Description**
   - **Category**
   - **Icon**
   - **Instructions:** Professional instructions injected into the Agent
   - **Tool List:** Select built-in tools to be automatically included

**Management Operations:**

- Edit: Modify settings of existing Skills
- Delete: Remove Skills that are no longer needed
- View Built-in Skills: View detailed instruction content of built-in Skills

Custom Skill data is stored in `ISkillStore` (the storage backend depends on the configured database provider).

---

## Quick Reference

| Feature | Page Path | Description |
|---------|-----------|-------------|
| API Key Settings | `/settings` | Configure credentials for various providers and tools |
| Knowledge Base Management | `/knowledge-bases` | Create knowledge bases, upload files, manage documents |
| Skill Management | `/skills` | View built-in Skills, create custom Skills |
| Service Testing | Service Tester | Test external service connections for MCP / A2A, etc. |
