import { describe, it, expect } from 'vitest'
import { autoLayout } from '../auto-layout'
import type { Node, Edge } from '@xyflow/react'
import type { NodeData, AgentNodeData } from '@/types/workflow'

function makeAgentData(id: string): AgentNodeData {
  return {
    type: 'agent',
    name: id,
    instructions: '',
    model: { provider: 'openai', model: 'gpt-4o' },
    tools: [],
    mcpServers: [],
    a2AAgents: [],
    httpApis: [],
    skills: [],
    output: { kind: 'text' },
    history: { provider: 'none', maxMessages: 20 },
    middleware: [],
  }
}

function node(id: string, x = 0, y = 0): Node<NodeData> {
  return { id, type: 'agent', position: { x, y }, data: makeAgentData(id) }
}

function edge(source: string, target: string): Edge {
  return { id: `e-${source}-${target}`, source, target }
}

describe('autoLayout', () => {
  it('repositions nodes based on graph structure', async () => {
    const nodes = [node('a'), node('b'), node('c')]
    const edges = [edge('a', 'b'), edge('b', 'c')]

    const result = await autoLayout(nodes, edges)

    expect(result).toHaveLength(3)
    // Each node should have updated positions (not all at 0,0)
    const positions = result.map((n) => n.position)
    const allSame = positions.every((p) => p.x === positions[0].x && p.y === positions[0].y)
    expect(allSame).toBe(false)
  })

  it('maintains left-to-right order by default', async () => {
    const nodes = [node('a'), node('b'), node('c')]
    const edges = [edge('a', 'b'), edge('b', 'c')]

    const result = await autoLayout(nodes, edges, 'LR')

    const [a, b, c] = result
    // In LR direction, x positions should increase along the chain
    expect(a.position.x).toBeLessThan(b.position.x)
    expect(b.position.x).toBeLessThan(c.position.x)
  })

  it('supports top-to-bottom direction', async () => {
    const nodes = [node('a'), node('b'), node('c')]
    const edges = [edge('a', 'b'), edge('b', 'c')]

    const result = await autoLayout(nodes, edges, 'TB')

    const [a, b, c] = result
    // In TB direction, y positions should increase along the chain
    expect(a.position.y).toBeLessThan(b.position.y)
    expect(b.position.y).toBeLessThan(c.position.y)
  })

  it('preserves node data and type', async () => {
    const original = node('x')
    original.data = makeAgentData('TestAgent')
    const result = await autoLayout([original], [])

    expect(result[0].id).toBe('x')
    expect(result[0].type).toBe('agent')
    expect(result[0].data).toEqual(original.data)
  })

  it('handles empty input', async () => {
    const result = await autoLayout([], [])
    expect(result).toEqual([])
  })

  it('handles disconnected nodes', async () => {
    const nodes = [node('a'), node('b'), node('c')]
    // No edges — nodes are disconnected
    const result = await autoLayout(nodes, [])

    expect(result).toHaveLength(3)
    // All should still get positions
    for (const n of result) {
      expect(n.position).toBeDefined()
      expect(typeof n.position.x).toBe('number')
      expect(typeof n.position.y).toBe('number')
    }
  })

  it('handles branching graph', async () => {
    const nodes = [node('root'), node('left'), node('right')]
    const edges = [edge('root', 'left'), edge('root', 'right')]

    const result = await autoLayout(nodes, edges, 'LR')

    const root = result.find((n) => n.id === 'root')!
    const left = result.find((n) => n.id === 'left')!
    const right = result.find((n) => n.id === 'right')!

    // Root should be to the left of both children
    expect(root.position.x).toBeLessThan(left.position.x)
    expect(root.position.x).toBeLessThan(right.position.x)
    // Branches should be at different y positions
    expect(left.position.y).not.toBe(right.position.y)
  })

  it('handles cycle (loop back edge)', async () => {
    const nodes = [node('a'), node('b'), node('c')]
    const edges = [edge('a', 'b'), edge('b', 'c'), edge('c', 'a')] // c → a = cycle

    const result = await autoLayout(nodes, edges, 'LR')

    expect(result).toHaveLength(3)
    // All should get valid positions despite cycle
    for (const n of result) {
      expect(typeof n.position.x).toBe('number')
      expect(typeof n.position.y).toBe('number')
    }
  })
})
