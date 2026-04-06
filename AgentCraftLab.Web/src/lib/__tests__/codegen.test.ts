import { describe, it, expect } from 'vitest'
import { generateCSharpCode } from '../codegen'
import type { Node, Edge } from '@xyflow/react'
import type { NodeData, AgentNodeData } from '@/types/workflow'

// ── Helpers ──

function agentNode(
  id: string,
  name: string,
  overrides: Partial<AgentNodeData> = {},
): Node<NodeData> {
  return {
    id,
    type: 'agent',
    position: { x: 0, y: 0 },
    data: {
      type: 'agent', name, instructions: 'You are helpful.', model: 'gpt-4o', provider: 'openai',
      endpoint: '', deploymentName: '', historyProvider: 'none', maxMessages: 20,
      middleware: '', tools: [], skills: [], ...overrides,
    },
  }
}

function startNode(): Node<NodeData> {
  return { id: 'start-1', type: 'start', position: { x: 0, y: 0 }, data: { type: 'start', name: 'Start' } }
}

function endNode(): Node<NodeData> {
  return { id: 'end-1', type: 'end', position: { x: 0, y: 0 }, data: { type: 'end', name: 'End' } }
}

function edge(source: string, target: string): Edge {
  return { id: `e-${source}-${target}`, source, target }
}

// ── Tests ──

describe('generateCSharpCode', () => {
  it('returns comment when no agents', () => {
    const result = generateCSharpCode([startNode(), endNode()], [])
    expect(result).toContain('No agents defined')
  })

  it('generates single agent pattern', () => {
    const nodes = [startNode(), agentNode('a1', 'Writer'), endNode()]
    const edges = [edge('start-1', 'a1'), edge('a1', 'end-1')]
    const code = generateCSharpCode(nodes, edges)

    expect(code).toContain('using Microsoft.Agents.AI;')
    expect(code).toContain('using OpenAI;')
    expect(code).toContain('ChatClientAgent Writer')
    expect(code).toContain('CreateSessionAsync')
    expect(code).toContain('RunAsync')
    // Single agent should NOT have Workflows using
    expect(code).not.toContain('using Microsoft.Agents.AI.Workflows;')
  })

  it('generates sequential pattern for chain of agents (one root)', () => {
    // detectPattern checks ALL edges — for sequential, exactly one agent must NOT be a target
    const nodes = [
      startNode(),
      agentNode('a1', 'Researcher'),
      agentNode('a2', 'Writer'),
      endNode(),
    ]
    // Only inter-agent edge: a1 → a2. a1 is never a target among agents → 1 root → sequential
    const edges = [
      edge('a1', 'a2'),
    ]
    const code = generateCSharpCode(nodes, edges)

    expect(code).toContain('Sequential Workflow')
    expect(code).toContain('BuildSequential')
    expect(code).toContain('Researcher')
    expect(code).toContain('Writer')
    expect(code).toContain('using Microsoft.Agents.AI.Workflows;')
  })

  it('generates concurrent pattern when multiple roots', () => {
    const nodes = [
      startNode(),
      agentNode('a1', 'AgentA'),
      agentNode('a2', 'AgentB'),
      endNode(),
    ]
    // Both agents connect to end, no inter-agent edges → concurrent
    const edges = [
      edge('start-1', 'a1'),
      edge('start-1', 'a2'),
      edge('a1', 'end-1'),
      edge('a2', 'end-1'),
    ]
    const code = generateCSharpCode(nodes, edges)

    expect(code).toContain('Concurrent Workflow')
    expect(code).toContain('BuildConcurrent')
  })

  it('generates handoff pattern when agent has multiple outgoing edges', () => {
    const nodes = [
      startNode(),
      agentNode('a1', 'Triage'),
      agentNode('a2', 'Billing'),
      agentNode('a3', 'Support'),
      endNode(),
    ]
    const edges = [
      edge('start-1', 'a1'),
      edge('a1', 'a2'),
      edge('a1', 'a3'),  // a1 has 2 outgoing → handoff
      edge('a2', 'end-1'),
      edge('a3', 'end-1'),
    ]
    const code = generateCSharpCode(nodes, edges)

    expect(code).toContain('Handoff Workflow')
    expect(code).toContain('CreateHandoffBuilderWith(Triage)')
    expect(code).toContain('WithHandoffs')
  })

  it('generates imperative pattern when logic nodes exist', () => {
    const nodes = [
      startNode(),
      agentNode('a1', 'Agent1'),
      {
        id: 'c1', type: 'condition', position: { x: 0, y: 0 },
        data: { type: 'condition' as const, name: 'Check', conditionType: 'contains', conditionExpression: 'done', maxIterations: 5 },
      },
      endNode(),
    ]
    const edges = [edge('start-1', 'a1'), edge('a1', 'c1'), edge('c1', 'end-1')]
    const code = generateCSharpCode(nodes as Node<NodeData>[], edges)

    expect(code).toContain('Imperative Mode')
    expect(code).toContain('WorkflowExecutionService')
  })

  it('handles azure-openai provider', () => {
    const nodes = [
      startNode(),
      agentNode('a1', 'AzureAgent', { provider: 'azure-openai', model: 'gpt-4o' }),
      endNode(),
    ]
    const edges = [edge('start-1', 'a1'), edge('a1', 'end-1')]
    const code = generateCSharpCode(nodes, edges)

    expect(code).toContain('using Azure.AI.OpenAI;')
    expect(code).toContain('AzureOpenAIClient')
    expect(code).toContain('AzureKeyCredential')
  })

  it('includes tool references in agent definition', () => {
    const nodes = [
      startNode(),
      agentNode('a1', 'ToolUser', { tools: ['search', 'calculator'] }),
      endNode(),
    ]
    const edges = [edge('start-1', 'a1'), edge('a1', 'end-1')]
    const code = generateCSharpCode(nodes, edges)

    expect(code).toContain('tools: [search, calculator]')
  })

  it('escapes special characters in instructions', () => {
    const nodes = [
      startNode(),
      agentNode('a1', 'Agent1', { instructions: 'Line1\nLine2 "quoted"' }),
      endNode(),
    ]
    const edges = [edge('start-1', 'a1'), edge('a1', 'end-1')]
    const code = generateCSharpCode(nodes, edges)

    expect(code).toContain('Line1\\nLine2 \\"quoted\\"')
  })

  it('topologically sorts sequential agents', () => {
    const nodes = [
      startNode(),
      agentNode('a3', 'Third'),
      agentNode('a1', 'First'),
      agentNode('a2', 'Second'),
      endNode(),
    ]
    // Only inter-agent edges — a1 is never a target → 1 root → sequential
    const edges = [
      edge('a1', 'a2'),
      edge('a2', 'a3'),
    ]
    const code = generateCSharpCode(nodes, edges)

    // In BuildSequential call, topo-sorted order: First, Second, Third
    const buildLine = code.split('\n').find((l) => l.includes('BuildSequential'))
    expect(buildLine).toBeDefined()
    const afterBuild = code.slice(code.indexOf('BuildSequential'))
    const firstIdx = afterBuild.indexOf('First')
    const secondIdx = afterBuild.indexOf('Second')
    const thirdIdx = afterBuild.indexOf('Third')
    expect(firstIdx).toBeLessThan(secondIdx)
    expect(secondIdx).toBeLessThan(thirdIdx)
  })
})
