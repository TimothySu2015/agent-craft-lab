# AgentCraftLab -- Getting Started Guide

This guide will help you launch AgentCraftLab in just a few minutes, create and run your first multi-Agent workflow.

---

## 1. System Requirements

| Item | Minimum Version |
|------|-----------------|
| .NET SDK | 10.0 Preview |
| Node.js | 20 LTS or above |
| npm | Included with Node.js |
| Operating System | Windows 10+, macOS, Linux |

> AgentCraftLab uses **SQLite** by default, so no additional database installation is required.

---

## 2. Docker Deployment (Recommended)

The quickest way to get started is with Docker -- no need to install .NET or Node.js locally.

### 2.1 Prerequisites

| Item | Minimum Version |
|------|-----------------|
| Docker | 20.10+ |
| Docker Compose | v2.0+ |

### 2.2 Start with One Command

```bash
git clone https://github.com/TimothySu2015/agent-craft-lab.git
cd agent-craft-lab
cp .env.example .env
# Edit .env to add your LLM API key (e.g. OPENAI_API_KEY)
docker compose up --build
```

Once the build is complete, open **http://localhost:3000** to access the Workflow Studio.

### 2.3 Configuration

Edit the `.env` file to customize:

| Variable | Default | Description |
|----------|---------|-------------|
| `WEB_PORT` | 3000 | Web UI port |
| `API_PORT` | 5200 | API port |
| `DATABASE_PROVIDER` | sqlite | Database provider (sqlite / postgresql / mongodb / sqlserver) |
| `OPENAI_API_KEY` | - | OpenAI API Key |
| `AZURE_OPENAI_API_KEY` | - | Azure OpenAI API Key |
| `AZURE_OPENAI_ENDPOINT` | - | Azure OpenAI Endpoint |

### 2.4 Data Persistence

All data is stored in the `Data/` directory (mounted as a Docker volume):
- SQLite database
- Encrypted credentials (Data Protection keys)
- Uploaded files

Data persists across container restarts.

### 2.5 Using PostgreSQL (Optional)

```bash
DATABASE_PROVIDER=postgresql \
DATABASE_CONNECTION_STRING="Host=postgres;Port=5432;Database=agentcraftlab;Username=agentcraftlab;Password=changeme" \
docker compose --profile postgres up --build
```

---

## 3. Local Development Setup

If you prefer to develop locally without Docker:

### 3.1 Get the Source Code

```bash
git clone https://github.com/your-org/AgentCraftLab.git
cd AgentCraftLab
```

### 3.2 Install Frontend Dependencies

```bash
cd AgentCraftLab.Web
npm install
cd ..
```

### 3.3 Start the Three Services

AgentCraftLab uses a frontend-backend separation architecture and requires three Terminals running simultaneously:

**Terminal 1 -- .NET API Backend (port 5200)**

```bash
dotnet run --project AgentCraftLab.Api
```

Wait until you see `Now listening on: http://localhost:5200`, then open the next Terminal.

**Terminal 2 -- CopilotKit Runtime (port 4000)**

```bash
cd AgentCraftLab.Web
node server.mjs
```

**Terminal 3 -- React Dev Server (port 5173)**

```bash
cd AgentCraftLab.Web
npm run dev:vite
```

### 3.4 Open the Browser

Navigate to **http://localhost:5173** -- you should see the Workflow Studio interface.

> No login is required. The system runs under the `local` user identity.

---

## 4. Configure API Credentials

Before running any workflow that includes LLM Agent nodes, you need to set up at least one AI model API Key.

1. Click **Settings** in the left navigation bar (or go directly to `/settings`).
2. Find the **Credentials** section.
3. Enter your API Key, for example:
   - **OpenAI API Key** -- for GPT-4o, GPT-4o-mini, and other models
   - **Azure OpenAI** -- requires additional Endpoint and Deployment Name
   - **Anthropic API Key** -- for Claude series models
   - **Google AI API Key** -- for Gemini series models
4. Click **Save** to save.

All API Keys are encrypted via DPAPI and stored on the backend. The frontend does not retain plaintext keys.

---

## 4. Create Your First Workflow

### Method 1: Create from a Template

1. On the Workflow Studio page, click **Templates**.
2. Select the **Simple Chat** template under the **Basic** category.
3. The template will automatically load onto the canvas, including a `start` node, an `agent` node, and an `end` node.
4. Click the `agent` node and confirm the model settings in the right panel (e.g., `gpt-4o-mini`).

### Method 2: Create with AI Build Using Natural Language

1. On the Workflow Studio page, open the **AI Build** panel.
2. Describe the workflow you want in natural language, for example:

   ```
   Create a translation Agent that takes Chinese input from the user, translates it into English and Japanese, then merges the results and returns them.
   ```

3. The AI will automatically generate the corresponding nodes and connections and load them onto the canvas.
4. You can manually fine-tune the node settings before executing.

---

## 5. Test Execution

1. Switch to the **Execute** tab (the chat panel on the right side of the canvas).
2. Enter a message in the input box, for example `Hello, please introduce yourself`.
3. Press send and observe the Agent's streaming response.
4. If the workflow contains multiple nodes, you can see the execution status of each node during the process.

> If you encounter API Key-related errors, go back to the Settings page and verify that Credentials are configured correctly.

---

## 6. Core Concepts Overview

| Concept | Description |
|---------|-------------|
| **Node** | The basic unit of a workflow. `agent` nodes call LLMs, `code` nodes perform data transformations, `condition` nodes handle conditional branching, etc. |
| **Edge** | Defines the execution order and data flow between nodes. |
| **Tool** | External capabilities available to Agents, sourced from four layers: built-in tools, MCP Servers, A2A Agents, and HTTP APIs. |
| **Strategy** | The system automatically selects an execution strategy based on node types: Sequential, Handoff, Imperative, etc. |
| **Middleware** | Attachable middleware layers such as GuardRails, PII filtering, Rate Limit, and more. |

---

## 7. Common Pages

| Page | Path | Purpose |
|------|------|---------|
| Workflow Studio | `/` | Visually design and execute workflows |
| Settings | `/settings` | API Credentials, language, default model |
| Skills | `/skills` | Manage Agent skills |
| Service Tester | `/tester` | Test external services such as MCP / A2A / HTTP |
| Schedules | `/schedules` | Schedule management |

---

## 8. Next Steps

- **Advanced Node Types**: Try adding `condition` (conditional branching), `iteration` (loop), `parallel` (parallel execution), and other nodes to your workflow.
- **External Tool Integration**: Attach MCP Servers or HTTP APIs to Agent nodes to extend Agent capabilities.
- **Knowledge Base (RAG)**: Upload documents to build a knowledge base, giving your Agent domain-specific knowledge.
- **Autonomous Agent**: Use `autonomous` nodes to let AI autonomously plan and execute complex tasks.
- **Export for Deployment**: Completed workflows can be exported as standalone deployment packages.

For further information, refer to the other design documents under the `docs/` directory, or check the project's `CLAUDE.md` for a complete architecture overview.
