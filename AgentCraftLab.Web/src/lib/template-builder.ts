/**
 * Template Builder — 將簡化定義轉為完整的 React Flow nodes + edges。
 * 輸出格式與 workflow save/load 相同：{ nodes: Node[], edges: Edge[] }
 */
import type { Node, Edge } from '@xyflow/react'
import { NODE_REGISTRY } from '@/components/studio/nodes/registry'
import type { NodeData, NodeType } from '@/types/workflow'

export interface TemplateNodeDef {
  type: NodeType;
  name: string;
  /** Schema v2: 欄位直接在 node 物件上（無 data wrapper） */
  [key: string]: unknown;
}

export interface TemplateConnection {
  from: number;  // index into nodes array (0-based, excludes start/end)
  to: number;
  fromOutput?: string;
  toPort?: string;
}

export interface TemplateDef {
  nodes: TemplateNodeDef[];
  connections: TemplateConnection[];
}

export interface TemplateWorkflow {
  nodes: Node<NodeData>[];
  edges: Edge[];
}

/**
 * 將簡化範本定義轉為完整的 React Flow nodes + edges。
 * 自動加 Start/End 節點，自動計算位置。
 */
export function buildTemplate(def: TemplateDef): TemplateWorkflow {
  const allNodes: Node<NodeData>[] = []
  const allEdges: Edge[] = []

  const xStart = 50
  const xGap = 250
  const yBase = 200

  // Start node
  allNodes.push({
    id: 'start-1',
    type: 'start',
    position: { x: xStart, y: yBase },
    data: { type: 'start', name: 'Start' },
  })

  // Template nodes
  const nodeIds: string[] = []
  def.nodes.forEach((nd, i) => {
    const config = NODE_REGISTRY[nd.type]
    if (!config) return

    const id = `${nd.type}-${i + 1}`
    nodeIds.push(id)

    const defaultData = config.defaultData(nd.name)
    // Schema v2: 欄位直接在 node 物件上（type/name 已在 defaultData 中）
    const { type: _t, name: _n, ...fields } = nd
    const mergedData = { ...defaultData, ...fields, name: nd.name } as NodeData

    // 簡單水平排列，多分支的往下偏移
    const x = xStart + xGap * (i + 1)
    const y = yBase + (i % 2 === 1 ? -60 : 0)

    allNodes.push({ id, type: nd.type, position: { x, y }, data: mergedData })
  })

  // End node
  const endX = xStart + xGap * (def.nodes.length + 1)
  allNodes.push({
    id: 'end-1',
    type: 'end',
    position: { x: endX, y: yBase },
    data: { type: 'end', name: 'End' },
  })

  // Start → first node
  if (nodeIds.length > 0) {
    allEdges.push({ id: 'e-start-0', source: 'start-1', target: nodeIds[0] })
  }

  // Template connections
  for (const conn of def.connections) {
    const srcId = nodeIds[conn.from]
    const tgtId = nodeIds[conn.to]
    if (!srcId || !tgtId) continue
    allEdges.push({
      id: `e-${conn.from}-${conn.to}`,
      source: srcId,
      target: tgtId,
      sourceHandle: conn.fromOutput,
      targetHandle: conn.toPort,
    })
  }

  // Last node → End (if not already connected)
  if (nodeIds.length > 0) {
    const lastId = nodeIds[nodeIds.length - 1]
    const hasEndConn = allEdges.some((e) => e.target === 'end-1')
    if (!hasEndConn) {
      allEdges.push({ id: 'e-last-end', source: lastId, target: 'end-1' })
    }
  }

  return { nodes: allNodes, edges: allEdges }
}
