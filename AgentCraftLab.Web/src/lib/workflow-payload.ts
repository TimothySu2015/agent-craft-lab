/**
 * 將 React Flow 的 nodes/edges 序列化為後端 Engine 的 Schema v2 wire format。
 *
 * F3 之後：前端 NodeData 已改 nested Schema shape，本檔只做最小包裝
 * （加 version / settings / resources），節點 `data` 直接 pass-through。
 */
import type { Node, Edge } from '@xyflow/react'
import '@/types/debug' // Window.__craftlab_debug type augmentation
import type { NodeData } from '@/types/workflow'
import type { WorkflowSettings } from '@/stores/workflow-store'

/** Wire format 頂層結構 — 對應後端 Schema.WorkflowPayload */
interface SchemaPayload {
  version: '2.0'
  settings: Record<string, unknown>
  nodes: Array<NodeData & { id: string }>
  connections: Array<{ from: string; to: string; port: string }>
  variables?: unknown[]
  hooks?: Record<string, unknown>
  resources?: Record<string, unknown>
}

export function toWorkflowPayloadJson(
  nodes: Node<NodeData>[],
  edges: Edge[],
  settings?: WorkflowSettings,
): string {
  // 1. 節點：排除 start/end/群組容器，保留 id + nested NodeData
  //    type 必須排在 JSON 物件最前面 — .NET System.Text.Json 的 JsonPolymorphic
  //    discriminator 需要在物件開頭才能正確辨識子型別。
  const payloadNodes: Array<NodeData & { id: string }> = nodes
    .filter((n) => n.type !== 'start' && n.type !== 'end' && !n.type?.endsWith('-group'))
    .map((n) => {
      const { type, ...rest } = n.data as NodeData
      return { type, id: n.id, ...rest } as NodeData & { id: string }
    })

  // 2. 連線：保留 start→node（FindStartNode 用），過濾 node→end
  //    後端 Schema.Connection 用單一 `port` 欄位（= 舊 fromOutput），不再有 toPort
  const nodeIds = new Set(payloadNodes.map((n) => n.id))
  const connections = edges
    .filter((e) => nodeIds.has(e.target))
    .map((e) => ({
      from: e.source,
      to: e.target,
      port: e.sourceHandle ?? 'output_1',
    }))

  // 3. Settings 對應 Schema.WorkflowSettings — `strategy` 取代舊 `type`
  const schemaSettings: Record<string, unknown> = {
    strategy: settings?.type ?? 'auto',
    maxTurns: settings?.maxTurns ?? 10,
  }
  if (settings?.contextPassing && settings.contextPassing !== 'previous-only') {
    schemaSettings.contextPassing = settings.contextPassing
  }

  const payload: SchemaPayload = {
    version: '2.0',
    settings: schemaSettings,
    nodes: payloadNodes,
    connections,
  }

  // 4. Variables / Hooks — 後端 Schema 已有對應結構，直接帶
  if (settings?.variables && settings.variables.length > 0) {
    payload.variables = settings.variables
  }
  if (settings?.hooks && Object.keys(settings.hooks).length > 0) {
    payload.hooks = settings.hooks
  }

  const json = JSON.stringify(payload)

  // Debug：記錄實際送出的 payload（Claude Code 可透過 Chrome JS 工具讀取）
  window.__craftlab_debug = {
    ...window.__craftlab_debug,
    lastPayloadJson: json,
  }

  return json
}
