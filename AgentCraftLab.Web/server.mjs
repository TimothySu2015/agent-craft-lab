import http from "node:http";
import {
  CopilotRuntime,
  ExperimentalEmptyAdapter,
  OpenAIAdapter,
  copilotRuntimeNodeHttpEndpoint,
} from "@copilotkit/runtime";
import { HttpAgent } from "@ag-ui/client";
import OpenAI from "openai";

// ============================================================
// CopilotKit Runtime — AG-UI 橋接伺服器
//
// LLM 路由模式（Generative UI）：
//   設定方式（擇一）：
//     A. 環境變數 OPENAI_API_KEY
//     B. 環境變數 AZURE_OPENAI_API_KEY + AZURE_OPENAI_ENDPOINT
//     C. 從後端 /api/credentials 自動讀取（憑證管理頁面設定的 key）
//     D. 都不設 → 純轉發模式（預設）
// ============================================================

const DOTNET_BASE = process.env.AGUI_BACKEND_URL || "http://localhost:5200";
const PORT = process.env.RUNTIME_PORT || 4000;

// ─── 從後端讀取前端設定的 credentials ───
async function fetchBackendCredentials() {
  try {
    const res = await fetch(`${DOTNET_BASE}/api/credentials/runtime-keys`);
    if (!res.ok) return null;
    const creds = await res.json();
    // creds 格式: [{ provider, apiKey, endpoint, model }]
    // 優先 Azure OpenAI，其次 OpenAI
    const azure = creds.find((c) => c.provider === "azure-openai" && c.apiKey);
    if (azure) {
      return { type: "azure", apiKey: azure.apiKey, endpoint: azure.endpoint, model: azure.model || "gpt-4o-mini" };
    }
    const openai = creds.find((c) => c.provider === "openai" && c.apiKey);
    if (openai) {
      return { type: "openai", apiKey: openai.apiKey, model: openai.model || "gpt-4o-mini" };
    }
  } catch {
    // 後端不可用，忽略
  }
  return null;
}

function createAzureAdapter(apiKey, endpoint, model) {
  const deployment = model || "gpt-4o-mini";
  const client = new OpenAI({
    apiKey,
    baseURL: `${endpoint.replace(/\/$/, "")}/openai/deployments/${deployment}`,
    defaultQuery: { "api-version": "2024-10-21" },
    defaultHeaders: { "api-key": apiKey },
  });
  return {
    adapter: new OpenAIAdapter({ openai: client, model: deployment }),
    mode: `Azure OpenAI (${deployment})`,
  };
}

function createOpenAIAdapter(apiKey, model) {
  const m = model || "gpt-4o-mini";
  const client = new OpenAI({ apiKey });
  return {
    adapter: new OpenAIAdapter({ openai: client, model: m }),
    mode: `OpenAI (${m})`,
  };
}

async function createServiceAdapter() {
  // 1. 優先環境變數 — Azure
  const envAzureKey = process.env.AZURE_OPENAI_API_KEY || "";
  const envAzureEndpoint = process.env.AZURE_OPENAI_ENDPOINT || "";
  const envAzureDeployment = process.env.AZURE_OPENAI_DEPLOYMENT || "gpt-4o-mini";
  if (envAzureKey && envAzureEndpoint) {
    return createAzureAdapter(envAzureKey, envAzureEndpoint, envAzureDeployment);
  }

  // 2. 環境變數 — OpenAI
  const envOpenAIKey = process.env.OPENAI_API_KEY || "";
  if (envOpenAIKey) {
    return createOpenAIAdapter(envOpenAIKey);
  }

  // 3. 從後端 /api/credentials 讀取（憑證管理頁面的設定）
  const backendCred = await fetchBackendCredentials();
  if (backendCred) {
    if (backendCred.type === "azure") {
      return createAzureAdapter(backendCred.apiKey, backendCred.endpoint, backendCred.model);
    }
    return createOpenAIAdapter(backendCred.apiKey, backendCred.model);
  }

  // 4. 預設：純轉發
  return { adapter: new ExperimentalEmptyAdapter(), mode: "Empty (passthrough)" };
}

// ─── 啟動（async） ───
(async () => {
  const { adapter: serviceAdapter, mode: adapterMode } = await createServiceAdapter();

  const craftLabAgent = new HttpAgent({ url: `${DOTNET_BASE}/ag-ui` });
  const craftLabGoalAgent = new HttpAgent({ url: `${DOTNET_BASE}/ag-ui/goal` });

  const runtime = new CopilotRuntime({
    agents: {
      craftlab: craftLabAgent,
      "craftlab-goal": craftLabGoalAgent,
    },
  });

  const handler = copilotRuntimeNodeHttpEndpoint({
    endpoint: "/copilotkit",
    runtime,
    serviceAdapter,
  });

  const server = http.createServer((req, res) => {
    if (req.url === "/health" && req.method === "GET") {
      res.writeHead(200, { "Content-Type": "application/json" });
      res.end(JSON.stringify({
        status: "ok",
        runtime: "CopilotKit",
        adapter: adapterMode,
        backend: DOTNET_BASE,
        agents: ["craftlab (workflow)", "craftlab-goal (autonomous)"],
      }));
      return;
    }

    res.setHeader("Access-Control-Allow-Origin", "*");
    res.setHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
    res.setHeader("Access-Control-Allow-Headers", "Content-Type");
    if (req.method === "OPTIONS") {
      res.writeHead(204);
      res.end();
      return;
    }

    handler(req, res);
  });

  server.listen(PORT, () => {
    console.log();
    console.log("  CopilotKit Runtime Bridge");
    console.log(`  Runtime:     http://localhost:${PORT}/copilotkit`);
    console.log(`  Adapter:     ${adapterMode}`);
    console.log(`  Agents:`);
    console.log(`    craftlab       → ${DOTNET_BASE}/ag-ui      (Workflow)`);
    console.log(`    craftlab-goal  → ${DOTNET_BASE}/ag-ui/goal (Autonomous)`);
    if (adapterMode === "Empty (passthrough)") {
      console.log();
      console.log("  Tip: 在憑證管理頁面設定 Azure OpenAI 或 OpenAI Key，重啟 Runtime 即可啟用 Generative UI");
      console.log("       或設定環境變數 OPENAI_API_KEY / AZURE_OPENAI_API_KEY + AZURE_OPENAI_ENDPOINT");
    }
    console.log();
  });
})();
