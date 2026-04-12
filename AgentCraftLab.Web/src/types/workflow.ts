/**
 * Workflow 型別定義 — 對應後端 Engine 的 Schema.NodeConfig discriminator union。
 *
 * Phase F Stage 1（F3）：全面改為 nested Schema shape，和後端 C# 型別 1:1 對齊。
 * - 欄位命名對應 System.Text.Json camelCase（第一字小寫，其餘不變）
 *   → `A2AAgents` → `a2AAgents`、`HttpApis` → `httpApis`
 * - 所有 enum 採 camelCase literal union（C# PascalCase enum 由 JsonStringEnumConverter
 *   以 camelCase 輸出）
 * - 巢狀 config 物件（ModelConfig / OutputConfig / HistoryConfig / ConditionConfig /
 *   RagConfig / HttpRequestSpec / BranchConfig / RouteConfig / MiddlewareBinding）
 *   直接對應後端 record
 *
 * 本檔的型別同時作為：
 *   (1) React Flow 節點 `data` 欄位的內部表達
 *   (2) wire format 的 TypeScript 對映（workflow-payload.ts 直接 pass-through 輸出）
 */

// ─── Enum literal unions ─────────────────────────────────────────

export type NodeType =
  | 'agent' | 'a2a-agent' | 'autonomous' | 'rag'
  | 'condition' | 'loop' | 'router' | 'human'
  | 'code' | 'iteration' | 'parallel' | 'http-request'
  | 'start' | 'end';

/** Agent 輸出格式（對應 C# OutputFormat） */
export type OutputFormat = 'text' | 'json' | 'jsonSchema';

/** Agent 歷史訊息提供者（對應 C# HistoryProviderKind） */
export type HistoryProvider = 'none' | 'session' | 'database' | 'inMemory';

/** 條件 / 迴圈節點的判斷模式（對應 C# ConditionKind） */
export type ConditionKind = 'contains' | 'regex' | 'llmJudge' | 'expression';

/** Code 節點的轉換類型 — 9 種（對應 C# TransformKind） */
export type TransformKind =
  | 'template' | 'regex' | 'jsonPath' | 'trim'
  | 'split' | 'upper' | 'lower' | 'truncate' | 'script';

/** Code 節點 script 模式的腳本語言（對應 C# ScriptLanguage） */
export type ScriptLanguage = 'javaScript' | 'cSharp';

/** Human 節點的輸入模式（對應 C# HumanInputKind） */
export type HumanInputKind = 'text' | 'choice' | 'approval';

/** Iteration 節點的拆分模式（對應 C# SplitModeKind） */
export type SplitModeKind = 'jsonArray' | 'delimiter';

/** Parallel 節點的合併策略（對應 C# MergeStrategyKind） */
export type MergeStrategy = 'labeled' | 'join' | 'json';

/** A2A Agent 協定格式（對應 C# A2AFormat） */
export type A2AFormat = 'auto' | 'google' | 'microsoft';

/** HTTP 方法（對應 C# HttpMethodKind） */
export type HttpMethod = 'get' | 'post' | 'put' | 'delete' | 'patch' | 'head' | 'options';

/** RAG 節點的搜尋模式（RagConfig.searchMode — 後端是字串，這裡收斂） */
export type RagSearchMode = 'fulltext' | 'vector' | 'hybrid';

// ─── Nested config types ─────────────────────────────────────────

/** LLM 模型設定 — Agent / Autonomous / Condition(llm-judge) 共用 */
export interface ModelConfig {
  provider: string;
  model: string;
  temperature?: number;
  topP?: number;
  maxOutputTokens?: number;
}

/** Agent 輸出格式設定 */
export interface OutputConfig {
  kind: OutputFormat;
  /** JsonSchema 模式使用，其他模式可省略 */
  schemaJson?: string;
}

/** Agent 歷史訊息設定 */
export interface HistoryConfig {
  provider: HistoryProvider;
  maxMessages: number;
}

/** 條件節點判斷設定 */
export interface ConditionConfig {
  kind: ConditionKind;
  value: string;
}

/** Parallel 節點分支設定 */
export interface BranchConfig {
  name: string;
  goal: string;
  tools?: string[];
}

/** Router 節點路由設定 */
export interface RouteConfig {
  name: string;
  keywords: string[];
  isDefault: boolean;
}

/** Middleware 綁定 — key 為 middleware 識別符，options 為自訂參數 */
export interface MiddlewareBinding {
  key: string;
  options: Record<string, string>;
}

/** RAG 檢索設定 */
export interface RagConfig {
  dataSource: string;
  chunkSize: number;
  chunkOverlap: number;
  topK: number;
  embeddingModel: string;
  searchMode: RagSearchMode;
  minScore: number;
  queryExpansion: boolean;
  fileNameFilter?: string;
  contextCompression: boolean;
  tokenBudget: number;
}

// ─── HttpRequestSpec discriminator union ─────────────────────────

/** HTTP 請求規格 — discriminator 為 `kind` */
export type HttpRequestSpec = CatalogHttpRef | InlineHttpRequest;

/** 引用 WorkflowResources.httpApis 中預定義的 API */
export interface CatalogHttpRef {
  kind: 'catalog';
  apiId: string;
  /** 傳給 catalog API 的參數 JSON（可為 string/object/array，字串可含 {{var:}} 引用） */
  args?: unknown;
}

/** 節點內就地定義的完整 HTTP 請求 */
export interface InlineHttpRequest {
  kind: 'inline';
  url: string;
  method: HttpMethod;
  headers: HttpHeader[];
  body?: HttpBody;
  contentType: string;
  auth: HttpAuth;
  retry: RetryConfig;
  timeoutSeconds: number;
  response: ResponseParser;
  /** 回應最大字元數（0 = 不截斷） */
  responseMaxLength: number;
}

export interface HttpHeader {
  name: string;
  value: string;
}

export interface HttpBody {
  /** JSON 結構，字串可含 {{var:}} 引用 */
  content?: unknown;
}

export interface RetryConfig {
  count: number;
  delayMs: number;
}

// ─── HttpAuth discriminator union ─────────────────────────────────

export type HttpAuth =
  | NoneAuth | BearerAuth | BasicAuth | ApiKeyHeaderAuth | ApiKeyQueryAuth;

export interface NoneAuth { kind: 'none'; }
export interface BearerAuth { kind: 'bearer'; token: string; }
export interface BasicAuth { kind: 'basic'; userPass: string; }
export interface ApiKeyHeaderAuth { kind: 'apikey-header'; keyName: string; value: string; }
export interface ApiKeyQueryAuth { kind: 'apikey-query'; keyName: string; value: string; }

// ─── ResponseParser discriminator union ───────────────────────────

export type ResponseParser = TextParser | JsonParser | JsonPathParser;

export interface TextParser { kind: 'text'; }
export interface JsonParser { kind: 'json'; }
export interface JsonPathParser { kind: 'jsonPath'; path: string; }

// ─── Node base ───────────────────────────────────────────────────

/** 所有 NodeConfig 共用的基礎欄位（對應 C# NodeConfig base record） */
interface NodeBase {
  /** Type discriminator — React Flow 和 wire format 共用同一個欄位 */
  type: NodeType;
  /** 使用者可見名稱 */
  name: string;
  /** 可選說明 */
  description?: string;
  /** Meta 字典 — Flow planner 可能塞入 `flow:trueBranchIndex` 等結構化資料 */
  meta?: Record<string, string>;
}

// ─── 14 node subtypes ────────────────────────────────────────────

export interface AgentNodeData extends NodeBase {
  type: 'agent';
  instructions: string;
  model: ModelConfig;
  tools: string[];
  mcpServers: string[];
  a2AAgents: string[];
  httpApis: string[];
  skills: string[];
  output: OutputConfig;
  history: HistoryConfig;
  middleware: MiddlewareBinding[];
}

export interface A2ANodeData extends NodeBase {
  type: 'a2a-agent';
  url: string;
  format: A2AFormat;
  instructions: string;
}

export interface AutonomousNodeData extends NodeBase {
  type: 'autonomous';
  instructions: string;
  model: ModelConfig;
  maxIterations: number;
  tools: string[];
  mcpServers: string[];
  a2AAgents: string[];
  httpApis: string[];
  skills: string[];
}

export interface ConditionNodeData extends NodeBase {
  type: 'condition';
  condition: ConditionConfig;
  /** 僅當 condition.kind === 'llmJudge' 時使用 */
  judgeModel?: ModelConfig;
}

export interface LoopNodeData extends NodeBase {
  type: 'loop';
  condition: ConditionConfig;
  maxIterations: number;
  // bodyAgent 由畫布 edges 決定（placeholder），前端不存這個欄位
}

export interface RouterNodeData extends NodeBase {
  type: 'router';
  routes: RouteConfig[];
}

export interface HumanNodeData extends NodeBase {
  type: 'human';
  prompt: string;
  kind: HumanInputKind;
  /** kind === 'choice' 時使用 */
  choices?: string[];
  /** 0 = 無限等待 */
  timeoutSeconds: number;
}

export interface CodeNodeData extends NodeBase {
  type: 'code';
  /** 轉換模式 — 對應後端 TransformKind */
  kind: TransformKind;
  /** 主要運算式：template 字串 / regex pattern / json path / 腳本程式碼 */
  expression: string;
  /** Regex 替換字串（僅 kind === 'regex' 時有意義） */
  replacement?: string;
  /** Split 分隔符（僅 kind === 'split' 時有意義） */
  delimiter: string;
  /** Split 取第幾段 */
  splitIndex: number;
  /** Truncate 最大字元數（0 = 不截斷） */
  maxLength: number;
  /** 腳本語言（僅 kind === 'script' 時有意義） */
  language?: ScriptLanguage;
}

export interface IterationNodeData extends NodeBase {
  type: 'iteration';
  split: SplitModeKind;
  delimiter: string;
  maxItems: number;
  maxConcurrency: number;
  // bodyAgent 由畫布 edges 決定
}

export interface ParallelNodeData extends NodeBase {
  type: 'parallel';
  branches: BranchConfig[];
  merge: MergeStrategy;
}

export interface HttpRequestNodeData extends NodeBase {
  type: 'http-request';
  /** HTTP 請求規格 — Catalog 引用或 Inline 定義（discriminator union） */
  spec: HttpRequestSpec;
}

export interface RagNodeData extends NodeBase {
  type: 'rag';
  rag: RagConfig;
  knowledgeBaseIds: string[];
}

export interface StartNodeData extends NodeBase {
  type: 'start';
}

export interface EndNodeData extends NodeBase {
  type: 'end';
}

export type NodeData =
  | AgentNodeData
  | A2ANodeData
  | AutonomousNodeData
  | ConditionNodeData
  | LoopNodeData
  | RouterNodeData
  | HumanNodeData
  | CodeNodeData
  | IterationNodeData
  | ParallelNodeData
  | HttpRequestNodeData
  | RagNodeData
  | StartNodeData
  | EndNodeData;

// ─── Misc ────────────────────────────────────────────────────────

export type LayoutDirection = 'LR' | 'TB';

/** Workflow 變數定義 — 對應 Engine 的 VariableDef */
export interface WorkflowVariable {
  name: string;
  type: 'string' | 'number' | 'boolean' | 'json';
  defaultValue: string;
  description: string;
}

export const EMBEDDING_MODELS = [
  { value: 'text-embedding-3-small', label: 'text-embedding-3-small' },
  { value: 'text-embedding-3-large', label: 'text-embedding-3-large' },
  { value: 'text-embedding-ada-002', label: 'text-embedding-ada-002' },
] as const;
