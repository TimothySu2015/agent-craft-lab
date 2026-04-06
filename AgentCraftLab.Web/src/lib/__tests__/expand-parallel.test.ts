import { describe, it, expect } from 'vitest'
import { expandFlowPlanParallel } from '../expand-parallel'

describe('expandFlowPlanParallel', () => {
  it('leaves non-parallel nodes unchanged', () => {
    const spec = {
      nodes: [
        { type: 'agent', name: 'A' },
        { type: 'agent', name: 'B' },
      ],
    }
    expandFlowPlanParallel(spec)

    expect(spec.nodes).toHaveLength(2)
    expect(spec.nodes[0].name).toBe('A')
    expect(spec.nodes[1].name).toBe('B')
  })

  it('generates sequential connections when no connections provided', () => {
    const spec = {
      nodes: [
        { type: 'agent', name: 'A' },
        { type: 'agent', name: 'B' },
        { type: 'agent', name: 'C' },
      ],
    }
    expandFlowPlanParallel(spec)

    expect(spec.connections).toEqual([
      { from: 0, to: 1 },
      { from: 1, to: 2 },
    ])
  })

  it('expands parallel node branches into individual agent nodes', () => {
    const spec = {
      nodes: [
        {
          type: 'parallel',
          name: 'Fan-out',
          branches: [
            { name: 'Worker1', goal: 'do task 1', tools: ['t1'] },
            { name: 'Worker2', goal: 'do task 2', tools: ['t2'] },
          ],
        },
      ],
    }
    expandFlowPlanParallel(spec)

    // parallel node + 2 branch agents = 3 nodes
    expect(spec.nodes).toHaveLength(3)

    // parallel node has branch names joined
    expect(spec.nodes[0].type).toBe('parallel')
    expect(spec.nodes[0].branches).toBe('Worker1,Worker2')
    expect(spec.nodes[0].mergeStrategy).toBe('labeled')

    // branch agents
    expect(spec.nodes[1]).toMatchObject({
      type: 'agent',
      name: 'Worker1',
      instructions: 'do task 1',
      tools: ['t1'],
    })
    expect(spec.nodes[2]).toMatchObject({
      type: 'agent',
      name: 'Worker2',
      instructions: 'do task 2',
      tools: ['t2'],
    })
  })

  it('creates connections from parallel node to each branch agent', () => {
    const spec = {
      nodes: [
        {
          type: 'parallel',
          branches: [
            { name: 'B1', goal: 'g1' },
            { name: 'B2', goal: 'g2' },
            { name: 'B3', goal: 'g3' },
          ],
        },
      ],
    }
    expandFlowPlanParallel(spec)

    // connections: parallel(0) -> B1(1), parallel(0) -> B2(2), parallel(0) -> B3(3)
    expect(spec.connections).toContainEqual({ from: 0, to: 1, fromOutput: 'output_1' })
    expect(spec.connections).toContainEqual({ from: 0, to: 2, fromOutput: 'output_2' })
    expect(spec.connections).toContainEqual({ from: 0, to: 3, fromOutput: 'output_3' })
  })

  it('uses Done port for parallel-to-next sequential connection', () => {
    const spec = {
      nodes: [
        { type: 'agent', name: 'Start' },
        {
          type: 'parallel',
          branches: [
            { name: 'B1', goal: 'g1' },
            { name: 'B2', goal: 'g2' },
          ],
        },
        { type: 'agent', name: 'End' },
      ],
    }
    expandFlowPlanParallel(spec)

    // Expanded: Start(0), Parallel(1), B1(2), B2(3), End(4)
    // Sequential: Start→Parallel, Parallel→End (Done port = output_3)
    const seqConnections = spec.connections!.filter(
      (c: any) => !c.fromOutput?.startsWith('output_') || c.fromOutput === 'output_3'
    )
    expect(seqConnections).toContainEqual({ from: 0, to: 1 })
    expect(seqConnections).toContainEqual({ from: 1, to: 4, fromOutput: 'output_3' })
  })

  it('preserves existing connections when provided', () => {
    const spec = {
      nodes: [
        { type: 'agent', name: 'A' },
        { type: 'agent', name: 'B' },
      ],
      connections: [{ from: 0, to: 1, label: 'custom' }],
    }
    expandFlowPlanParallel(spec)

    // Should NOT generate additional sequential connections
    expect(spec.connections).toHaveLength(1)
    expect(spec.connections![0]).toEqual({ from: 0, to: 1, label: 'custom' })
  })

  it('reads nodeType as fallback for type field', () => {
    const spec = {
      nodes: [
        {
          nodeType: 'parallel',
          branches: [{ name: 'W1', goal: 'g' }],
        },
      ],
    }
    expandFlowPlanParallel(spec)

    expect(spec.nodes).toHaveLength(2)
    expect(spec.nodes[0].type).toBe('parallel')
  })

  it('reads branches from data.branches', () => {
    const spec = {
      nodes: [
        {
          type: 'parallel',
          name: 'P',
          data: {
            branches: [{ name: 'X', instructions: 'do X' }],
          },
        },
      ],
    }
    expandFlowPlanParallel(spec)

    expect(spec.nodes).toHaveLength(2)
    expect(spec.nodes[1].name).toBe('X')
    expect(spec.nodes[1].instructions).toBe('do X')
  })

  it('uses instructions field when goal is absent', () => {
    const spec = {
      nodes: [
        {
          type: 'parallel',
          branches: [{ name: 'W', instructions: 'fallback instructions' }],
        },
      ],
    }
    expandFlowPlanParallel(spec)

    expect(spec.nodes[1].instructions).toBe('fallback instructions')
  })

  it('reads mergeStrategy from data.mergeStrategy', () => {
    const spec = {
      nodes: [
        {
          type: 'parallel',
          data: { mergeStrategy: 'concatenate', branches: [{ name: 'A' }] },
        },
      ],
    }
    expandFlowPlanParallel(spec)

    expect(spec.nodes[0].mergeStrategy).toBe('concatenate')
  })

  it('handles single-node spec without crashing', () => {
    const spec = { nodes: [{ type: 'agent', name: 'Solo' }] }
    expandFlowPlanParallel(spec)

    expect(spec.nodes).toHaveLength(1)
    expect(spec.connections).toEqual([])
  })

  it('handles empty nodes array', () => {
    const spec = { nodes: [] as any[] }
    expandFlowPlanParallel(spec)

    expect(spec.nodes).toHaveLength(0)
    expect(spec.connections).toEqual([])
  })

  it('skips parallel expansion when branches are strings (already expanded)', () => {
    const spec = {
      nodes: [
        { type: 'parallel', branches: 'A,B', name: 'P' },
      ],
    }
    expandFlowPlanParallel(spec)

    // branches is a string, not object array — should be treated as non-parallel
    expect(spec.nodes).toHaveLength(1)
    expect(spec.nodes[0].branches).toBe('A,B')
  })
})
