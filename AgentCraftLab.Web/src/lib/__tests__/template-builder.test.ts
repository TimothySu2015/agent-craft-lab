import { describe, it, expect, vi } from 'vitest'
import { buildTemplate, type TemplateDef } from '../template-builder'

// Mock NODE_REGISTRY with minimal configs
vi.mock('@/components/studio/nodes/registry', () => ({
  NODE_REGISTRY: {
    agent: {
      type: 'agent',
      defaultData: (name: string) => ({
        type: 'agent', name, instructions: '', model: 'gpt-4o', provider: 'openai',
        endpoint: '', deploymentName: '', historyProvider: 'none', maxMessages: 20,
        middleware: '', tools: [], skills: [],
      }),
    },
    condition: {
      type: 'condition',
      defaultData: (name: string) => ({
        type: 'condition', name, conditionType: 'contains', conditionExpression: '', maxIterations: 5,
      }),
    },
    code: {
      type: 'code',
      defaultData: (name: string) => ({
        type: 'code', name, transformType: 'template', pattern: '', replacement: '',
        template: '{{input}}', maxLength: 0, delimiter: '\\n', splitIndex: 0,
      }),
    },
    human: {
      type: 'human',
      defaultData: (name: string) => ({
        type: 'human', name, prompt: '', inputType: 'text', choices: '', timeoutSeconds: 0,
      }),
    },
    start: { type: 'start', defaultData: (name: string) => ({ type: 'start', name }) },
    end: { type: 'end', defaultData: (name: string) => ({ type: 'end', name }) },
  },
}))

describe('buildTemplate', () => {
  it('always includes Start and End nodes', () => {
    const def: TemplateDef = { nodes: [], connections: [] }
    const { nodes } = buildTemplate(def)

    expect(nodes).toHaveLength(2) // Start + End
    expect(nodes[0].type).toBe('start')
    expect(nodes[0].id).toBe('start-1')
    expect(nodes[nodes.length - 1].type).toBe('end')
    expect(nodes[nodes.length - 1].id).toBe('end-1')
  })

  it('creates nodes with correct IDs based on type and index', () => {
    const def: TemplateDef = {
      nodes: [
        { type: 'agent', name: 'A' },
        { type: 'agent', name: 'B' },
        { type: 'condition', name: 'C' },
      ],
      connections: [],
    }
    const { nodes } = buildTemplate(def)

    // Start + 3 template nodes + End
    expect(nodes).toHaveLength(5)
    expect(nodes[1].id).toBe('agent-1')
    expect(nodes[2].id).toBe('agent-2')
    expect(nodes[3].id).toBe('condition-3')
  })

  it('merges custom data with defaults', () => {
    const def: TemplateDef = {
      nodes: [
        { type: 'agent', name: 'Expert', data: { instructions: 'Be thorough.', tools: ['web_search'] } },
      ],
      connections: [],
    }
    const { nodes } = buildTemplate(def)

    const agent = nodes[1]
    expect(agent.data.name).toBe('Expert')
    expect((agent.data as any).instructions).toBe('Be thorough.')
    expect((agent.data as any).tools).toEqual(['web_search'])
    // Default fields preserved
    expect((agent.data as any).model).toBe('gpt-4o')
  })

  it('creates Start → first node edge automatically', () => {
    const def: TemplateDef = {
      nodes: [{ type: 'agent', name: 'A' }],
      connections: [],
    }
    const { edges } = buildTemplate(def)

    const startEdge = edges.find((e) => e.source === 'start-1')
    expect(startEdge).toBeDefined()
    expect(startEdge!.target).toBe('agent-1')
  })

  it('creates last node → End edge when no explicit end connection', () => {
    const def: TemplateDef = {
      nodes: [
        { type: 'agent', name: 'A' },
        { type: 'agent', name: 'B' },
      ],
      connections: [{ from: 0, to: 1 }],
    }
    const { edges } = buildTemplate(def)

    const endEdge = edges.find((e) => e.target === 'end-1')
    expect(endEdge).toBeDefined()
    expect(endEdge!.source).toBe('agent-2') // last node
  })

  it('maps connections to correct node IDs', () => {
    const def: TemplateDef = {
      nodes: [
        { type: 'agent', name: 'A' },
        { type: 'condition', name: 'Check' },
        { type: 'agent', name: 'B' },
      ],
      connections: [
        { from: 0, to: 1 },
        { from: 1, to: 2, fromOutput: 'output_1' },
      ],
    }
    const { edges } = buildTemplate(def)

    // Start → A, A → Check, Check → B, B → End
    const aToCheck = edges.find((e) => e.source === 'agent-1' && e.target === 'condition-2')
    expect(aToCheck).toBeDefined()

    const checkToB = edges.find((e) => e.source === 'condition-2' && e.target === 'agent-3')
    expect(checkToB).toBeDefined()
    expect(checkToB!.sourceHandle).toBe('output_1')
  })

  it('skips connections with invalid indices', () => {
    const def: TemplateDef = {
      nodes: [{ type: 'agent', name: 'A' }],
      connections: [{ from: 0, to: 5 }], // index 5 doesn't exist
    }
    const { edges } = buildTemplate(def)

    // Should only have Start → A and A → End
    expect(edges).toHaveLength(2)
  })

  it('positions nodes horizontally with spacing', () => {
    const def: TemplateDef = {
      nodes: [
        { type: 'agent', name: 'A' },
        { type: 'agent', name: 'B' },
        { type: 'agent', name: 'C' },
      ],
      connections: [],
    }
    const { nodes } = buildTemplate(def)

    // Start, A, B, C, End — x positions should increase
    for (let i = 1; i < nodes.length; i++) {
      expect(nodes[i].position.x).toBeGreaterThan(nodes[i - 1].position.x)
    }
  })

  it('alternates y position for visual interest', () => {
    const def: TemplateDef = {
      nodes: [
        { type: 'agent', name: 'A' },
        { type: 'agent', name: 'B' },
      ],
      connections: [],
    }
    const { nodes } = buildTemplate(def)

    // node at index 0 (even) → yBase, index 1 (odd) → yBase - 60
    const a = nodes[1] // first template node
    const b = nodes[2] // second template node
    expect(a.position.y).not.toBe(b.position.y)
  })

  it('skips unknown node types gracefully', () => {
    const def: TemplateDef = {
      nodes: [
        { type: 'agent', name: 'A' },
        { type: 'unknown-type' as any, name: 'X' },
        { type: 'agent', name: 'B' },
      ],
      connections: [],
    }
    const { nodes } = buildTemplate(def)

    // Start + A + B + End (unknown skipped)
    expect(nodes).toHaveLength(4)
  })
})
