import http from "node:http";
import {
  CopilotRuntime,
  ExperimentalEmptyAdapter,
  copilotRuntimeNodeHttpEndpoint,
} from "@copilotkit/runtime";
import { HttpAgent } from "@ag-ui/client";

// ============================================================
// CopilotKit Runtime — AG-UI 橋接伺服器
// 註冊兩個 agent：workflow（/ag-ui）+ autonomous（/ag-ui/goal）
// ============================================================

const DOTNET_BASE = process.env.AGUI_BACKEND_URL || "http://localhost:5200";
const PORT = process.env.RUNTIME_PORT || 4000;

// Agent 1: Workflow 模式 — 執行 Studio 設計的 JSON workflow
const craftLabAgent = new HttpAgent({
  url: `${DOTNET_BASE}/ag-ui`,
});

// Agent 2: Autonomous 模式 — ReAct / Flow 自主執行
const craftLabGoalAgent = new HttpAgent({
  url: `${DOTNET_BASE}/ag-ui/goal`,
});

const runtime = new CopilotRuntime({
  agents: {
    craftlab: craftLabAgent,
    "craftlab-goal": craftLabGoalAgent,
  },
});

const serviceAdapter = new ExperimentalEmptyAdapter();

const handler = copilotRuntimeNodeHttpEndpoint({
  endpoint: "/copilotkit",
  runtime,
  serviceAdapter,
});

const server = http.createServer((req, res) => {
  // 健康檢查
  if (req.url === "/health" && req.method === "GET") {
    res.writeHead(200, { "Content-Type": "application/json" });
    res.end(JSON.stringify({
      status: "ok",
      runtime: "CopilotKit",
      backend: DOTNET_BASE,
      agents: ["craftlab (workflow)", "craftlab-goal (autonomous)"],
    }));
    return;
  }

  // CORS
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
  console.log(`  Agents:`);
  console.log(`    craftlab       → ${DOTNET_BASE}/ag-ui      (Workflow)`);
  console.log(`    craftlab-goal  → ${DOTNET_BASE}/ag-ui/goal (Autonomous)`);
  console.log();
});
