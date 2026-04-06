import { describe, it, expect, vi } from 'vitest'
import { BUILTIN_TEMPLATES, TEMPLATE_CATEGORIES, getTemplateWorkflow } from '../templates'

// Mock NODE_REGISTRY (used by buildTemplate via template-builder)
vi.mock('@/components/studio/nodes/registry', () => ({
  NODE_REGISTRY: new Proxy({}, {
    get: (_target, type: string) => ({
      type,
      defaultData: (name: string) => ({ type, name }),
    }),
  }),
}))

describe('BUILTIN_TEMPLATES', () => {
  it('has at least 20 templates', () => {
    expect(BUILTIN_TEMPLATES.length).toBeGreaterThanOrEqual(20)
  })

  it('each template has required fields', () => {
    for (const t of BUILTIN_TEMPLATES) {
      expect(t.id).toBeTruthy()
      expect(t.name).toBeTruthy()
      expect(t.category).toBeTruthy()
      expect(t.shortDescription).toBeTruthy()
      expect(Array.isArray(t.tags)).toBe(true)
      expect(Array.isArray(t.sampleMessages)).toBe(true)
      expect(t.sampleMessages.length).toBeGreaterThan(0)
      expect(t.def).toBeDefined()
      expect(Array.isArray(t.def.nodes)).toBe(true)
      expect(Array.isArray(t.def.connections)).toBe(true)
    }
  })

  it('has unique IDs', () => {
    const ids = BUILTIN_TEMPLATES.map((t) => t.id)
    expect(new Set(ids).size).toBe(ids.length)
  })

  it('all connection indices reference valid nodes', () => {
    for (const t of BUILTIN_TEMPLATES) {
      const maxIndex = t.def.nodes.length - 1
      for (const conn of t.def.connections) {
        expect(conn.from).toBeGreaterThanOrEqual(0)
        expect(conn.from).toBeLessThanOrEqual(maxIndex)
        expect(conn.to).toBeGreaterThanOrEqual(0)
        expect(conn.to).toBeLessThanOrEqual(maxIndex)
      }
    }
  })

  it('all node types are valid', () => {
    const validTypes = [
      'agent', 'tool', 'rag', 'condition', 'loop', 'router',
      'a2a-agent', 'human', 'code', 'iteration', 'parallel',
      'http-request', 'autonomous', 'start', 'end',
    ]
    for (const t of BUILTIN_TEMPLATES) {
      for (const nd of t.def.nodes) {
        expect(validTypes).toContain(nd.type)
      }
    }
  })
})

describe('TEMPLATE_CATEGORIES', () => {
  it('is derived from templates', () => {
    expect(TEMPLATE_CATEGORIES.length).toBeGreaterThan(0)
    for (const cat of TEMPLATE_CATEGORIES) {
      expect(BUILTIN_TEMPLATES.some((t) => t.category === cat)).toBe(true)
    }
  })

  it('has no duplicates', () => {
    expect(new Set(TEMPLATE_CATEGORIES).size).toBe(TEMPLATE_CATEGORIES.length)
  })
})

describe('getTemplateWorkflow', () => {
  it('returns workflow for valid template ID', () => {
    const result = getTemplateWorkflow('sequential')
    expect(result).not.toBeNull()
    expect(result!.nodes.length).toBeGreaterThan(0)
    expect(Array.isArray(result!.edges)).toBe(true)
  })

  it('returns null for unknown template ID', () => {
    expect(getTemplateWorkflow('nonexistent')).toBeNull()
  })

  it('includes Start and End nodes', () => {
    const result = getTemplateWorkflow('sequential')!
    expect(result.nodes[0].type).toBe('start')
    expect(result.nodes[result.nodes.length - 1].type).toBe('end')
  })
})
