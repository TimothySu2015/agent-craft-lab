import { describe, it, expect, beforeEach, vi } from 'vitest'
import { useCustomTemplatesStore } from '../custom-templates-store'
import type { Node, Edge } from '@xyflow/react'
import type { NodeData } from '@/types/workflow'

// Mock api — 預設後端不可用，走 localStorage fallback
vi.mock('@/lib/api', () => ({
  api: {
    templates: {
      list: vi.fn().mockRejectedValue(new Error('offline')),
      create: vi.fn().mockRejectedValue(new Error('offline')),
      delete: vi.fn().mockRejectedValue(new Error('offline')),
    },
  },
}))

const sampleNodes: Node<NodeData>[] = [
  { id: 'start-1', type: 'start', position: { x: 0, y: 0 }, data: { type: 'start', name: 'Start' } },
  { id: 'agent-1', type: 'agent', position: { x: 200, y: 0 }, data: { type: 'agent', name: 'A' } as NodeData },
]
const sampleEdges: Edge[] = [{ id: 'e1', source: 'start-1', target: 'agent-1' }]

describe('useCustomTemplatesStore', () => {
  beforeEach(() => {
    useCustomTemplatesStore.setState({ templates: [] })
  })

  describe('addTemplate', () => {
    it('adds a template with generated ID and metadata (fallback)', async () => {
      vi.spyOn(Date, 'now').mockReturnValue(1234567890)

      await useCustomTemplatesStore.getState().addTemplate('My Template', 'A description', sampleNodes, sampleEdges)

      const { templates } = useCustomTemplatesStore.getState()
      expect(templates).toHaveLength(1)
      expect(templates[0].id).toBe('custom-1234567890')
      expect(templates[0].name).toBe('My Template')
      expect(templates[0].description).toBe('A description')
      expect(templates[0].createdAt).toBeTruthy()

      vi.restoreAllMocks()
    })

    it('deep clones nodes and edges', async () => {
      await useCustomTemplatesStore.getState().addTemplate('Test', '', sampleNodes, sampleEdges)

      const { templates } = useCustomTemplatesStore.getState()
      expect(templates[0].nodes).not.toBe(sampleNodes)
      expect(templates[0].edges).not.toBe(sampleEdges)
      expect(templates[0].nodes).toEqual(sampleNodes)
    })

    it('prepends new templates (newest first)', async () => {
      const store = useCustomTemplatesStore.getState()
      await store.addTemplate('First', '', sampleNodes, sampleEdges)
      await store.addTemplate('Second', '', sampleNodes, sampleEdges)

      const { templates } = useCustomTemplatesStore.getState()
      expect(templates[0].name).toBe('Second')
      expect(templates[1].name).toBe('First')
    })
  })

  describe('removeTemplate', () => {
    it('removes template by ID', async () => {
      vi.spyOn(Date, 'now').mockReturnValue(100)
      await useCustomTemplatesStore.getState().addTemplate('ToRemove', '', sampleNodes, sampleEdges)
      vi.restoreAllMocks()

      expect(useCustomTemplatesStore.getState().templates).toHaveLength(1)

      await useCustomTemplatesStore.getState().removeTemplate('custom-100')
      expect(useCustomTemplatesStore.getState().templates).toHaveLength(0)
    })

    it('does nothing for non-existent ID', async () => {
      await useCustomTemplatesStore.getState().addTemplate('Keep', '', sampleNodes, sampleEdges)

      await useCustomTemplatesStore.getState().removeTemplate('nonexistent')
      expect(useCustomTemplatesStore.getState().templates).toHaveLength(1)
    })

    it('only removes the targeted template', async () => {
      vi.spyOn(Date, 'now')
        .mockReturnValueOnce(1)
        .mockReturnValueOnce(2)
        .mockReturnValueOnce(3)

      const store = useCustomTemplatesStore.getState()
      await store.addTemplate('A', '', sampleNodes, sampleEdges)
      await store.addTemplate('B', '', sampleNodes, sampleEdges)
      await store.addTemplate('C', '', sampleNodes, sampleEdges)
      vi.restoreAllMocks()

      await useCustomTemplatesStore.getState().removeTemplate('custom-2')

      const { templates } = useCustomTemplatesStore.getState()
      expect(templates).toHaveLength(2)
      expect(templates.map((t) => t.name)).toEqual(['C', 'A'])
    })
  })
})
