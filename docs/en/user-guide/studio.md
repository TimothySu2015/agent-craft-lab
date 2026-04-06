# Studio Canvas Operation Guide

This document describes the interface layout and operations of the AgentCraftLab Workflow Studio.

---

## 1. Interface Overview

The Studio page consists of four main areas:

| Area | Location | Description |
|------|----------|-------------|
| Node Palette | Left panel | Node palette listing all available node types, which can be dragged onto the canvas |
| Canvas | Center area | React Flow canvas for placing nodes, drawing connections, and assembling workflows |
| Properties Panel | Right floating panel | Displayed when a node is selected, allowing you to edit all properties of that node |
| Chat Panel | Rightmost panel | Contains Execute and AI Build tabs, collapsible and resizable by dragging |

Above the canvas is the **Top Bar toolbar**, arranged from left to right:

- File operations: Load / Save / Import
- Canvas tools: Auto Layout / Settings / Templates / Save as Template
- Output: Code Generation / Export

At the bottom of the canvas is the **Console Panel**, which displays log output during execution.

### Node Palette Categories

The node palette is divided into two groups:

**Nodes (Core Nodes):**
Agent, Tool, RAG, Condition, Loop, Router, Human, Code, Iteration, Parallel, Autonomous

**Integrations (Integration Nodes):**
A2A Agent, HTTP Request

Click the arrow icon in the upper left corner to collapse the Node Palette, showing only the icon bar.

---

## 2. Basic Operations

### 2.1 Adding Nodes

Drag a node from the left Node Palette onto the canvas to add it. When you release the mouse, the node snaps to a 20px grid.

You can also use the right-click context menu to quickly add commonly used nodes (Agent, Condition, Human, Code, Parallel, Iteration).

### 2.2 Connecting Nodes

Drag a connection from the output handle at the bottom of a node to the input handle of another node. The system automatically validates connection legality:

- Cannot connect a node to itself
- Cannot connect into a Start node
- Cannot connect out from an End node
- Tool and RAG nodes can only connect to Agent nodes

Connections use the smoothstep style by default. Click a connection to select it, then press the Delete key to remove it directly (no confirmation required).

### 2.3 Selecting and Deleting Nodes

- Click a node to select it; the Properties Panel appears on the right when selected
- Press `Delete` or `Backspace` to delete the selected node (a confirmation dialog will appear)
- Click on an empty area of the canvas to deselect

### 2.4 Duplicating Nodes

Press `Ctrl+D` after selecting a node to duplicate it. The duplicated node will appear near the original.

### 2.5 Undo / Redo

- `Ctrl+Z`: Undo the last operation
- `Ctrl+Y`: Redo the last operation
- Up to 50 steps can be undone

### 2.6 Auto Layout

Click the Auto Layout button in the toolbar. The system will automatically rearrange all nodes and animate the view to fit the screen.

### 2.7 Quick Save

Press `Ctrl+S` to open the save dialog. If a save already exists, you can overwrite it directly; for the first save, you need to enter a name.

### 2.8 Context Menu

Right-click on the canvas to open the context menu:

- **Right-click on empty space**: Shows a quick add node menu
- **Right-click on a node**: Shows Duplicate, Delete, and Auto Layout options

Press `Escape` to close the context menu.

### 2.9 Importing a Workflow

Click the Import button in the toolbar and select a `.json` format workflow file to import. After importing, it replaces all content currently on the canvas.

---

## 3. Properties Panel

When any node is selected, the Properties Panel slides out on the right side of the canvas, allowing you to edit that node's properties. Different node types display different configuration fields. Common items include:

- **Label**: The name displayed on the canvas for the node
- **Instructions**: The Agent's system prompt (supports full-screen editing)
- **Model**: The LLM model used by the Agent
- **Tools**: Attached built-in tools, MCP Servers, HTTP APIs
- **Condition / Routing Rules**: Logic for Condition and Router nodes
- **Code Settings**: Transform mode and script for Code nodes

Click on an empty area of the canvas to close the Properties Panel.

---

## 4. Chat Panel

The Chat Panel is located on the rightmost side of the interface. You can drag its left edge to adjust the width (280px ~ 800px), or click the collapse button to hide it.

### 4.1 Execute Tab

Used to execute the current workflow on the canvas:

1. Enter a message in the input box (as input to the workflow)
2. You can upload files via the attachment button
3. After sending, the system streams the response via the AG-UI protocol
4. If a Human node is encountered during execution, an interaction panel appears (text input / choice / approval modes)

### 4.2 AI Build Tab

Describe your requirements in natural language, and the AI will automatically build a workflow on the canvas:

1. Switch to the AI Build tab
2. Describe the workflow you want in natural language (e.g., "Create a customer service bot that first classifies the issue and then routes it to an expert for answering")
3. The AI will stream the node configuration, updating the canvas in real time

AI Build uses a partial update priority strategy (incremental updates), performing a full rebuild only when necessary.

---

## 5. Workflow Settings

Click the gear icon in the toolbar to open the settings dialog, where you can configure:

### Middleware

Sequentially wraps the Agent's ChatClient, providing enterprise-grade security and observability:

#### GuardRails -- Content Safety Guardrails

Protects Agents from processing or producing policy-violating content. Configurable in the Middleware settings panel:

- **Scan All Messages**: Scan all conversation messages (not just the last one) to prevent multi-turn attacks
- **Scan Output**: Scan LLM responses to prevent the model from leaking sensitive content
- **Injection Detection**: Detect Prompt Injection attacks (e.g., "ignore previous instructions"), 9 patterns in Chinese and English
- **Blocked Terms**: Blocked keywords (comma-separated); triggers a rejection message
- **Warn Terms**: Warning keywords; logs a warning but allows the message through
- **Regex Rules**: Regular expression rules (one per line) for complex pattern matching
- **Allowed Topics**: Restrict the Agent to discussing only specified topics (comma-separated); off-topic messages are automatically blocked
- **Blocked Response**: Custom rejection response message

#### PII Masking -- Personal Data Protection

Automatically detects and masks Personally Identifiable Information (PII), supporting GDPR/HIPAA/PCI-DSS enterprise compliance:

- **Protection Mode**:
  - *Irreversible*: PII is replaced with `***`; the LLM never sees the original data
  - *Reversible*: PII is replaced with tokens like `[EMAIL_1]`, `[PHONE_1]`; automatically restored after the LLM responds
- **Region Rules**: Select which regional formats to detect
  - Global (Email, IP, Credit Card, IBAN, URL, Cryptocurrency Address)
  - Taiwan (National ID, Phone, Tax ID, NHI Card, Address)
  - Japan (My Number, Phone, Passport, Driver's License)
  - Korea (Resident Registration, Phone, Business Registration)
  - US (SSN, Phone, Passport, Driver's License)
  - UK (NHS, NINO, Passport, Postal Code)
- **Confidence Threshold**: Confidence threshold (0.0-1.0); detection results below this value are ignored
- **Scan Output**: Scan PII in LLM responses
- **Custom Patterns**: Custom regular expression rules

#### Other Middleware

- **RateLimit**: Limits LLM call frequency (Token Bucket algorithm, default 5 per second)
- **Retry**: Automatic retry on failure (exponential backoff, up to 3 times), supports HTTP 429/503 and other transient errors
- **Logging**: Logs the input, output, and duration of each LLM call

### Hooks (Event Hooks)

6 insertion points that allow you to inject custom logic at specific stages of workflow execution:

| Hook | Trigger Timing |
|------|----------------|
| OnInput | When input is received |
| PreExecute | Before execution |
| PreAgent | Before each Agent executes |
| PostAgent | After each Agent executes |
| OnComplete | When execution completes |
| OnError | When an error occurs |

Each Hook supports two types:
- **code**: Data transformation using TransformHelper
- **webhook**: Sends an HTTP POST to a specified URL

Additionally, **BlockPattern** is supported, which can intercept specific content using regular expressions.

---

## 6. Code Generation

Click the Code button in the toolbar to convert the current workflow into executable code. Three languages are supported:

| Language | Description |
|----------|-------------|
| C# | Uses the Microsoft.Agents.AI framework, directly integrable into .NET projects |
| Python | Equivalent implementation in Python |
| TypeScript | Equivalent implementation in TypeScript/Node.js |

The generated code can be copied to the clipboard for direct use in project development.

---

## 7. Export

Click the Export button in the toolbar to choose from four export modes:

| Mode | Description |
|------|-------------|
| JSON | Export as a `.json` file for backup or re-import |
| Web API | Generate a complete .NET Web API deployment package |
| Teams Bot | Generate a Microsoft Teams Bot deployment package |
| Console App | Generate a .NET Console application deployment package |

The three modes other than JSON produce compressed files containing a complete project structure, ready for building and deployment.

---

## 8. Save as Template

Click the bookmark icon in the toolbar, enter a template name, and save the current workflow as a custom template. Saved templates appear in the Templates dialog for quick reuse later.

Custom templates are synced to backend persistent storage. If the backend is unavailable, it falls back to local storage.

---

## Keyboard Shortcuts Overview

| Shortcut | Function |
|----------|----------|
| `Ctrl+S` | Open save dialog |
| `Ctrl+Z` | Undo (up to 50 steps) |
| `Ctrl+Y` | Redo |
| `Ctrl+D` | Duplicate selected node |
| `Delete` / `Backspace` | Delete selected node or connection |
| `Escape` | Close context menu |
