import { describe, it, expect } from 'vitest'
import { toWorkflowPayloadJson } from '../workflow-payload'
import type { Node, Edge } from '@xyflow/react'
import type { NodeData } from '@/types/workflow'
import type { WorkflowSettings } from '@/stores/workflow-store'

function makeNode(id: string, type: string, data: NodeData): Node<NodeData> {
  return { id, type, position: { x: 0, y: 0 }, data }
}

function makeEdge(source: string, target: string, sourceHandle?: string, targetHandle?: string): Edge {
  return {
    id: `e-${source}-${target}`,
    source,
    target,
    ...(sourceHandle && { sourceHandle }),
    ...(targetHandle && { targetHandle }),
  }
}

describe('toWorkflowPayloadJson', () => {
  it('filters out start and end nodes', () => {
    const nodes: Node<NodeData>[] = [
      makeNode('start-1', 'start', { type: 'start', name: 'Start' }),
      makeNode('agent-1', 'agent', {
        type: 'agent', name: 'A1', instructions: '', model: 'gpt-4o', provider: 'openai',
        endpoint: '', deploymentName: '', historyProvider: 'none', maxMessages: 20,
        middleware: '', tools: [], skills: [],
      }),
      makeNode('end-1', 'end', { type: 'end', name: 'End' }),
    ]
    const edges: Edge[] = [makeEdge('start-1', 'agent-1'), makeEdge('agent-1', 'end-1')]

    const result = JSON.parse(toWorkflowPayloadJson(nodes, edges))

    // Only agent node in payload
    expect(result.nodes).toHaveLength(1)
    expect(result.nodes[0].name).toBe('A1')
    expect(result.nodes[0].type).toBe('agent')

    // start→agent edge preserved, agent→end filtered (target not in payload)
    expect(result.connections).toHaveLength(1)
  })

  it('maps edges to connections with default ports', () => {
    const nodes: Node<NodeData>[] = [
      makeNode('agent-1', 'agent', {
        type: 'agent', name: 'A1', instructions: '', model: 'gpt-4o', provider: 'openai',
        endpoint: '', deploymentName: '', historyProvider: 'none', maxMessages: 20,
        middleware: '', tools: [], skills: [],
      }),
    ]
    const edges: Edge[] = [makeEdge('start-1', 'agent-1')]
    const result = JSON.parse(toWorkflowPayloadJson(nodes, edges))

    expect(result.connections[0]).toEqual({
      from: 'start-1',
      to: 'agent-1',
      fromOutput: 'output_1',
      toPort: 'input_1',
    })
  })

  it('preserves custom source/target handles', () => {
    const nodes: Node<NodeData>[] = [
      makeNode('cond-1', 'condition', {
        type: 'condition', name: 'Check', conditionType: 'contains', conditionExpression: '', maxIterations: 5,
      }),
      makeNode('agent-1', 'agent', {
        type: 'agent', name: 'A1', instructions: '', model: 'gpt-4o', provider: 'openai',
        endpoint: '', deploymentName: '', historyProvider: 'none', maxMessages: 20,
        middleware: '', tools: [], skills: [],
      }),
    ]
    const edges: Edge[] = [makeEdge('cond-1', 'agent-1', 'output_2', 'input_1')]
    const result = JSON.parse(toWorkflowPayloadJson(nodes, edges))

    expect(result.connections[0].fromOutput).toBe('output_2')
    expect(result.connections[0].toPort).toBe('input_1')
  })

  it('uses default settings when none provided', () => {
    const result = JSON.parse(toWorkflowPayloadJson([], []))

    expect(result.workflowSettings.type).toBe('auto')
    expect(result.workflowSettings.maxTurns).toBe(10)
  })

  it('applies custom settings', () => {
    const settings: WorkflowSettings = {
      type: 'handoff',
      maxTurns: 25,
      terminationStrategy: 'keyword',
      terminationKeyword: 'DONE',
    }
    const result = JSON.parse(toWorkflowPayloadJson([], [], settings))

    expect(result.workflowSettings.type).toBe('handoff')
    expect(result.workflowSettings.maxTurns).toBe(25)
    expect(result.workflowSettings.terminationStrategy).toBe('keyword')
    expect(result.workflowSettings.terminationKeyword).toBe('DONE')
  })

  it('omits terminationStrategy when set to none', () => {
    const settings: WorkflowSettings = {
      type: 'auto',
      maxTurns: 10,
      terminationStrategy: 'none',
    }
    const result = JSON.parse(toWorkflowPayloadJson([], [], settings))

    expect(result.workflowSettings.terminationStrategy).toBeUndefined()
  })

  it('serializes contextPassing when not default', () => {
    const settings: WorkflowSettings = {
      type: 'sequential',
      maxTurns: 10,
      contextPassing: 'accumulate',
    }
    const result = JSON.parse(toWorkflowPayloadJson([], [], settings))

    expect(result.workflowSettings.contextPassing).toBe('accumulate')
  })

  it('omits contextPassing when set to previous-only', () => {
    const settings: WorkflowSettings = {
      type: 'auto',
      maxTurns: 10,
      contextPassing: 'previous-only',
    }
    const result = JSON.parse(toWorkflowPayloadJson([], [], settings))

    expect(result.workflowSettings.contextPassing).toBeUndefined()
  })

  it('includes node id and spreads all data fields', () => {
    const nodes: Node<NodeData>[] = [
      makeNode('code-1', 'code', {
        type: 'code', name: 'Transform', transformType: 'template',
        pattern: '', replacement: '', template: '{{input}}',
        maxLength: 0, delimiter: '\\n', splitIndex: 0,
      }),
    ]
    const result = JSON.parse(toWorkflowPayloadJson(nodes, []))

    expect(result.nodes[0].id).toBe('code-1')
    expect(result.nodes[0].transformType).toBe('template')
    expect(result.nodes[0].template).toBe('{{input}}')
  })
})
