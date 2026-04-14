import { describe, it, expect } from 'vitest'
import { toWorkflowPayloadJson } from '../workflow-payload'
import type { Node, Edge } from '@xyflow/react'
import type { NodeData, AgentNodeData, ConditionNodeData, CodeNodeData } from '@/types/workflow'
import type { WorkflowSettings } from '@/stores/workflow-store'

function makeAgent(name: string): AgentNodeData {
  return {
    type: 'agent',
    name,
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

function makeNode(id: string, type: string, data: NodeData): Node<NodeData> {
  return { id, type, position: { x: 0, y: 0 }, data }
}

function makeEdge(source: string, target: string, sourceHandle?: string): Edge {
  return {
    id: `e-${source}-${target}`,
    source,
    target,
    ...(sourceHandle && { sourceHandle }),
  }
}

describe('toWorkflowPayloadJson (Schema v2)', () => {
  it('emits version 2.0 and nested settings.strategy', () => {
    const result = JSON.parse(toWorkflowPayloadJson([], []))
    expect(result.version).toBe('2.0')
    expect(result.settings.strategy).toBe('auto')
    expect(result.settings.maxTurns).toBe(10)
  })

  it('filters out start and end nodes', () => {
    const nodes: Node<NodeData>[] = [
      makeNode('start-1', 'start', { type: 'start', name: 'Start' }),
      makeNode('agent-1', 'agent', makeAgent('A1')),
      makeNode('end-1', 'end', { type: 'end', name: 'End' }),
    ]
    const edges: Edge[] = [makeEdge('start-1', 'agent-1'), makeEdge('agent-1', 'end-1')]

    const result = JSON.parse(toWorkflowPayloadJson(nodes, edges))

    expect(result.nodes).toHaveLength(1)
    expect(result.nodes[0].name).toBe('A1')
    expect(result.nodes[0].type).toBe('agent')
    // start→agent edge preserved, agent→end filtered (target not in payload)
    expect(result.connections).toHaveLength(1)
  })

  it('spreads nested NodeData as pass-through', () => {
    const nodes: Node<NodeData>[] = [makeNode('agent-1', 'agent', makeAgent('A1'))]
    const result = JSON.parse(toWorkflowPayloadJson(nodes, []))
    expect(result.nodes[0]).toMatchObject({
      id: 'agent-1',
      type: 'agent',
      name: 'A1',
      model: { provider: 'openai', model: 'gpt-4o' },
      output: { kind: 'text' },
      history: { provider: 'none', maxMessages: 20 },
    })
  })

  it('maps edges to connections with single port field', () => {
    const nodes: Node<NodeData>[] = [makeNode('agent-1', 'agent', makeAgent('A1'))]
    const edges: Edge[] = [makeEdge('start-1', 'agent-1')]
    const result = JSON.parse(toWorkflowPayloadJson(nodes, edges))

    expect(result.connections[0]).toEqual({
      from: 'start-1',
      to: 'agent-1',
      port: 'output_1',
    })
  })

  it('preserves custom source handles as port', () => {
    const condition: ConditionNodeData = {
      type: 'condition',
      name: 'Check',
      condition: { kind: 'contains', value: 'yes' },
    }
    const nodes: Node<NodeData>[] = [
      makeNode('cond-1', 'condition', condition),
      makeNode('agent-1', 'agent', makeAgent('A1')),
    ]
    const edges: Edge[] = [makeEdge('cond-1', 'agent-1', 'output_2')]
    const result = JSON.parse(toWorkflowPayloadJson(nodes, edges))

    expect(result.connections[0].port).toBe('output_2')
  })

  it('applies custom settings.strategy', () => {
    const settings: WorkflowSettings = { type: 'handoff', maxTurns: 25 }
    const result = JSON.parse(toWorkflowPayloadJson([], [], settings))

    expect(result.settings.strategy).toBe('handoff')
    expect(result.settings.maxTurns).toBe(25)
  })

  it('serializes contextPassing when not default', () => {
    const settings: WorkflowSettings = {
      type: 'sequential',
      maxTurns: 10,
      contextPassing: 'accumulate',
    }
    const result = JSON.parse(toWorkflowPayloadJson([], [], settings))
    expect(result.settings.contextPassing).toBe('accumulate')
  })

  it('omits contextPassing when set to previous-only', () => {
    const settings: WorkflowSettings = {
      type: 'auto',
      maxTurns: 10,
      contextPassing: 'previous-only',
    }
    const result = JSON.parse(toWorkflowPayloadJson([], [], settings))
    expect(result.settings.contextPassing).toBeUndefined()
  })

  it('spreads code node nested fields', () => {
    const code: CodeNodeData = {
      type: 'code',
      name: 'Transform',
      kind: 'template',
      expression: '{{input}}',
      delimiter: '\n',
      splitIndex: 0,
      maxLength: 0,
    }
    const nodes: Node<NodeData>[] = [makeNode('code-1', 'code', code)]
    const result = JSON.parse(toWorkflowPayloadJson(nodes, []))

    expect(result.nodes[0].id).toBe('code-1')
    expect(result.nodes[0].kind).toBe('template')
    expect(result.nodes[0].expression).toBe('{{input}}')
  })

  it('puts type discriminator as first property in node JSON', () => {
    const nodes: Node<NodeData>[] = [makeNode('agent-1', 'agent', makeAgent('A1'))]
    const json = toWorkflowPayloadJson(nodes, [])

    // type 必須在 id 之前 — .NET JsonPolymorphic 需要 discriminator 在物件開頭
    const nodeJson = json.match(/"nodes":\[(\{[^}]+)/)?.[1] ?? ''
    const typePos = nodeJson.indexOf('"type"')
    const idPos = nodeJson.indexOf('"id"')
    expect(typePos).toBeLessThan(idPos)
    expect(typePos).toBeGreaterThanOrEqual(0)
  })

  describe('hooks serialization', () => {
    it('maps code hook with kind and expression', () => {
      const settings: WorkflowSettings = {
        type: 'auto',
        maxTurns: 10,
        hooks: {
          onInput: { type: 'code', kind: 'template', expression: 'Hello {{input}}' },
        },
      }
      const result = JSON.parse(toWorkflowPayloadJson([], [], settings))

      expect(result.hooks.onInput).toEqual({
        type: 'code',
        kind: 'template',
        expression: 'Hello {{input}}',
      })
    })

    it('includes regex replacement when present', () => {
      const settings: WorkflowSettings = {
        type: 'auto',
        maxTurns: 10,
        hooks: {
          preExecute: { type: 'code', kind: 'regex', expression: '\\d+', replacement: '[NUM]' },
        },
      }
      const result = JSON.parse(toWorkflowPayloadJson([], [], settings))

      expect(result.hooks.preExecute).toEqual({
        type: 'code',
        kind: 'regex',
        expression: '\\d+',
        replacement: '[NUM]',
      })
    })

    it('includes split delimiter and index when present', () => {
      const settings: WorkflowSettings = {
        type: 'auto',
        maxTurns: 10,
        hooks: {
          onInput: { type: 'code', kind: 'split', expression: '{{input}}', delimiter: ',', splitIndex: 2 },
        },
      }
      const result = JSON.parse(toWorkflowPayloadJson([], [], settings))

      expect(result.hooks.onInput.delimiter).toBe(',')
      expect(result.hooks.onInput.splitIndex).toBe(2)
    })

    it('includes truncate maxLength when present', () => {
      const settings: WorkflowSettings = {
        type: 'auto',
        maxTurns: 10,
        hooks: {
          onInput: { type: 'code', kind: 'truncate', expression: '{{input}}', maxLength: 500 },
        },
      }
      const result = JSON.parse(toWorkflowPayloadJson([], [], settings))

      expect(result.hooks.onInput.maxLength).toBe(500)
    })

    it('maps webhook hook with url, method, and bodyTemplate', () => {
      const settings: WorkflowSettings = {
        type: 'auto',
        maxTurns: 10,
        hooks: {
          onComplete: {
            type: 'webhook',
            url: 'https://example.com/hook',
            method: 'post',
            bodyTemplate: '{"text":"{{output}}"}',
            headers: [{ name: 'Authorization', value: 'Bearer xxx' }],
          },
        },
      }
      const result = JSON.parse(toWorkflowPayloadJson([], [], settings))

      expect(result.hooks.onComplete).toEqual({
        type: 'webhook',
        url: 'https://example.com/hook',
        method: 'post',
        bodyTemplate: '{"text":"{{output}}"}',
        headers: [{ name: 'Authorization', value: 'Bearer xxx' }],
      })
    })

    it('includes blockPattern when present', () => {
      const settings: WorkflowSettings = {
        type: 'auto',
        maxTurns: 10,
        hooks: {
          onInput: { type: 'code', kind: 'template', expression: '{{input}}', blockPattern: 'ignore.*instructions' },
        },
      }
      const result = JSON.parse(toWorkflowPayloadJson([], [], settings))

      expect(result.hooks.onInput.blockPattern).toBe('ignore.*instructions')
    })

    it('omits empty optional fields', () => {
      const settings: WorkflowSettings = {
        type: 'auto',
        maxTurns: 10,
        hooks: {
          onInput: { type: 'code', kind: 'template', expression: '{{input}}', replacement: '', maxLength: 0 },
        },
      }
      const result = JSON.parse(toWorkflowPayloadJson([], [], settings))

      expect(result.hooks.onInput).toEqual({
        type: 'code',
        kind: 'template',
        expression: '{{input}}',
      })
    })

    it('omits hooks when empty', () => {
      const settings: WorkflowSettings = { type: 'auto', maxTurns: 10, hooks: {} }
      const result = JSON.parse(toWorkflowPayloadJson([], [], settings))

      expect(result.hooks).toBeUndefined()
    })
  })
})
