/**
 * Agent State 共用型別 — AG-UI STATE_SNAPSHOT 透過 useCoAgent 同步的 state 結構。
 * 後端 AgUiEventConverter.BuildStateSnapshot() 產生，前端 useCoAgent 自動接收。
 */

/** 節點狀態常數 — 對應後端 AgUiEventConverter.NodeStatusExecuting/Completed/DebugPaused/Cancelled */
export const NodeStatus = { Executing: 'executing', Completed: 'completed', Cancelled: 'cancelled', DebugPaused: 'debug-paused' } as const

export interface ConsoleLog {
  ts: string
  level: 'info' | 'success' | 'error' | 'warning'
  message: string
}

export interface ExecutionStats {
  durationMs: number
  totalTokens: number
  totalSteps: number
  totalToolCalls: number
  estimatedCost: string | null
}

/** OpenTelemetry trace span — 由後端 TraceCollectorExporter 攔截 Activity 產出 */
export interface TraceSpan {
  id: string
  parentId?: string
  nodeId?: string
  name: string
  type: string
  source: 'framework' | 'platform'
  status: 'running' | 'completed' | 'error' | 'timeout' | 'cancelled'
  startMs: number
  endMs: number
  model?: string
  inputTokens?: number
  outputTokens?: number
  tokens?: number
  cost?: string
  input?: string
  result?: string
  error?: string
  toolCalls?: TraceToolCall[]
  truncated?: boolean
}

export interface TraceToolCall {
  name: string
  args?: string
  result?: string
  durationMs?: number
}

/** 完整的 trace 資料（一次 workflow 執行） */
export interface TraceData {
  traceId: string
  workflowName?: string
  totalMs: number
  totalTokens: number
  totalCost: string
  status: 'running' | 'completed' | 'error'
  spans: TraceSpan[]
}

export interface PendingDebugAction {
  nodeName: string
  nodeType: string
  output: string
}

export interface AgentState {
  /** 節點執行狀態 map：nodeName → NodeStatus */
  nodeStates: Record<string, 'executing' | 'completed' | 'cancelled' | 'debug-paused'>
  /** 各節點 output 預覽（截斷 500 字元，供 Debug/Rerun 顯示） */
  nodeOutputs?: Record<string, string>
  /** 等待中的 Debug Action（null = 無等待） */
  pendingDebugAction?: PendingDebugAction | null
  /** 等待中的 Human Input（null = 無等待） */
  pendingHumanInput: {
    prompt: string
    inputType: 'text' | 'choice' | 'approval'
    choices?: string
  } | null
  /** 最近的執行日誌（由後端 STATE_SNAPSHOT 同步） */
  recentLogs: ConsoleLog[]
  /** 執行統計（耗時 + token 數 + 步驟數） */
  executionStats?: ExecutionStats
  /** OpenTelemetry trace spans（由 STATE_SNAPSHOT 搭載） */
  traceSpans?: TraceSpan[]
  /** 當前執行 ID（由 STATE_SNAPSHOT 同步，用於 Checkpoint Resume） */
  executionId?: string
}

export const INITIAL_AGENT_STATE: AgentState = {
  nodeStates: {},
  pendingDebugAction: null,
  pendingHumanInput: null,
  recentLogs: [],
}
