/**
 * Workflow 型別定義 — 對應 Engine 的 NODE_REGISTRY（studio-nodes.js）。
 */

export type NodeType =
  | 'agent' | 'tool' | 'rag' | 'condition' | 'loop'
  | 'router' | 'a2a-agent' | 'human' | 'code'
  | 'iteration' | 'parallel' | 'http-request'
  | 'autonomous' | 'start' | 'end';

// ─── Per-node data interfaces ───

export interface AgentNodeData {
  type: 'agent';
  name: string;
  instructions: string;
  model: string;
  provider: string;
  endpoint: string;
  deploymentName: string;
  historyProvider: string;
  maxMessages: number;
  middleware: string;
  tools: string[];
  skills: string[];
  temperature?: number;
  topP?: number;
  maxOutputTokens?: number;
  outputFormat?: string;
  jsonSchema?: string;
  middlewareConfig?: Record<string, Record<string, string>>;
}

export interface RagNodeData {
  type: 'rag';
  name: string;
  ragDataSource: string;
  ragChunkSize: number;
  ragChunkOverlap: number;
  ragTopK: number;
  ragEmbeddingModel: string;
  knowledgeBaseIds: string[];
  ragSearchQuality?: number;    // 0=精確, 1=平衡, 2=涵蓋
  ragSearchMode?: string;       // 'fulltext' | 'vector' | 'hybrid'
  ragMinScore?: number;         // 0.001 ~ 0.1
  ragQueryExpansion?: boolean;  // 查詢擴展（預設開啟）
  ragFileNameFilter?: string;  // 檔案名稱過濾
  ragContextCompression?: boolean;
  ragTokenBudget?: number;
}

export const EMBEDDING_MODELS = [
  { value: 'text-embedding-3-small', label: 'text-embedding-3-small' },
  { value: 'text-embedding-3-large', label: 'text-embedding-3-large' },
  { value: 'text-embedding-ada-002', label: 'text-embedding-ada-002' },
] as const

export interface ConditionNodeData {
  type: 'condition';
  name: string;
  conditionType: string;
  conditionExpression: string;
  maxIterations: number;
}

export interface LoopNodeData {
  type: 'loop';
  name: string;
  conditionType: string;
  conditionExpression: string;
  maxIterations: number;
}

export interface RouterNodeData {
  type: 'router';
  name: string;
  conditionExpression: string;
  routes: string;
}

export interface A2ANodeData {
  type: 'a2a-agent';
  name: string;
  instructions: string;
  a2AUrl: string;
  a2AFormat: string;
}

export interface HumanNodeData {
  type: 'human';
  name: string;
  prompt: string;
  inputType: string;
  choices: string;
  timeoutSeconds: number;
}

export interface CodeNodeData {
  type: 'code';
  name: string;
  transformType: string;
  pattern: string;
  replacement: string;
  template: string;
  maxLength: number;
  delimiter: string;
  splitIndex: number;
  scriptLanguage?: string;
}

export interface IterationNodeData {
  type: 'iteration';
  name: string;
  splitMode: string;
  iterationDelimiter: string;
  maxItems: number;
  maxConcurrency?: number;
}

export interface ParallelNodeData {
  type: 'parallel';
  name: string;
  branches: string;
  mergeStrategy: string;
}

export interface HttpRequestNodeData {
  type: 'http-request';
  name: string;
  httpApiId: string;
  httpArgsTemplate: string;
  httpUrl: string;
  httpMethod: string;
  httpHeaders: string;
  httpBodyTemplate: string;
  httpContentType: string;
  httpResponseMaxLength: number;
  httpTimeoutSeconds: number;
  httpAuthMode: string;
  httpAuthCredential: string;
  httpAuthKeyName: string;
  httpRetryCount: number;
  httpRetryDelayMs: number;
  httpResponseFormat: string;
  httpResponseJsonPath: string;
}

export interface AutonomousNodeData {
  type: 'autonomous';
  name: string;
  instructions: string;
  model: string;
  provider: string;
  maxIterations: number;
  maxOutputTokens: number;
  tools: string[];
  skills: string[];
  mcpServers: string[];
  a2AAgents: string[];
}

export interface StartNodeData {
  type: 'start';
  name: string;
}

export interface EndNodeData {
  type: 'end';
  name: string;
}

export type NodeData =
  | AgentNodeData
  | RagNodeData
  | ConditionNodeData
  | LoopNodeData
  | RouterNodeData
  | A2ANodeData
  | HumanNodeData
  | CodeNodeData
  | IterationNodeData
  | ParallelNodeData
  | HttpRequestNodeData
  | AutonomousNodeData
  | StartNodeData
  | EndNodeData;

export type LayoutDirection = 'LR' | 'TB'
