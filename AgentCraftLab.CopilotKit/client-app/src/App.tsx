import { CopilotKit } from "@copilotkit/react-core";
import { CopilotChat } from "@copilotkit/react-ui";
import "@copilotkit/react-ui/styles.css";
import { useState } from "react";

type ExecutionMode = "workflow" | "autonomous";

function App() {
  const [mode, setMode] = useState<ExecutionMode>("workflow");
  const [workflowJson, setWorkflowJson] = useState("");
  const [tools, setTools] = useState("azure_web_search");
  const [credentials, setCredentials] = useState({
    provider: "openai",
    apiKey: "",
    endpoint: "",
    model: "gpt-4o-mini",
  });
  const [configured, setConfigured] = useState(false);

  if (!configured) {
    return <SetupPanel
      mode={mode}
      setMode={setMode}
      credentials={credentials}
      setCredentials={setCredentials}
      workflowJson={workflowJson}
      setWorkflowJson={setWorkflowJson}
      tools={tools}
      setTools={setTools}
      onStart={() => setConfigured(true)}
    />;
  }

  const credMap: Record<string, { apiKey: string; endpoint: string; model: string }> = {
    [credentials.provider]: {
      apiKey: credentials.apiKey,
      endpoint: credentials.endpoint,
      model: credentials.model,
    },
  };

  // Autonomous 模式：直接傳 credentials + provider/model/tools
  // Workflow 模式：傳 workflowJson + credentials
  const isAutonomous = mode === "autonomous";
  const resolvedWorkflow = workflowJson || buildMinimalWorkflow(credentials);

  const properties = isAutonomous
    ? {
        credentials: credMap,
        provider: credentials.provider,
        model: credentials.model,
        tools,
      }
    : {
        workflowJson: resolvedWorkflow,
        credentials: credMap,
      };

  // Autonomous 用 craftlab-goal agent，Workflow 用 craftlab agent
  const agentName = isAutonomous ? "craftlab-goal" : "craftlab";

  const modeLabel = isAutonomous ? "Autonomous" : "Workflow";
  const modeBadgeColor = isAutonomous ? "rgba(16,185,129,0.3)" : "rgba(99,102,241,0.3)";
  const modeBadgeText = isAutonomous ? "#6ee7b7" : "#a5b4fc";

  return (
    <div style={{ height: "100vh", display: "flex", flexDirection: "column" }}>
      <header style={{
        padding: "12px 20px",
        background: "linear-gradient(135deg, #1a1a2e, #16213e)",
        color: "#fff",
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        borderBottom: "1px solid #333"
      }}>
        <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
          <span style={{ fontSize: 20, fontWeight: 700 }}>AgentCraftLab</span>
          <span style={{
            fontSize: 11,
            padding: "2px 8px",
            background: modeBadgeColor,
            borderRadius: 4,
            color: modeBadgeText
          }}>{modeLabel}</span>
        </div>
        <button
          onClick={() => setConfigured(false)}
          style={{
            background: "rgba(255,255,255,0.1)",
            border: "1px solid rgba(255,255,255,0.2)",
            color: "#fff",
            padding: "6px 14px",
            borderRadius: 6,
            cursor: "pointer",
            fontSize: 13
          }}
        >
          Settings
        </button>
      </header>

      <div style={{ flex: 1, overflow: "hidden" }}>
        <CopilotKit
          runtimeUrl="/copilotkit"
          agent={agentName}
          properties={properties}
        >
          <CopilotChat
            labels={{
              title: `AgentCraftLab — ${modeLabel}`,
              initial: isAutonomous
                ? "Autonomous mode: describe your goal, I'll plan and execute it step by step."
                : "Workflow mode: connected to AgentCraftLab Engine. Ask me anything!",
              placeholder: isAutonomous ? "Describe your goal..." : "Type your message...",
            }}
            className="copilot-chat-fullscreen"
          />
        </CopilotKit>
      </div>

      <style>{`
        .copilot-chat-fullscreen {
          height: 100% !important;
          max-height: 100% !important;
          border: none !important;
          border-radius: 0 !important;
        }
      `}</style>
    </div>
  );
}

function buildMinimalWorkflow(cred: {
  provider: string; model: string; apiKey: string; endpoint: string
}): string {
  const workflow = {
    workflowSettings: { workflowType: "auto" },
    nodes: [
      {
        id: "agent-1",
        type: "agent",
        name: "Assistant",
        instructions: "You are a helpful assistant. Answer user questions clearly and concisely.",
        provider: cred.provider,
        model: cred.model,
        tools: [],
        middleware: "",
      },
    ],
    connections: [],
  };
  return JSON.stringify(workflow);
}

interface SetupPanelProps {
  mode: ExecutionMode;
  setMode: (m: ExecutionMode) => void;
  credentials: { provider: string; apiKey: string; endpoint: string; model: string };
  setCredentials: (c: { provider: string; apiKey: string; endpoint: string; model: string }) => void;
  workflowJson: string;
  setWorkflowJson: (v: string) => void;
  tools: string;
  setTools: (v: string) => void;
  onStart: () => void;
}

function SetupPanel({ mode, setMode, credentials, setCredentials, workflowJson, setWorkflowJson, tools, setTools, onStart }: SetupPanelProps) {
  const providers: Record<string, string[]> = {
    openai: ["gpt-4o", "gpt-4o-mini", "gpt-4.1", "gpt-4.1-mini"],
    "azure-openai": ["gpt-4o", "gpt-4o-mini", "gpt-4.1", "gpt-4.1-mini"],
    ollama: ["llama3.3", "phi4", "mistral", "qwen2.5"],
    anthropic: ["claude-sonnet-4-20250514", "claude-haiku-4-5-20251001"],
  };

  const needsEndpoint = credentials.provider === "azure-openai" || credentials.provider === "ollama";
  const isAutonomous = mode === "autonomous";

  return (
    <div style={{
      height: "100vh",
      display: "flex",
      alignItems: "center",
      justifyContent: "center",
      background: "linear-gradient(135deg, #0f0f23, #1a1a2e, #16213e)",
      color: "#e2e8f0"
    }}>
      <div style={{
        background: "rgba(30,30,50,0.9)",
        borderRadius: 16,
        padding: 40,
        width: 480,
        border: "1px solid rgba(99,102,241,0.2)",
        boxShadow: "0 20px 60px rgba(0,0,0,0.5)"
      }}>
        <h1 style={{ fontSize: 24, marginBottom: 8, color: "#fff" }}>AgentCraftLab + CopilotKit</h1>
        <p style={{ fontSize: 14, color: "#94a3b8", marginBottom: 24 }}>
          AG-UI Protocol Bridge
        </p>

        {/* Mode 切換 */}
        <div style={{ display: "flex", gap: 8, marginBottom: 20 }}>
          {(["workflow", "autonomous"] as ExecutionMode[]).map(m => (
            <button
              key={m}
              onClick={() => setMode(m)}
              style={{
                flex: 1,
                padding: "10px 0",
                background: mode === m
                  ? (m === "autonomous" ? "rgba(16,185,129,0.2)" : "rgba(99,102,241,0.2)")
                  : "rgba(15,15,35,0.5)",
                border: `1px solid ${mode === m
                  ? (m === "autonomous" ? "rgba(16,185,129,0.5)" : "rgba(99,102,241,0.5)")
                  : "rgba(255,255,255,0.1)"}`,
                borderRadius: 8,
                color: mode === m ? "#fff" : "#94a3b8",
                cursor: "pointer",
                fontSize: 13,
                fontWeight: mode === m ? 600 : 400,
                transition: "all 0.2s",
              }}
            >
              {m === "workflow" ? "Workflow" : "Autonomous"}
              <div style={{ fontSize: 10, opacity: 0.7, marginTop: 2 }}>
                {m === "workflow" ? "Studio JSON" : "ReAct / Flow"}
              </div>
            </button>
          ))}
        </div>

        <label style={labelStyle}>Provider</label>
        <select
          value={credentials.provider}
          onChange={e => setCredentials({
            ...credentials,
            provider: e.target.value,
            model: providers[e.target.value]?.[0] ?? "gpt-4o-mini"
          })}
          style={inputStyle}
        >
          {Object.keys(providers).map(p => (
            <option key={p} value={p}>{p}</option>
          ))}
        </select>

        <label style={labelStyle}>API Key</label>
        <input
          type="password"
          value={credentials.apiKey}
          onChange={e => setCredentials({ ...credentials, apiKey: e.target.value })}
          placeholder="sk-..."
          style={inputStyle}
        />

        {needsEndpoint && (
          <>
            <label style={labelStyle}>Endpoint</label>
            <input
              value={credentials.endpoint}
              onChange={e => setCredentials({ ...credentials, endpoint: e.target.value })}
              placeholder="https://your-resource.openai.azure.com/"
              style={inputStyle}
            />
          </>
        )}

        <label style={labelStyle}>Model</label>
        <select
          value={credentials.model}
          onChange={e => setCredentials({ ...credentials, model: e.target.value })}
          style={inputStyle}
        >
          {(providers[credentials.provider] ?? []).map(m => (
            <option key={m} value={m}>{m}</option>
          ))}
        </select>

        {/* Autonomous 模式：工具選擇 */}
        {isAutonomous && (
          <>
            <label style={labelStyle}>Tools (comma-separated)</label>
            <input
              value={tools}
              onChange={e => setTools(e.target.value)}
              placeholder="azure_web_search, list_directory, read_file"
              style={inputStyle}
            />
          </>
        )}

        {/* Workflow 模式：自訂 JSON */}
        {!isAutonomous && (
          <details style={{ marginTop: 16 }}>
            <summary style={{ cursor: "pointer", fontSize: 13, color: "#94a3b8" }}>
              Custom Workflow JSON (optional)
            </summary>
            <textarea
              value={workflowJson}
              onChange={e => setWorkflowJson(e.target.value)}
              placeholder='Paste Studio export JSON here...'
              rows={6}
              style={{ ...inputStyle, fontFamily: "monospace", fontSize: 12, resize: "vertical" }}
            />
          </details>
        )}

        <button
          onClick={onStart}
          disabled={!credentials.apiKey}
          style={{
            marginTop: 24,
            width: "100%",
            padding: "12px 0",
            background: credentials.apiKey
              ? (isAutonomous
                  ? "linear-gradient(135deg, #10b981, #059669)"
                  : "linear-gradient(135deg, #6366f1, #8b5cf6)")
              : "#333",
            color: "#fff",
            border: "none",
            borderRadius: 8,
            fontSize: 15,
            fontWeight: 600,
            cursor: credentials.apiKey ? "pointer" : "not-allowed",
            transition: "opacity 0.2s",
          }}
        >
          {isAutonomous ? "Start Autonomous Agent" : "Start Copilot"}
        </button>
      </div>
    </div>
  );
}

const labelStyle: React.CSSProperties = {
  display: "block",
  fontSize: 13,
  fontWeight: 500,
  marginBottom: 4,
  marginTop: 14,
  color: "#cbd5e1",
};

const inputStyle: React.CSSProperties = {
  width: "100%",
  padding: "10px 12px",
  background: "rgba(15,15,35,0.8)",
  border: "1px solid rgba(99,102,241,0.3)",
  borderRadius: 8,
  color: "#e2e8f0",
  fontSize: 14,
  outline: "none",
};

export default App;
