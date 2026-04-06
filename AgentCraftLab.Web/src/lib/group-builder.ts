/**
 * Group Builder — 從 edges 推斷控制流程的分支範圍，產生群組定義。
 *
 * 鏡像後端邏輯：
 * - collectChainUntilStop ↔ ImperativeWorkflowStrategy.ExecuteBodyChainAsync
 * - collectBranchNodes    ↔ ParallelNodeExecutor.CollectBranchNodes (BFS)
 * - findConvergencePoint  ↔ Condition True/False 分支匯合偵測
 *
 * 群組是純前端 UI 概念，不存入 WorkflowDocument。
 */
import type { Node, Edge } from '@xyflow/react'

// ─── Types ───

export interface GroupDef {
  /** React Flow group node 的 type */
  type: 'condition-group' | 'loop-group' | 'parallel-group' | 'iteration-group'
  /** 對應的控制節點 ID（condition/loop/parallel/iteration） */
  controlNodeId: string
  /** 各分支包含的節點 ID */
  branches: GroupBranch[]
}

export interface GroupBranch {
  label: string
  nodeIds: Set<string>
}

/** 鄰接表：nodeId → [{toId, fromOutput}] */
export type AdjacencyMap = Map<string, { toId: string; fromOutput: string }[]>

// ─── 控制節點類型 ───

const CONTROL_TYPES = new Set(['condition', 'loop', 'iteration', 'parallel'])

// ─── Public API ───

/** 不應被收進任何群組的節點類型 */
const EXCLUDED_TYPES = new Set(['start', 'end'])

/**
 * 分析 nodes + edges，產生所有控制流程的群組定義。
 * 由內而外排序（巢狀的內層群組先出現）。
 */
export function buildGroups(nodes: Node[], edges: Edge[]): GroupDef[] {
  const adj = buildAdjacency(edges)
  const nodeMap = new Map(nodes.map((n) => [n.id, n]))
  const controlNodes = nodes.filter((n) => CONTROL_TYPES.has(n.type ?? ''))

  // 排序：拓撲排序中越後面的越先處理（innermost first）
  const order = topologicalOrder(nodes, edges)
  controlNodes.sort((a, b) => (order.get(b.id) ?? 0) - (order.get(a.id) ?? 0))

  const groups: GroupDef[] = []
  // 追蹤已被歸入群組的節點，避免外層群組重複包含內層群組的子節點
  const assigned = new Set<string>()

  for (const cn of controlNodes) {
    const type = cn.type as string
    const group = buildGroupForNode(cn.id, type, nodeMap, adj, assigned)
    if (group) {
      // 標記所有子節點為已歸屬
      for (const branch of group.branches) {
        for (const id of branch.nodeIds) {
          assigned.add(id)
        }
      }
      assigned.add(cn.id)
      groups.push(group)
    }
  }

  return groups
}

// ─── 群組建構（per control node） ───

function buildGroupForNode(
  controlId: string,
  type: string,
  nodeMap: Map<string, Node>,
  adj: AdjacencyMap,
  assigned: Set<string>,
): GroupDef | null {
  switch (type) {
    case 'condition':
      return buildConditionGroup(controlId, adj, nodeMap, assigned)
    case 'loop':
      return buildLoopGroup(controlId, adj, nodeMap, assigned)
    case 'iteration':
      return buildIterationGroup(controlId, adj, nodeMap, assigned)
    case 'parallel':
      return buildParallelGroup(controlId, nodeMap, adj, assigned)
    default:
      return null
  }
}

function buildConditionGroup(controlId: string, adj: AdjacencyMap, nodeMap: Map<string, Node>, assigned: Set<string>): GroupDef {
  const trueBranch = collectBranchNodes(controlId, 'output_1', adj, nodeMap)
  const falseBranch = collectBranchNodes(controlId, 'output_2', adj, nodeMap)

  // 找匯合點，移除匯合點本身 + 匯合點之後的所有下游節點
  const convergence = findConvergencePoint(trueBranch, falseBranch)
  if (convergence) {
    const downstream = collectBranchNodes(convergence, 'output_1', adj, nodeMap)
    downstream.add(convergence)
    for (const id of downstream) {
      trueBranch.delete(id)
      falseBranch.delete(id)
    }
  }

  // 移除已被內層群組佔用的節點
  removeAssigned(trueBranch, assigned)
  removeAssigned(falseBranch, assigned)

  return {
    type: 'condition-group',
    controlNodeId: controlId,
    branches: [
      { label: 'True', nodeIds: trueBranch },
      { label: 'False', nodeIds: falseBranch },
    ],
  }
}

function buildLoopGroup(controlId: string, adj: AdjacencyMap, nodeMap: Map<string, Node>, assigned: Set<string>): GroupDef {
  const body = collectChainUntilStop(controlId, 'output_1', controlId, adj, nodeMap)
  removeAssigned(body, assigned)
  return {
    type: 'loop-group',
    controlNodeId: controlId,
    branches: [{ label: 'Body', nodeIds: body }],
  }
}

function buildIterationGroup(controlId: string, adj: AdjacencyMap, nodeMap: Map<string, Node>, assigned: Set<string>): GroupDef {
  const body = collectChainUntilStop(controlId, 'output_1', controlId, adj, nodeMap)
  removeAssigned(body, assigned)
  return {
    type: 'iteration-group',
    controlNodeId: controlId,
    branches: [{ label: 'Body', nodeIds: body }],
  }
}

function buildParallelGroup(
  controlId: string,
  nodeMap: Map<string, Node>,
  adj: AdjacencyMap,
  assigned: Set<string>,
): GroupDef {
  const node = nodeMap.get(controlId)
  const branchesStr = (node?.data as any)?.branches ?? ''
  const branchNames = typeof branchesStr === 'string'
    ? branchesStr.split(',').filter(Boolean).map((s: string) => s.trim())
    : []
  const branchCount = branchNames.length || 2

  // Done port 連接的節點是分支的邊界（stop boundary）
  const donePort = `output_${branchCount + 1}`
  const doneTarget = getNextNodeId(adj, controlId, donePort)

  const branches: GroupBranch[] = []
  for (let i = 0; i < branchCount; i++) {
    const portName = `output_${i + 1}`
    // 鏡像後端 ParallelNodeExecutor：沿 output_1 線性走訪，遇到 Done target 或控制節點自身就停
    const branchNodes = collectParallelBranch(controlId, portName, doneTarget, adj, nodeMap)
    removeAssigned(branchNodes, assigned)
    branches.push({
      label: branchNames[i] ?? `Branch ${i + 1}`,
      nodeIds: branchNodes,
    })
  }

  return {
    type: 'parallel-group',
    controlNodeId: controlId,
    branches,
  }
}

// ─── 子演算法 ───

/**
 * Parallel 分支走訪 — 沿 output_1 線性走，遇到 doneTarget 或 controlId 就停。
 * 鏡像後端 ParallelNodeExecutor 的分支走訪邏輯。
 * 不收集 start/end 類型節點。
 */
function collectParallelBranch(
  controlId: string,
  outputPort: string,
  doneTarget: string | null,
  adj: AdjacencyMap,
  nodeMap: Map<string, Node>,
): Set<string> {
  const collected = new Set<string>()
  let nodeId = getNextNodeId(adj, controlId, outputPort)

  while (nodeId != null && nodeId !== controlId && nodeId !== doneTarget) {
    if (collected.has(nodeId)) break
    const nodeType = nodeMap.get(nodeId)?.type
    if (nodeType && EXCLUDED_TYPES.has(nodeType)) break
    collected.add(nodeId)
    nodeId = getNextNodeId(adj, nodeId, 'output_1')
  }

  return collected
}

/**
 * 沿 output_1 鏈式走訪，直到遇到 stopId 或走到底。
 * 鏡像後端 ExecuteBodyChainAsync。
 * 用於 Loop / Iteration 的 body 範圍偵測。
 * 不收集 start/end 類型節點。
 */
export function collectChainUntilStop(
  sourceId: string,
  outputPort: string,
  stopId: string,
  adj: AdjacencyMap,
  nodeMap?: Map<string, Node>,
): Set<string> {
  const collected = new Set<string>()
  let nodeId = getNextNodeId(adj, sourceId, outputPort)

  while (nodeId != null && nodeId !== stopId) {
    if (collected.has(nodeId)) break
    if (nodeMap) {
      const nodeType = nodeMap.get(nodeId)?.type
      if (nodeType && EXCLUDED_TYPES.has(nodeType)) break
    }
    collected.add(nodeId)
    nodeId = getNextNodeId(adj, nodeId, 'output_1')
  }

  return collected
}

/**
 * BFS 走訪分支所有可達節點（不跨過控制節點的 stopId）。
 * 鏡像後端 ParallelNodeExecutor.CollectBranchNodes。
 * 用於 Condition 的分支範圍偵測。
 * 不收集 start/end 類型節點。
 */
export function collectBranchNodes(
  sourceId: string,
  outputPort: string,
  adj: AdjacencyMap,
  nodeMap?: Map<string, Node>,
): Set<string> {
  const collected = new Set<string>()
  const startId = getNextNodeId(adj, sourceId, outputPort)
  if (!startId) return collected

  const queue = [startId]

  while (queue.length > 0) {
    const nodeId = queue.shift()!
    if (nodeId === sourceId) continue
    if (collected.has(nodeId)) continue
    if (nodeMap) {
      const nodeType = nodeMap.get(nodeId)?.type
      if (nodeType && EXCLUDED_TYPES.has(nodeType)) continue
    }
    collected.add(nodeId)

    const edges = adj.get(nodeId)
    if (edges) {
      for (const { toId } of edges) {
        if (toId !== sourceId && !collected.has(toId)) {
          queue.push(toId)
        }
      }
    }
  }

  return collected
}

/**
 * 找出兩個分支的匯合節點 — 第一個同時出現在兩個分支中的節點。
 * Condition 專用。
 */
export function findConvergencePoint(
  branchA: Set<string>,
  branchB: Set<string>,
): string | null {
  // 交集：同時存在於兩個分支的節點
  for (const id of branchA) {
    if (branchB.has(id)) return id
  }
  return null
}

// ─── 工具方法 ───

/**
 * 建立鄰接表。鏡像後端 WorkflowGraphHelper.GetNextNodeId 的資料結構。
 */
export function buildAdjacency(edges: Edge[]): AdjacencyMap {
  const adj: AdjacencyMap = new Map()
  for (const e of edges) {
    const list = adj.get(e.source) ?? []
    list.push({ toId: e.target, fromOutput: e.sourceHandle ?? 'output_1' })
    adj.set(e.source, list)
  }
  return adj
}

/**
 * 根據鄰接表取得指定 output port 的下一個節點 ID。
 * 鏡像後端 WorkflowGraphHelper.GetNextNodeId。
 */
export function getNextNodeId(
  adj: AdjacencyMap,
  nodeId: string,
  outputPort: string,
): string | null {
  const edges = adj.get(nodeId)
  if (!edges) return null

  // 精確匹配 port
  const match = edges.find((e) => e.fromOutput === outputPort)
  if (match) return match.toId

  // Fallback：output_1 可以 fallback 到第一條邊
  if (outputPort === 'output_1' && edges.length > 0) return edges[0].toId

  return null
}

/** 簡易拓撲排序，回傳 nodeId → order（越大越靠後） */
function topologicalOrder(nodes: Node[], edges: Edge[]): Map<string, number> {
  const inDegree = new Map<string, number>()
  const adj = new Map<string, string[]>()

  for (const n of nodes) {
    inDegree.set(n.id, 0)
    adj.set(n.id, [])
  }

  for (const e of edges) {
    adj.get(e.source)?.push(e.target)
    inDegree.set(e.target, (inDegree.get(e.target) ?? 0) + 1)
  }

  const queue = [...inDegree.entries()].filter(([, d]) => d === 0).map(([id]) => id)
  const order = new Map<string, number>()
  let idx = 0

  while (queue.length > 0) {
    const nodeId = queue.shift()!
    order.set(nodeId, idx++)
    for (const next of adj.get(nodeId) ?? []) {
      const d = (inDegree.get(next) ?? 1) - 1
      inDegree.set(next, d)
      if (d === 0) queue.push(next)
    }
  }

  // 有環的節點（Loop 回頭線）給最大 order
  for (const n of nodes) {
    if (!order.has(n.id)) order.set(n.id, idx++)
  }

  return order
}

/** 從集合中移除已被歸屬的節點 */
function removeAssigned(nodeIds: Set<string>, assigned: Set<string>): void {
  for (const id of nodeIds) {
    if (assigned.has(id)) nodeIds.delete(id)
  }
}
