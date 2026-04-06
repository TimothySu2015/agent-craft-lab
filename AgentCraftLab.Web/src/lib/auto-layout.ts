/**
 * Auto Layout — 使用 ELK (Eclipse Layout Kernel) 自動排版 React Flow nodes。
 *
 * 兩階段佈局策略：
 * 1. 先偵測群組 + 計算群組內部排版（得到群組尺寸）
 * 2. 把群組當成一個大方塊，與外部節點一起丟進 ELK 排版
 *
 * 這讓 ELK 知道群組的真實尺寸，避免外部節點壓在群組上。
 */
import ELK, { type ElkNode, type ElkExtendedEdge, type ElkPort } from 'elkjs/lib/elk.bundled.js'
import type { Node, Edge } from '@xyflow/react'
import type { NodeData, LayoutDirection } from '@/types/workflow'
import { NODE_REGISTRY } from '@/components/studio/nodes/registry'
import { buildGroups, type GroupDef } from './group-builder'
import { layoutGroupInternals, GROUP_PAD, GROUP_HEADER_HEIGHT, type GroupLayoutResult } from './group-layout'

const NODE_WIDTH = 280
const NODE_HEIGHT = 100

const elk = new ELK()

/** 為 ELK 節點建立 ports，方向跟隨 layout direction */
function buildPorts(nodeId: string, nodes: Node[], direction: string): ElkPort[] {
  const node = nodes.find((n) => n.id === nodeId)
  const nodeType = node?.type ?? 'agent'
  const config = NODE_REGISTRY[nodeType as keyof typeof NODE_REGISTRY]
  const inputs = config?.inputs ?? 1
  const outputs = config?.outputs ?? 1

  let actualOutputs = outputs
  if (nodeType === 'parallel') {
    const branches = ((node?.data as any)?.branches ?? '').split(',').filter(Boolean)
    actualOutputs = branches.length + 1
  }

  const isLR = direction === 'RIGHT'
  const inputSide = isLR ? 'WEST' : 'NORTH'
  const outputSide = isLR ? 'EAST' : 'SOUTH'

  const ports: ElkPort[] = []

  if (inputs > 0) {
    ports.push({
      id: `${nodeId}_input`,
      layoutOptions: { 'port.side': inputSide, 'port.index': '0' },
    })
  }

  for (let i = 0; i < actualOutputs; i++) {
    ports.push({
      id: `${nodeId}_output_${i + 1}`,
      layoutOptions: { 'port.side': outputSide, 'port.index': `${i + 1}` },
    })
  }

  return ports
}

export async function autoLayout(
  nodes: Node<NodeData>[],
  edges: Edge[],
  direction: LayoutDirection = 'LR',
): Promise<Node<NodeData>[]> {
  if (nodes.length === 0) return []

  const realNodes = nodes.filter((n) => !n.type?.endsWith('-group'))
  const elkDirection = direction === 'LR' ? 'RIGHT' : 'DOWN'

  // ─── 階段 1：偵測群組 + 計算內部排版 ───
  const groups = buildGroups(realNodes, edges)
  const groupLayouts = new Map<string, { group: GroupDef; layout: GroupLayoutResult }>()
  const membership = new Map<string, string>() // nodeId → groupId

  for (const g of groups) {
    const groupId = `group-${g.controlNodeId}`
    const layout = layoutGroupInternals(g, direction)
    groupLayouts.set(groupId, { group: g, layout })

    membership.set(g.controlNodeId, groupId)
    for (const branch of g.branches) {
      for (const id of branch.nodeIds) membership.set(id, groupId)
    }
  }

  // ─── 階段 2：建立 ELK 簡化圖（群組 = 一個大方塊） ───
  const elkChildren: ElkNode[] = []

  // 外部節點
  for (const n of realNodes) {
    if (!membership.has(n.id)) {
      elkChildren.push({
        id: n.id,
        width: NODE_WIDTH,
        height: NODE_HEIGHT,
        ports: buildPorts(n.id, realNodes, elkDirection),
        layoutOptions: { 'portConstraints': 'FIXED_SIDE' },
      })
    }
  }

  // 群組方塊（用群組內部排版算出的尺寸）
  for (const [groupId, { layout }] of groupLayouts) {
    const isLR = elkDirection === 'RIGHT'
    elkChildren.push({
      id: groupId,
      width: layout.dimensions.w,
      height: layout.dimensions.h,
      ports: [
        { id: `${groupId}_input`, layoutOptions: { 'port.side': isLR ? 'WEST' : 'NORTH', 'port.index': '0' } },
        { id: `${groupId}_output`, layoutOptions: { 'port.side': isLR ? 'EAST' : 'SOUTH', 'port.index': '1' } },
      ],
      layoutOptions: { 'portConstraints': 'FIXED_SIDE' },
    })
  }

  // 重映射邊線：群組內部的跳過，跨群組的映射到群組方塊
  const seenEdges = new Set<string>()
  const elkEdges: ElkExtendedEdge[] = []

  for (const e of edges) {
    const srcGroup = membership.get(e.source)
    const tgtGroup = membership.get(e.target)

    // 群組內部邊線 → 跳過（群組內部已自行排版）
    if (srcGroup && srcGroup === tgtGroup) continue

    // 重映射 source/target
    const source = srcGroup ?? e.source
    const target = tgtGroup ?? e.target

    // 去重（多條邊映射到同一個 group → group 連線）
    const edgeKey = `${source}→${target}`
    if (seenEdges.has(edgeKey)) continue
    seenEdges.add(edgeKey)

    // Port 映射
    const sourcePort = srcGroup
      ? `${srcGroup}_output`
      : (e.sourceHandle ? `${e.source}_${e.sourceHandle}` : `${e.source}_output_1`)
    const targetPort = tgtGroup
      ? `${tgtGroup}_input`
      : `${e.target}_input`

    elkEdges.push({ id: `elk-${edgeKey}`, sources: [sourcePort], targets: [targetPort] })
  }

  const graph: ElkNode = {
    id: 'root',
    layoutOptions: {
      'elk.algorithm': 'layered',
      'elk.direction': elkDirection,
      'elk.spacing.nodeNode': '80',
      'elk.layered.spacing.nodeNodeBetweenLayers': '120',
      'elk.spacing.edgeNode': '40',
      'elk.spacing.edgeEdge': '20',
      'elk.layered.cycleBreaking.strategy': 'INTERACTIVE',
      'elk.layered.crossingMinimization.strategy': 'LAYER_SWEEP',
      'elk.layered.nodePlacement.strategy': 'NETWORK_SIMPLEX',
      'elk.layered.nodePlacement.favorStraightEdges': 'true',
      'elk.layered.nodePlacement.bk.fixedAlignment': 'BALANCED',
      'elk.edgeRouting': 'ORTHOGONAL',
    },
    children: elkChildren,
    edges: elkEdges,
  }

  const laid = await elk.layout(graph)

  // ─── 組合結果 ───
  const posMap = new Map<string, { x: number; y: number; w?: number; h?: number }>()
  for (const child of laid.children ?? []) {
    posMap.set(child.id, { x: child.x ?? 0, y: child.y ?? 0, w: child.width, h: child.height })
  }

  const result: Node<NodeData>[] = []

  // 群組節點 + 子節點
  for (const [groupId, { group, layout }] of groupLayouts) {
    const gPos = posMap.get(groupId)
    if (!gPos) continue

    // 群組容器
    result.push({
      id: groupId,
      type: group.type,
      position: { x: gPos.x, y: gPos.y },
      data: { type: group.type as any, name: groupId } as NodeData,
      style: { width: layout.dimensions.w, height: layout.dimensions.h },
      zIndex: -1,
      selectable: true,
      focusable: false,
      draggable: true,
    })

    // 子節點（相對於群組的位置）
    for (const [nodeId, pos] of layout.positions) {
      const n = realNodes.find((r) => r.id === nodeId)
      if (n) {
        result.push({ ...n, position: pos, parentId: groupId, extent: 'parent' as const })
      }
    }
  }

  // 外部節點
  for (const n of realNodes) {
    if (!membership.has(n.id)) {
      const pos = posMap.get(n.id)
      result.push({
        ...n,
        position: pos ? { x: pos.x, y: pos.y } : n.position,
        parentId: undefined,
        extent: undefined,
      })
    }
  }

  return result
}
