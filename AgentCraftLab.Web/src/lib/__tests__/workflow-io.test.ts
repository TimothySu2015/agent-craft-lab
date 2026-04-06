import { describe, it, expect, vi, beforeEach } from 'vitest'
import { importWorkflow, exportWorkflow } from '../workflow-io'
import type { Node, Edge } from '@xyflow/react'
import type { NodeData } from '@/types/workflow'

describe('importWorkflow', () => {
  it('parses valid workflow file', async () => {
    const data = {
      version: 1,
      name: 'Test',
      nodes: [{ id: 'start-1', type: 'start', position: { x: 0, y: 0 }, data: { type: 'start', name: 'Start' } }],
      edges: [],
      createdAt: '2026-01-01T00:00:00Z',
    }
    const file = new File([JSON.stringify(data)], 'test.json', { type: 'application/json' })

    const result = await importWorkflow(file)
    expect(result.name).toBe('Test')
    expect(result.nodes).toHaveLength(1)
    expect(result.version).toBe(1)
  })

  it('rejects file with missing nodes', async () => {
    const data = { version: 1, name: 'Bad' }
    const file = new File([JSON.stringify(data)], 'bad.json')

    await expect(importWorkflow(file)).rejects.toThrow('missing nodes')
  })

  it('rejects file with non-array nodes', async () => {
    const data = { version: 1, name: 'Bad', nodes: 'not-array' }
    const file = new File([JSON.stringify(data)], 'bad.json')

    await expect(importWorkflow(file)).rejects.toThrow('missing nodes')
  })

  it('rejects invalid JSON', async () => {
    const file = new File(['not json {{{'], 'invalid.json')

    await expect(importWorkflow(file)).rejects.toThrow('Invalid JSON')
  })
})

describe('exportWorkflow', () => {
  let clickedLink: { href: string; download: string } | null = null

  beforeEach(() => {
    clickedLink = null
    // Mock DOM methods
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:mock-url')
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {})
    vi.spyOn(document, 'createElement').mockReturnValue({
      set href(v: string) { clickedLink = clickedLink || { href: '', download: '' }; clickedLink.href = v },
      set download(v: string) { clickedLink = clickedLink || { href: '', download: '' }; clickedLink.download = v },
      click: vi.fn(),
    } as any)
  })

  it('sanitizes filename — replaces special chars with underscore', () => {
    const nodes: Node<NodeData>[] = []
    const edges: Edge[] = []

    exportWorkflow('Hello World!@#', nodes, edges)

    expect(clickedLink!.download).toBe('Hello_World___.json')
  })

  it('preserves CJK characters in filename', () => {
    exportWorkflow('我的工作流', [], [])

    expect(clickedLink!.download).toBe('我的工作流.json')
  })

  it('uses default name when empty', () => {
    exportWorkflow('', [], [])

    expect(clickedLink!.download).toBe('workflow.json')
  })
})
