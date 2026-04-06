/** 前端診斷資料 — Claude Code 透過 Chrome JS 工具讀取 */
export interface CraftLabDebug {
  lastAiBuildSpec?: unknown
  lastExpandedSpec?: unknown
  lastPayloadJson?: string
  lastApplyErrors?: string[]
  ts?: string
}

declare global {
  interface Window {
    __craftlab_debug?: CraftLabDebug
  }
}
