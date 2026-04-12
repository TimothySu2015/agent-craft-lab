import { describe, it, expect, beforeEach, vi } from 'vitest'
import { useWorkflowStore } from '../workflow-store'
import type { Node, Edge } from '@xyflow/react'
import type { NodeData } from '@/types/workflow'

// Mock dependencies that workflow-store imports
vi.mock('@/components/studio/nodes/registry', () => ({
  NODE_REGISTRY: {
    agent: {
      type: 'agent',
      labelKey: 'node.agent',
      defaultData: (name: string) => ({
        type: 'agent', name, instructions: '', model: 'gpt-4o', provider: 'openai',
        endpoint: '', deploymentName: '', historyProvider: 'none', maxMessages: 20,
        middleware: '', tools: [], skills: [],
      }),
    },
    condition: {
      type: 'condition',
      labelKey: 'node.condition',
      defaultData: (name: string) => ({
        type: 'condition', name, conditionType: 'contains', conditionExpression: '', maxIterations: 5,
      }),
    },
    start: {
      type: 'start',
      labelKey: 'node.start',
      defaultData: (name: string) => ({ type: 'start', name }),
    },
    end: {
      type: 'end',
      labelKey: 'node.end',
      defaultData: (name: string) => ({ type: 'end', name }),
    },
  },
}))

vi.mock('@/stores/credential-store', () => ({
  useCredentialStore: {
    getState: () => ({ credentials: {} }),
  },
}))

vi.mock('@/lib/providers', () => ({
  getModelsForProvider: () => ['gpt-4o'],
}))

vi.mock('@/lib/auto-layout', () => ({
  autoLayout: async (nodes: Node<NodeData>[]) => nodes.map((n, i) => ({ ...n, position: { x: i * 200, y: 0 } })),
}))

describe('useWorkflowStore', () => {
  beforeEach(() => {
    // Reset store to default state
    useWorkflowStore.getState().clear()
  })

  describe('initial state', () => {
    it('has default nodes (start, agent, end)', () => {
      const { nodes } = useWorkflowStore.getState()
      expect(nodes).toHaveLength(3)
      expect(nodes.map((n) => n.type)).toEqual(['start', 'agent', 'end'])
    })

    it('has default edges connecting start → agent → end', () => {
      const { edges } = useWorkflowStore.getState()
      expect(edges).toHaveLength(2)
      expect(edges[0].source).toBe('start-1')
      expect(edges[0].target).toBe('agent-1')
      expect(edges[1].source).toBe('agent-1')
      expect(edges[1].target).toBe('end-1')
    })

    it('has default settings', () => {
      const { workflowSettings } = useWorkflowStore.getState()
      expect(workflowSettings.type).toBe('auto')
      expect(workflowSettings.maxTurns).toBe(10)
    })
  })

  describe('addNode', () => {
    it('adds a new agent node', () => {
      const store = useWorkflowStore.getState()
      store.addNode('agent', { x: 400, y: 200 })

      const { nodes } = useWorkflowStore.getState()
      expect(nodes).toHaveLength(4)
      const added = nodes[3]
      expect(added.type).toBe('agent')
      expect(added.data.type).toBe('agent')
      expect(added.position).toEqual({ x: 400, y: 200 })
    })

    it('increments node counter for unique IDs', () => {
      const store = useWorkflowStore.getState()
      store.addNode('agent', { x: 0, y: 0 })
      store.addNode('agent', { x: 0, y: 0 })

      const { nodes } = useWorkflowStore.getState()
      const agentNodes = nodes.filter((n) => n.type === 'agent')
      const ids = agentNodes.map((n) => n.id)
      // All IDs unique
      expect(new Set(ids).size).toBe(ids.length)
    })

    it('does nothing for unknown node type', () => {
      const store = useWorkflowStore.getState()
      store.addNode('unknown' as any, { x: 0, y: 0 })

      const { nodes } = useWorkflowStore.getState()
      expect(nodes).toHaveLength(3) // unchanged
    })
  })

  describe('updateNodeData', () => {
    it('updates data for a specific node', () => {
      const store = useWorkflowStore.getState()
      store.updateNodeData('agent-1', { instructions: 'Be concise.' })

      const { nodes } = useWorkflowStore.getState()
      const agent = nodes.find((n) => n.id === 'agent-1')
      expect((agent?.data as any).instructions).toBe('Be concise.')
    })

    it('preserves other data fields', () => {
      const store = useWorkflowStore.getState()
      store.updateNodeData('agent-1', { instructions: 'New' })

      const { nodes } = useWorkflowStore.getState()
      const agent = nodes.find((n) => n.id === 'agent-1')
      expect((agent?.data as any).model?.model).toBe('gpt-4o')
    })
  })

  describe('removeSelected', () => {
    it('removes selected node and its edges', () => {
      const store = useWorkflowStore.getState()
      store.setSelectedNode('agent-1')
      store.removeSelected()

      const { nodes, edges, selectedNodeId } = useWorkflowStore.getState()
      expect(nodes.find((n) => n.id === 'agent-1')).toBeUndefined()
      expect(edges.filter((e) => e.source === 'agent-1' || e.target === 'agent-1')).toHaveLength(0)
      expect(selectedNodeId).toBeNull()
    })

    it('does not remove start node', () => {
      const store = useWorkflowStore.getState()
      store.setSelectedNode('start-1')
      store.removeSelected()

      const { nodes } = useWorkflowStore.getState()
      expect(nodes.find((n) => n.id === 'start-1')).toBeDefined()
    })

    it('does not remove end node', () => {
      const store = useWorkflowStore.getState()
      store.setSelectedNode('end-1')
      store.removeSelected()

      const { nodes } = useWorkflowStore.getState()
      expect(nodes.find((n) => n.id === 'end-1')).toBeDefined()
    })

    it('does nothing when no node is selected', () => {
      const store = useWorkflowStore.getState()
      store.removeSelected()

      const { nodes } = useWorkflowStore.getState()
      expect(nodes).toHaveLength(3)
    })
  })

  describe('duplicateSelected', () => {
    it('creates a copy with offset position', () => {
      const store = useWorkflowStore.getState()
      store.setSelectedNode('agent-1')
      store.duplicateSelected()

      const { nodes, selectedNodeId } = useWorkflowStore.getState()
      expect(nodes).toHaveLength(4)
      const copy = nodes[3]
      expect(copy.type).toBe('agent')
      expect((copy.data as any).name).toContain('-copy')
      // Position offset
      const original = nodes.find((n) => n.id === 'agent-1')!
      expect(copy.position.x).toBe(original.position.x + 40)
      expect(copy.position.y).toBe(original.position.y + 40)
      // Selection moves to copy
      expect(selectedNodeId).toBe(copy.id)
    })

    it('does not duplicate start/end nodes', () => {
      const store = useWorkflowStore.getState()
      store.setSelectedNode('start-1')
      store.duplicateSelected()

      const { nodes } = useWorkflowStore.getState()
      expect(nodes).toHaveLength(3)
    })
  })

  describe('setWorkflow', () => {
    it('replaces all nodes and edges', () => {
      const newNodes: Node<NodeData>[] = [
        { id: 'start-1', type: 'start', position: { x: 0, y: 0 }, data: { type: 'start', name: 'Start' } },
        { id: 'agent-10', type: 'agent', position: { x: 100, y: 0 }, data: { type: 'agent', name: 'New' } as any },
        { id: 'end-1', type: 'end', position: { x: 200, y: 0 }, data: { type: 'end', name: 'End' } },
      ]
      const newEdges: Edge[] = [{ id: 'e1', source: 'start-1', target: 'agent-10' }]

      const store = useWorkflowStore.getState()
      store.setWorkflow(newNodes, newEdges)

      const { nodes, edges } = useWorkflowStore.getState()
      expect(nodes).toEqual(newNodes)
      expect(edges).toEqual(newEdges)
    })

    it('updates nodeCounter to prevent ID collision', () => {
      const newNodes: Node<NodeData>[] = [
        { id: 'agent-42', type: 'agent', position: { x: 0, y: 0 }, data: { type: 'agent', name: 'A' } as any },
      ]
      const store = useWorkflowStore.getState()
      store.setWorkflow(newNodes, [])

      const { nodeCounter } = useWorkflowStore.getState()
      expect(nodeCounter).toBeGreaterThanOrEqual(42)
    })
  })

  describe('updateSettings', () => {
    it('partially updates workflow settings', () => {
      const store = useWorkflowStore.getState()
      store.updateSettings({ type: 'handoff', maxTurns: 20 })

      const { workflowSettings } = useWorkflowStore.getState()
      expect(workflowSettings.type).toBe('handoff')
      expect(workflowSettings.maxTurns).toBe(20)
    })
  })

  describe('clear', () => {
    it('resets to default state', () => {
      const store = useWorkflowStore.getState()
      store.addNode('agent', { x: 0, y: 0 })
      store.updateSettings({ type: 'concurrent' })
      store.clear()

      const { nodes, edges, nodeCounter, workflowSettings } = useWorkflowStore.getState()
      expect(nodes).toHaveLength(3)
      expect(edges).toHaveLength(2)
      expect(nodeCounter).toBe(1)
      expect(workflowSettings.type).toBe('auto')
    })
  })

  describe('undo/redo', () => {
    it('undoes addNode', () => {
      const store = useWorkflowStore.getState()
      store.addNode('agent', { x: 0, y: 0 })
      expect(useWorkflowStore.getState().nodes).toHaveLength(4)

      store.undo()
      expect(useWorkflowStore.getState().nodes).toHaveLength(3)
    })

    it('redoes after undo', () => {
      const store = useWorkflowStore.getState()
      store.addNode('agent', { x: 0, y: 0 })
      store.undo()
      expect(useWorkflowStore.getState().nodes).toHaveLength(3)

      store.redo()
      expect(useWorkflowStore.getState().nodes).toHaveLength(4)
    })

    it('undoes removeSelected', () => {
      const store = useWorkflowStore.getState()
      store.setSelectedNode('agent-1')
      store.removeSelected()
      expect(useWorkflowStore.getState().nodes).toHaveLength(2)

      store.undo()
      expect(useWorkflowStore.getState().nodes).toHaveLength(3)
    })

    it('canUndo/canRedo reflect stack state', () => {
      const store = useWorkflowStore.getState()
      expect(store.canUndo()).toBe(false)
      expect(store.canRedo()).toBe(false)

      store.addNode('agent', { x: 0, y: 0 })
      expect(useWorkflowStore.getState().canUndo()).toBe(true)

      useWorkflowStore.getState().undo()
      expect(useWorkflowStore.getState().canRedo()).toBe(true)
    })

    it('clears redo stack after new action', () => {
      const store = useWorkflowStore.getState()
      store.addNode('agent', { x: 0, y: 0 })
      store.undo()
      expect(useWorkflowStore.getState().canRedo()).toBe(true)

      // New action clears redo
      useWorkflowStore.getState().addNode('condition', { x: 0, y: 0 })
      expect(useWorkflowStore.getState().canRedo()).toBe(false)
    })
  })

  describe('layout', () => {
    it('applies auto-layout and increments layoutVersion', async () => {
      const store = useWorkflowStore.getState()
      const prevVersion = store.layoutVersion
      await store.layout()

      const { layoutVersion } = useWorkflowStore.getState()
      expect(layoutVersion).toBe(prevVersion + 1)
    })
  })
})
