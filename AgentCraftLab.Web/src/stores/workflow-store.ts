import { create } from 'zustand'
import {
  applyNodeChanges,
  applyEdgeChanges,
  addEdge,
  type Node,
  type Edge,
  type OnNodesChange,
  type OnEdgesChange,
  type Connection,
} from '@xyflow/react'
import { NODE_REGISTRY } from '@/components/studio/nodes/registry'
import { useCredentialStore } from '@/stores/credential-store'
import { getModelsForProvider } from '@/lib/providers'
import { autoLayout } from '@/lib/auto-layout'
import { buildGroups } from '@/lib/group-builder'
import { layoutGroupInternals, GROUP_PAD, GROUP_HEADER_HEIGHT } from '@/lib/group-layout'
import type { NodeData, NodeType, LayoutDirection, WorkflowVariable } from '@/types/workflow'

export interface WorkflowSettings {
  type: 'auto' | 'sequential' | 'concurrent' | 'handoff' | 'imperative'
  maxTurns: number
  terminationStrategy?: 'none' | 'maxturns' | 'keyword' | 'combined'
  terminationKeyword?: string
  aggregatorStrategy?: 'default' | 'custom'
  contextPassing?: 'previous-only' | 'with-original' | 'accumulate'
  hooks?: Record<string, any>
  variables?: WorkflowVariable[]
}

interface HistoryEntry {
  nodes: Node<NodeData>[];
  edges: Edge[];
}

interface WorkflowState {
  nodes: Node<NodeData>[];
  edges: Edge[];
  selectedNodeId: string | null;
  nodeCounter: number;
  layoutVersion: number;
  layoutDirection: LayoutDirection;
  workflowSettings: WorkflowSettings;
  /** 每次 setWorkflow 時遞增，用於重置 CopilotKit chat session */
  chatSessionId: number;

  // React Flow callbacks
  onNodesChange: OnNodesChange;
  onEdgesChange: OnEdgesChange;
  onConnect: (connection: Connection) => void;

  // Node actions
  addNode: (type: NodeType, position: { x: number; y: number }) => void;
  updateNodeData: (id: string, data: Partial<NodeData>) => void;
  removeSelected: () => void;
  duplicateSelected: () => void;
  setSelectedNode: (id: string | null) => void;

  // Workflow actions
  setWorkflow: (nodes: Node<NodeData>[], edges: Edge[]) => void;
  updateSettings: (settings: Partial<WorkflowSettings>) => void;
  clear: () => void;
  layout: (direction?: LayoutDirection) => Promise<void>;

  // Chat session
  resetChatSession: () => void;

  // Undo/Redo
  undo: () => void;
  redo: () => void;
  canUndo: () => boolean;
  canRedo: () => boolean;
}

const MAX_HISTORY = 50

const undoStack: HistoryEntry[] = []
const redoStack: HistoryEntry[] = []

function pushHistory(nodes: Node<NodeData>[], edges: Edge[]) {
  undoStack.push({ nodes: structuredClone(nodes), edges: structuredClone(edges) })
  if (undoStack.length > MAX_HISTORY) undoStack.shift()
  redoStack.length = 0
}

// Debounced version — 避免每次 keystroke 都 push（500ms 內合併）
let debounceTimer: ReturnType<typeof setTimeout> | null = null
function debouncedPushHistory(nodes: Node<NodeData>[], edges: Edge[]) {
  if (debounceTimer) return // 已經排程，跳過
  debounceTimer = setTimeout(() => { debounceTimer = null }, 500)
  pushHistory(nodes, edges)
}

/**
 * rebuildGroups — 從 nodes + edges 推斷控制流程群組，產生群組節點 + 設定 parentId。
 * 群組節點不持久化，每次載入/undo/redo/layout 後自動重建。
 * 排版邏輯委託給 layoutGroupInternals（group-layout.ts）。
 */
function rebuildGroups(nodes: Node<NodeData>[], edges: Edge[], dir?: LayoutDirection): Node<NodeData>[] {
  const realNodes = nodes
    .filter((n) => !n.type?.endsWith('-group'))
    .map((n) => ({ ...n, parentId: undefined, extent: undefined }))

  const groups = buildGroups(realNodes, edges)
  if (groups.length === 0) return realNodes

  const direction = dir ?? 'LR'
  const membership = new Map<string, string>()
  const result: Node<NodeData>[] = []

  for (const g of groups) {
    const groupId = `group-${g.controlNodeId}`
    membership.set(g.controlNodeId, groupId)
    for (const branch of g.branches) {
      for (const id of branch.nodeIds) membership.set(id, groupId)
    }

    const { positions, dimensions } = layoutGroupInternals(g, direction)
    const ctrlNode = realNodes.find((n) => n.id === g.controlNodeId)
    const gx = (ctrlNode?.position.x ?? 0) - GROUP_PAD
    const gy = (ctrlNode?.position.y ?? 0) - GROUP_HEADER_HEIGHT

    // 群組容器節點
    result.push({
      id: groupId,
      type: g.type,
      position: { x: gx, y: gy },
      data: { type: g.type as any, name: groupId } as NodeData,
      style: { width: dimensions.w, height: dimensions.h },
      zIndex: -1,
      selectable: true,
      focusable: false,
      draggable: true,
    })

    // 群組內的子節點（用排版位置）
    for (const [nodeId, pos] of positions) {
      const n = realNodes.find((r) => r.id === nodeId)
      if (n) {
        result.push({ ...n, position: pos, parentId: groupId, extent: 'parent' as const })
      }
    }
  }

  // 群組外的節點保持原位
  for (const n of realNodes) {
    if (!membership.has(n.id)) result.push(n)
  }

  return result
}

const defaultNodes: Node<NodeData>[] = [
  { id: 'start-1', type: 'start', position: { x: 50, y: 200 }, data: { type: 'start', name: 'Start' } },
  {
    id: 'agent-1', type: 'agent', position: { x: 300, y: 160 },
    data: {
      type: 'agent', name: 'Agent-1', instructions: '', model: 'gpt-4o', provider: 'openai',
      endpoint: '', deploymentName: '', historyProvider: 'none', maxMessages: 20,
      middleware: '', tools: [], skills: [],
    },
  },
  { id: 'end-1', type: 'end', position: { x: 600, y: 200 }, data: { type: 'end', name: 'End' } },
]

const defaultEdges: Edge[] = [
  { id: 'e-start-agent', source: 'start-1', target: 'agent-1' },
  { id: 'e-agent-end', source: 'agent-1', target: 'end-1' },
]

export const useWorkflowStore = create<WorkflowState>((set, get) => ({
  nodes: defaultNodes,
  edges: defaultEdges,
  selectedNodeId: null,
  nodeCounter: 1,
  layoutVersion: 0,
  layoutDirection: 'LR' as LayoutDirection,
  chatSessionId: 0,
  workflowSettings: { type: 'auto', maxTurns: 10 },

  onNodesChange: (changes) => {
    set((s) => ({ nodes: applyNodeChanges(changes, s.nodes) as Node<NodeData>[] }))
  },

  onEdgesChange: (changes) => {
    set((s) => ({ edges: applyEdgeChanges(changes, s.edges) }))
  },

  onConnect: (connection) => {
    const { nodes, edges } = get()
    pushHistory(nodes, edges)
    const sourceNode = nodes.find((n) => n.id === connection.source)
    const branchCount = sourceNode?.type === 'parallel'
      ? ((sourceNode.data as any)?.branches ?? '').split(',').filter(Boolean).length
      : undefined
    const esl = getEdgeStyleAndLabel(connection.sourceHandle, sourceNode?.type, branchCount)
    const edge = esl.label
      ? { ...connection, label: esl.label, style: esl.style, animated: esl.animated, labelStyle: { fill: 'var(--muted-foreground)', fontSize: 10 } }
      : { ...connection, style: esl.style }
    set((s) => {
      const newEdges = addEdge(edge, s.edges)
      return { nodes: rebuildGroups(s.nodes, newEdges, get().layoutDirection), edges: newEdges }
    })
  },

  addNode: (type, position) => {
    const { nodes, edges, nodeCounter } = get()
    pushHistory(nodes, edges)

    const config = NODE_REGISTRY[type]
    if (!config) return

    const nextCount = nodeCounter + 1
    const id = `${type}-${nextCount}`
    const name = `${config.labelKey.split('.').pop()}-${nextCount}`
    const data = config.defaultData(name)

    // Agent/Autonomous 自動選有 Key 的 Provider
    if (data.type === 'agent' || data.type === 'autonomous') {
      const creds = useCredentialStore.getState().credentials
      const configured = Object.entries(creds).find(([, v]) => v.apiKey)
      if (configured) {
        const [provider, entry] = configured
        data.provider = provider
        data.model = entry.model || getModelsForProvider(provider)[0] || 'gpt-4o-mini'
      }
    }

    const newNode: Node<NodeData> = { id, type, position, data }
    set({ nodes: [...nodes, newNode], nodeCounter: nextCount })
  },

  updateNodeData: (id, partial) => {
    const { nodes, edges } = get()
    debouncedPushHistory(nodes, edges)
    set({
      nodes: nodes.map((n) =>
        n.id === id ? { ...n, data: { ...n.data, ...partial } as NodeData } : n,
      ),
    })
  },

  removeSelected: () => {
    const { nodes, edges, selectedNodeId } = get()
    if (!selectedNodeId) return
    const node = nodes.find((n) => n.id === selectedNodeId)
    if (node?.type === 'start' || node?.type === 'end') return

    pushHistory(nodes, edges)
    const groupId = `group-${selectedNodeId}`
    const newEdges = edges.filter((e) => e.source !== selectedNodeId && e.target !== selectedNodeId)
    // 刪除控制節點時，解除子節點 parentId 並移除群組 wrapper
    const newNodes = nodes
      .filter((n) => n.id !== selectedNodeId && n.id !== groupId)
      .map((n) => n.parentId === groupId ? { ...n, parentId: undefined, extent: undefined } : n)
    set({
      nodes: rebuildGroups(newNodes, newEdges, get().layoutDirection),
      edges: newEdges,
      selectedNodeId: null,
    })
  },

  duplicateSelected: () => {
    const { nodes, edges, selectedNodeId, nodeCounter } = get()
    if (!selectedNodeId) return
    const node = nodes.find((n) => n.id === selectedNodeId)
    if (!node || node.type === 'start' || node.type === 'end') return

    pushHistory(nodes, edges)
    const nextCount = nodeCounter + 1
    const newId = `${node.type}-${nextCount}`
    const newNode: Node<NodeData> = {
      ...node,
      id: newId,
      position: { x: node.position.x + 40, y: node.position.y + 40 },
      data: { ...node.data, name: `${(node.data as NodeData).name}-copy` } as NodeData,
      selected: false,
    }
    set({ nodes: [...nodes, newNode], nodeCounter: nextCount, selectedNodeId: newId })
  },

  setSelectedNode: (id) => set({ selectedNodeId: id }),

  setWorkflow: (nodes, edges) => {
    const state = get()
    pushHistory(state.nodes, state.edges)
    // 計算載入 workflow 中最大的 node counter，避免 ID 碰撞
    const maxId = nodes.reduce((max, n) => {
      const match = n.id.match(/-(\d+)$/)
      return match ? Math.max(max, parseInt(match[1])) : max
    }, 0)
    const grouped = rebuildGroups(nodes, edges, get().layoutDirection)
    set({ nodes: grouped, edges, selectedNodeId: null, layoutVersion: state.layoutVersion + 1, nodeCounter: Math.max(state.nodeCounter, maxId), chatSessionId: state.chatSessionId + 1 })
  },

  updateSettings: (partial) => {
    set((s) => ({ workflowSettings: { ...s.workflowSettings, ...partial } }))
  },

  resetChatSession: () => {
    set((s) => ({ chatSessionId: s.chatSessionId + 1 }))
  },

  clear: () => {
    undoStack.length = 0
    redoStack.length = 0
    set((s) => ({ nodes: defaultNodes, edges: defaultEdges, selectedNodeId: null, nodeCounter: 1, layoutVersion: s.layoutVersion + 1, workflowSettings: { type: 'auto', maxTurns: 10 } }))
  },

  layout: async (direction?: LayoutDirection) => {
    const { nodes, edges, layoutVersion, layoutDirection } = get()
    pushHistory(nodes, edges)
    const dir = direction ?? layoutDirection
    try {
      const laid = await autoLayout(nodes, edges, dir)
      set({ nodes: laid, edges, layoutVersion: layoutVersion + 1, layoutDirection: dir })
    } catch (err) {
      console.warn('[layout] ELK layout failed, updating direction only:', err)
      set({ layoutDirection: dir })
    }
  },

  undo: () => {
    const entry = undoStack.pop()
    if (!entry) return
    const { nodes, edges } = get()
    redoStack.push({ nodes: structuredClone(nodes), edges: structuredClone(edges) })
    const grouped = rebuildGroups(entry.nodes, entry.edges, get().layoutDirection)
    set({ nodes: grouped, edges: entry.edges })
  },

  redo: () => {
    const entry = redoStack.pop()
    if (!entry) return
    const { nodes, edges } = get()
    undoStack.push({ nodes: structuredClone(nodes), edges: structuredClone(edges) })
    const grouped = rebuildGroups(entry.nodes, entry.edges, get().layoutDirection)
    set({ nodes: grouped, edges: entry.edges })
  },

  canUndo: () => undoStack.length > 0,
  canRedo: () => redoStack.length > 0,
}))

/** 根據 sourceHandle + 節點類型產生連線 label + 樣式 */
interface EdgeStyleResult {
  label?: string
  style: Record<string, string | number | undefined>
  animated?: boolean
}

const PARALLEL_COLORS = ['#06b6d4', '#8b5cf6', '#f59e0b', '#ec4899', '#22d3ee', '#a855f7']

function edgeStyle(stroke: string, dash?: string): EdgeStyleResult['style'] {
  return dash ? { stroke, strokeWidth: 2, strokeDasharray: dash } : { stroke, strokeWidth: 2 }
}

function getEdgeStyleAndLabel(
  handle: string | null | undefined,
  nodeType: string | undefined,
  branchCount?: number,
): EdgeStyleResult {
  const defaultStyle = edgeStyle('var(--border)')
  if (!handle || !nodeType) return { style: defaultStyle }

  switch (nodeType) {
    case 'condition':
      if (handle === 'output_1') return { label: 'True', style: edgeStyle('#22c55e') }
      if (handle === 'output_2') return { label: 'False', style: edgeStyle('#ef4444') }
      break
    case 'loop':
      if (handle === 'output_1') return { label: 'Body', style: edgeStyle('#f59e0b', '5,5'), animated: true }
      if (handle === 'output_2') return { label: 'Exit', style: defaultStyle }
      break
    case 'iteration':
      if (handle === 'output_1') return { label: 'Body', style: edgeStyle('#14b8a6', '5,5') }
      if (handle === 'output_2') return { label: 'Done', style: defaultStyle }
      break
    case 'parallel': {
      const portNum = parseInt(handle.replace('output_', ''))
      const donePort = (branchCount ?? 2) + 1
      if (portNum === donePort) return { label: 'Done', style: defaultStyle }
      const color = PARALLEL_COLORS[(portNum - 1) % PARALLEL_COLORS.length]
      return { label: `Branch ${portNum}`, style: edgeStyle(color) }
    }
    case 'human':
      if (handle === 'output_1') return { label: 'Approve', style: edgeStyle('#22c55e') }
      if (handle === 'output_2') return { label: 'Reject', style: edgeStyle('#ef4444') }
      break
    case 'router': {
      const routeNum = parseInt(handle.replace('output_', ''))
      const color = PARALLEL_COLORS[(routeNum - 1) % PARALLEL_COLORS.length]
      return { label: `Route ${routeNum}`, style: edgeStyle(color) }
    }
  }

  return { style: defaultStyle }
}
