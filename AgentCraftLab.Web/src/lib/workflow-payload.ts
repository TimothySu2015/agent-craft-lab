/**
 * 將 React Flow 的 nodes/edges 轉換為 Engine 的 WorkflowPayload JSON 格式。
 */
import type { Node, Edge } from '@xyflow/react'
import '@/types/debug' // Window.__craftlab_debug type augmentation
import type { NodeData } from '@/types/workflow'
import type { WorkflowSettings } from '@/stores/workflow-store'

export function toWorkflowPayloadJson(
  nodes: Node<NodeData>[],
  edges: Edge[],
  settings?: WorkflowSettings,
): string {
  const payloadNodes = nodes
    .filter((n) => n.type !== 'start' && n.type !== 'end' && !n.type?.endsWith('-group'))
    .map((n) => {
      const d = n.data as NodeData
      const node: Record<string, unknown> = { id: n.id, ...d }
      // Engine 期望 branches 為 comma-separated 字串，FlowPlan fallback 可能是陣列
      if (Array.isArray(node.branches)) {
        node.branches = (node.branches as any[]).map((b: any) => typeof b === 'string' ? b : b.name).join(',')
      }
      // RAG 節點：扁平欄位組裝為 Engine 期望的嵌套 ragConfig 物件
      if (node.type === 'rag') {
        node.ragConfig = {
          dataSource: node.ragDataSource ?? 'upload',
          chunkSize: node.ragChunkSize ?? 512,
          chunkOverlap: node.ragChunkOverlap ?? 50,
          topK: node.ragTopK ?? 5,
          embeddingModel: node.ragEmbeddingModel ?? 'text-embedding-3-small',
          searchMode: node.ragSearchMode ?? 'hybrid',
          minScore: node.ragMinScore ?? 0.005,
          queryExpansion: node.ragQueryExpansion ?? true,
          fileNameFilter: node.ragFileNameFilter || undefined,
          contextCompression: node.ragContextCompression ?? false,
          tokenBudget: node.ragTokenBudget ?? 1500,
        }
        node.knowledgeBaseIds = node.knowledgeBaseIds ?? []
        // 清理前端扁平欄位（Engine 不認這些）
        delete node.ragDataSource
        delete node.ragChunkSize
        delete node.ragChunkOverlap
        delete node.ragTopK
        delete node.ragEmbeddingModel
        delete node.ragSearchQuality
        delete node.ragSearchMode
        delete node.ragQueryExpansion
        delete node.ragFileNameFilter
        delete node.ragContextCompression
        delete node.ragTokenBudget
        delete node.ragMinScore
      }
      // 移除 nodeType（FlowPlan 格式殘留，Engine 不認）
      delete node.nodeType
      return node
    })

  // 過濾 connections：保留 start→node（供 FindStartNode 用），移除 node→end
  const nodeIds = new Set(payloadNodes.map((n) => (n as any).id))
  const connections = edges
    .filter((e) => nodeIds.has(e.target))  // target 必須在 payload nodes 裡（排除 →end）
    .map((e) => ({
      from: e.source,                      // source 允許 start-1（phantom start）
      to: e.target,
      fromOutput: e.sourceHandle ?? 'output_1',
      toPort: e.targetHandle ?? 'input_1',
    }))

  const payload = {
    workflowSettings: {
      type: settings?.type ?? 'auto',
      maxTurns: settings?.maxTurns ?? 10,
      ...(settings?.terminationStrategy && settings.terminationStrategy !== 'none' && { terminationStrategy: settings.terminationStrategy }),
      ...(settings?.terminationKeyword && { terminationKeyword: settings.terminationKeyword }),
      ...(settings?.aggregatorStrategy && settings.aggregatorStrategy !== 'default' && { aggregatorStrategy: settings.aggregatorStrategy }),
      ...(settings?.hooks && Object.keys(settings.hooks).length > 0 && { hooks: settings.hooks }),
      ...(settings?.contextPassing && settings.contextPassing !== 'previous-only' && { contextPassing: settings.contextPassing }),
    },
    nodes: payloadNodes,
    connections,
    ...(settings?.variables && settings.variables.length > 0 && { variables: settings.variables }),
  }

  const json = JSON.stringify(payload)

  // Debug：記錄實際送出的 payload（Claude Code 可透過 Chrome JS 工具讀取）
  window.__craftlab_debug = {
    ...window.__craftlab_debug,
    lastPayloadJson: json,
  }

  return json
}
