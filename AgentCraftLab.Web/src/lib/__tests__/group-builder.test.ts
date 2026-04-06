import { describe, it, expect } from 'vitest'
import {
  buildGroups,
  buildAdjacency,
  collectChainUntilStop,
  collectBranchNodes,
  findConvergencePoint,
  getNextNodeId,
} from '../group-builder'
import type { Node, Edge } from '@xyflow/react'

// ─── Helpers ───

function node(id: string, type = 'agent', data: Record<string, any> = {}): Node {
  return { id, type, position: { x: 0, y: 0 }, data: { type, name: id, ...data } }
}

function edge(source: string, target: string, sourceHandle = 'output_1'): Edge {
  return { id: `e-${source}-${target}-${sourceHandle}`, source, target, sourceHandle }
}

// ─── buildAdjacency ───

describe('buildAdjacency', () => {
  it('builds adjacency map from edges', () => {
    const edges = [edge('a', 'b'), edge('a', 'c', 'output_2'), edge('b', 'c')]
    const adj = buildAdjacency(edges)

    expect(adj.get('a')).toHaveLength(2)
    expect(adj.get('b')).toHaveLength(1)
    expect(adj.has('c')).toBe(false) // c has no outgoing
  })
})

// ─── getNextNodeId ───

describe('getNextNodeId', () => {
  it('returns matching port target', () => {
    const adj = buildAdjacency([edge('a', 'b', 'output_1'), edge('a', 'c', 'output_2')])
    expect(getNextNodeId(adj, 'a', 'output_1')).toBe('b')
    expect(getNextNodeId(adj, 'a', 'output_2')).toBe('c')
  })

  it('fallback output_1 to first edge', () => {
    const adj = buildAdjacency([edge('a', 'b', 'output_2')])
    // output_1 not found, but fallback to first edge
    expect(getNextNodeId(adj, 'a', 'output_1')).toBe('b')
  })

  it('returns null for missing node', () => {
    const adj = buildAdjacency([])
    expect(getNextNodeId(adj, 'x', 'output_1')).toBeNull()
  })
})

// ─── collectChainUntilStop ───

describe('collectChainUntilStop', () => {
  it('collects linear chain until stopId', () => {
    // loop → a → b → loop (back edge)
    const edges = [
      edge('loop', 'a', 'output_1'),
      edge('a', 'b'),
      edge('b', 'loop'),
    ]
    const adj = buildAdjacency(edges)
    const body = collectChainUntilStop('loop', 'output_1', 'loop', adj)

    expect(body).toEqual(new Set(['a', 'b']))
  })

  it('returns empty for no outgoing edge', () => {
    const adj = buildAdjacency([])
    const body = collectChainUntilStop('loop', 'output_1', 'loop', adj)
    expect(body.size).toBe(0)
  })

  it('handles single-node body', () => {
    const edges = [edge('loop', 'a', 'output_1'), edge('a', 'loop')]
    const adj = buildAdjacency(edges)
    const body = collectChainUntilStop('loop', 'output_1', 'loop', adj)
    expect(body).toEqual(new Set(['a']))
  })
})

// ─── collectBranchNodes ───

describe('collectBranchNodes', () => {
  it('collects all reachable nodes from a branch', () => {
    const edges = [
      edge('cond', 'a', 'output_1'),
      edge('a', 'b'),
      edge('b', 'merge'),
    ]
    const adj = buildAdjacency(edges)
    const branch = collectBranchNodes('cond', 'output_1', adj)

    expect(branch).toEqual(new Set(['a', 'b', 'merge']))
  })

  it('returns empty for missing output port', () => {
    const adj = buildAdjacency([edge('cond', 'a', 'output_1')])
    const branch = collectBranchNodes('cond', 'output_2', adj)
    expect(branch.size).toBe(0)
  })

  it('does not loop back to source', () => {
    // 分支走完又回到 cond（不應收集 cond 本身）
    const edges = [
      edge('cond', 'a', 'output_1'),
      edge('a', 'cond'),
    ]
    const adj = buildAdjacency(edges)
    const branch = collectBranchNodes('cond', 'output_1', adj)
    expect(branch).toEqual(new Set(['a']))
    expect(branch.has('cond')).toBe(false)
  })
})

// ─── findConvergencePoint ───

describe('findConvergencePoint', () => {
  it('finds common node in two branches', () => {
    const branchA = new Set(['a', 'merge'])
    const branchB = new Set(['b', 'merge'])
    expect(findConvergencePoint(branchA, branchB)).toBe('merge')
  })

  it('returns null when no convergence', () => {
    const branchA = new Set(['a'])
    const branchB = new Set(['b'])
    expect(findConvergencePoint(branchA, branchB)).toBeNull()
  })

  it('returns first intersection in iteration order', () => {
    const branchA = new Set(['a', 'x', 'y'])
    const branchB = new Set(['b', 'x', 'y'])
    expect(findConvergencePoint(branchA, branchB)).toBe('x')
  })
})

// ─── buildGroups — Condition ───

describe('buildGroups — Condition', () => {
  it('creates condition group with True/False branches', () => {
    const nodes = [
      node('start', 'start'),
      node('cond', 'condition'),
      node('a', 'agent'),
      node('b', 'agent'),
      node('merge', 'agent'),
      node('end', 'end'),
    ]
    const edges = [
      edge('start', 'cond'),
      edge('cond', 'a', 'output_1'),   // True
      edge('cond', 'b', 'output_2'),   // False
      edge('a', 'merge'),
      edge('b', 'merge'),
      edge('merge', 'end'),
    ]

    const groups = buildGroups(nodes, edges)

    expect(groups).toHaveLength(1)
    expect(groups[0].type).toBe('condition-group')
    expect(groups[0].controlNodeId).toBe('cond')
    expect(groups[0].branches).toHaveLength(2)
    expect(groups[0].branches[0].label).toBe('True')
    expect(groups[0].branches[0].nodeIds).toEqual(new Set(['a']))
    expect(groups[0].branches[1].label).toBe('False')
    expect(groups[0].branches[1].nodeIds).toEqual(new Set(['b']))
    // merge 是匯合點，不屬於任何分支
    expect(groups[0].branches[0].nodeIds.has('merge')).toBe(false)
    expect(groups[0].branches[1].nodeIds.has('merge')).toBe(false)
  })

  it('handles empty False branch', () => {
    const nodes = [
      node('cond', 'condition'),
      node('a', 'agent'),
      node('end', 'end'),
    ]
    const edges = [
      edge('cond', 'a', 'output_1'),
      edge('a', 'end'),
    ]

    const groups = buildGroups(nodes, edges)

    expect(groups).toHaveLength(1)
    expect(groups[0].branches[0].nodeIds.size).toBeGreaterThan(0)
    expect(groups[0].branches[1].nodeIds.size).toBe(0) // False branch empty
  })
})

// ─── buildGroups — Loop ───

describe('buildGroups — Loop', () => {
  it('creates loop group with body nodes', () => {
    const nodes = [
      node('start', 'start'),
      node('loop', 'loop'),
      node('a', 'agent'),
      node('b', 'agent'),
      node('after', 'agent'),
      node('end', 'end'),
    ]
    const edges = [
      edge('start', 'loop'),
      edge('loop', 'a', 'output_1'),   // Body
      edge('a', 'b'),
      edge('b', 'loop'),               // back edge
      edge('loop', 'after', 'output_2'), // Exit
      edge('after', 'end'),
    ]

    const groups = buildGroups(nodes, edges)

    expect(groups).toHaveLength(1)
    expect(groups[0].type).toBe('loop-group')
    expect(groups[0].controlNodeId).toBe('loop')
    expect(groups[0].branches[0].label).toBe('Body')
    expect(groups[0].branches[0].nodeIds).toEqual(new Set(['a', 'b']))
  })
})

// ─── buildGroups — Parallel ───

describe('buildGroups — Parallel', () => {
  it('creates parallel group with 3 branches', () => {
    const nodes = [
      node('par', 'parallel', { branches: 'Search,Analyze,Report' }),
      node('s', 'agent'),
      node('a', 'agent'),
      node('r', 'agent'),
      node('merge', 'agent'),
    ]
    const edges = [
      edge('par', 's', 'output_1'),
      edge('par', 'a', 'output_2'),
      edge('par', 'r', 'output_3'),
      edge('par', 'merge', 'output_4'), // Done port
    ]

    const groups = buildGroups(nodes, edges)

    expect(groups).toHaveLength(1)
    expect(groups[0].type).toBe('parallel-group')
    expect(groups[0].branches).toHaveLength(3)
    expect(groups[0].branches[0].label).toBe('Search')
    expect(groups[0].branches[0].nodeIds).toEqual(new Set(['s']))
    expect(groups[0].branches[1].label).toBe('Analyze')
    expect(groups[0].branches[1].nodeIds).toEqual(new Set(['a']))
    expect(groups[0].branches[2].label).toBe('Report')
    expect(groups[0].branches[2].nodeIds).toEqual(new Set(['r']))
  })

  it('excludes nodes after Done port from branches', () => {
    // Start → Detector → Parallel(English,Japanese,Korean) → End
    // Done port (output_4) connects to End — End should NOT be in the group
    const nodes = [
      node('start', 'start'),
      node('detector', 'agent'),
      node('par', 'parallel', { branches: 'English,Japanese,Korean' }),
      node('en', 'agent'),
      node('ja', 'agent'),
      node('ko', 'agent'),
      node('end', 'end'),
    ]
    const edges = [
      edge('start', 'detector'),
      edge('detector', 'par'),
      edge('par', 'en', 'output_1'),
      edge('par', 'ja', 'output_2'),
      edge('par', 'ko', 'output_3'),
      edge('par', 'end', 'output_4'),   // Done port → End
    ]

    const groups = buildGroups(nodes, edges)

    expect(groups).toHaveLength(1)
    const g = groups[0]
    expect(g.branches).toHaveLength(3)
    // Each branch should only contain its own agent
    expect(g.branches[0].nodeIds).toEqual(new Set(['en']))
    expect(g.branches[1].nodeIds).toEqual(new Set(['ja']))
    expect(g.branches[2].nodeIds).toEqual(new Set(['ko']))
    // Detector, Start, End should NOT be in any branch
    const allBranchNodes = new Set([...g.branches[0].nodeIds, ...g.branches[1].nodeIds, ...g.branches[2].nodeIds])
    expect(allBranchNodes.has('detector')).toBe(false)
    expect(allBranchNodes.has('start')).toBe(false)
    expect(allBranchNodes.has('end')).toBe(false)
  })

  it('excludes downstream nodes after Done port target', () => {
    // Parallel → Done → AfterMerge → End
    const nodes = [
      node('par', 'parallel', { branches: 'A,B' }),
      node('a', 'agent'),
      node('b', 'agent'),
      node('merge', 'agent'),
      node('final', 'agent'),
    ]
    const edges = [
      edge('par', 'a', 'output_1'),
      edge('par', 'b', 'output_2'),
      edge('par', 'merge', 'output_3'),  // Done
      edge('merge', 'final'),
    ]

    const groups = buildGroups(nodes, edges)
    const g = groups[0]
    expect(g.branches[0].nodeIds).toEqual(new Set(['a']))
    expect(g.branches[1].nodeIds).toEqual(new Set(['b']))
    // merge and final should NOT be in any branch
    const allNodes = new Set([...g.branches[0].nodeIds, ...g.branches[1].nodeIds])
    expect(allNodes.has('merge')).toBe(false)
    expect(allNodes.has('final')).toBe(false)
  })
})

// ─── buildGroups — Iteration ───

describe('buildGroups — Iteration', () => {
  it('creates iteration group with body', () => {
    const nodes = [
      node('iter', 'iteration'),
      node('process', 'agent'),
      node('done', 'agent'),
    ]
    const edges = [
      edge('iter', 'process', 'output_1'),
      edge('process', 'iter'),           // back to iteration
      edge('iter', 'done', 'output_2'),
    ]

    const groups = buildGroups(nodes, edges)

    expect(groups).toHaveLength(1)
    expect(groups[0].type).toBe('iteration-group')
    expect(groups[0].branches[0].nodeIds).toEqual(new Set(['process']))
  })
})

// ─── buildGroups — Nested ───

describe('buildGroups — Nested', () => {
  it('handles condition inside loop', () => {
    const nodes = [
      node('loop', 'loop'),
      node('cond', 'condition'),
      node('a', 'agent'),
      node('b', 'agent'),
      node('after', 'agent'),
    ]
    const edges = [
      edge('loop', 'cond', 'output_1'),   // Loop body starts with condition
      edge('cond', 'a', 'output_1'),       // True
      edge('cond', 'b', 'output_2'),       // False
      edge('a', 'loop'),                   // back to loop
      edge('b', 'loop'),                   // back to loop
      edge('loop', 'after', 'output_2'),   // Exit
    ]

    const groups = buildGroups(nodes, edges)

    // Should have 2 groups: inner condition + outer loop
    expect(groups).toHaveLength(2)

    // Inner condition group should be first (innermost first)
    const condGroup = groups.find((g) => g.type === 'condition-group')!
    expect(condGroup).toBeDefined()
    expect(condGroup.controlNodeId).toBe('cond')

    const loopGroup = groups.find((g) => g.type === 'loop-group')!
    expect(loopGroup).toBeDefined()
    expect(loopGroup.controlNodeId).toBe('loop')
  })
})

// ─── buildGroups — No control nodes ───

// ─── Real-world Loop workflow ───

describe('buildGroups — Real-world Loop', () => {
  it('detects loop body when back edge goes to a node before the loop', () => {
    // Start → Writer → ReviewLoop ↻ (Reviewer → Writer) → Publisher → End
    // Key: Reviewer's output goes to Writer (which is BEFORE ReviewLoop), not to ReviewLoop directly
    const nodes = [
      node('start', 'start'),
      node('writer', 'agent'),
      node('review-loop', 'loop'),
      node('reviewer', 'agent'),
      node('publisher', 'agent'),
      node('end', 'end'),
    ]
    const edges = [
      edge('start', 'writer'),
      edge('writer', 'review-loop'),
      edge('review-loop', 'reviewer', 'output_1'),  // Body
      edge('reviewer', 'writer'),                     // Back to Writer (NOT to review-loop!)
      edge('review-loop', 'publisher', 'output_2'),   // Exit
      edge('publisher', 'end'),
    ]

    const groups = buildGroups(nodes, edges)

    expect(groups).toHaveLength(1)
    const g = groups[0]
    expect(g.type).toBe('loop-group')
    // Reviewer should be in body. Writer is BEFORE the loop, not part of body.
    expect(g.branches[0].nodeIds.has('reviewer')).toBe(true)
    // Writer IS part of the body chain (Reviewer → Writer → ReviewLoop)
    expect(g.branches[0].nodeIds.has('writer')).toBe(true)
  })

  it('detects loop body when chain goes Reviewer → Writer → ReviewLoop', () => {
    // Writer is part of the loop body (back edge goes to ReviewLoop)
    const nodes = [
      node('start', 'start'),
      node('review-loop', 'loop'),
      node('reviewer', 'agent'),
      node('writer', 'agent'),
      node('publisher', 'agent'),
      node('end', 'end'),
    ]
    const edges = [
      edge('start', 'review-loop'),
      edge('review-loop', 'reviewer', 'output_1'),   // Body
      edge('reviewer', 'writer'),
      edge('writer', 'review-loop'),                   // Back to loop
      edge('review-loop', 'publisher', 'output_2'),   // Exit
      edge('publisher', 'end'),
    ]

    const groups = buildGroups(nodes, edges)

    const g = groups[0]
    expect(g.branches[0].nodeIds).toEqual(new Set(['reviewer', 'writer']))
  })
})

describe('buildGroups — Edge cases', () => {
  it('returns empty for workflow with no control nodes', () => {
    const nodes = [
      node('start', 'start'),
      node('agent', 'agent'),
      node('end', 'end'),
    ]
    const edges = [edge('start', 'agent'), edge('agent', 'end')]

    expect(buildGroups(nodes, edges)).toEqual([])
  })

  it('handles control node with no outgoing edges', () => {
    const nodes = [node('cond', 'condition')]
    const edges: Edge[] = []

    const groups = buildGroups(nodes, edges)
    expect(groups).toHaveLength(1)
    expect(groups[0].branches[0].nodeIds.size).toBe(0)
    expect(groups[0].branches[1].nodeIds.size).toBe(0)
  })
})
