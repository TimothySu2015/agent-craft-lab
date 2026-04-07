import { describe, it, expect } from 'vitest'
import { NODE_REGISTRY, NODE_COLORS, type NodeTypeConfig } from '../registry'
import type { NodeType } from '@/types/workflow'

const ALL_NODE_TYPES: NodeType[] = [
  'agent', 'rag', 'condition', 'loop', 'router',
  'a2a-agent', 'human', 'code', 'iteration', 'parallel',
  'http-request', 'autonomous', 'start', 'end',
]

describe('NODE_REGISTRY', () => {
  it('has entries for all 14 node types', () => {
    for (const type of ALL_NODE_TYPES) {
      expect(NODE_REGISTRY[type], `missing registry entry for: ${type}`).toBeDefined()
    }
    expect(Object.keys(NODE_REGISTRY)).toHaveLength(ALL_NODE_TYPES.length)
  })

  it.each(ALL_NODE_TYPES)('%s has complete config', (type) => {
    const config: NodeTypeConfig = NODE_REGISTRY[type]
    expect(config.type).toBe(type)
    expect(config.labelKey).toBeTruthy()
    expect(config.icon).toBeDefined()
    expect(typeof config.color).toBe('string')
    expect(typeof config.inputs).toBe('number')
    expect(typeof config.outputs).toBe('number')
    expect(typeof config.defaultData).toBe('function')
  })

  it.each(ALL_NODE_TYPES)('%s defaultData returns correct type', (type) => {
    const data = NODE_REGISTRY[type].defaultData('TestName')
    expect(data.type).toBe(type)
    expect(data.name).toBe('TestName')
  })

  it('start has 0 inputs, 1 output', () => {
    expect(NODE_REGISTRY.start.inputs).toBe(0)
    expect(NODE_REGISTRY.start.outputs).toBe(1)
  })

  it('end has 1 input, 0 outputs', () => {
    expect(NODE_REGISTRY.end.inputs).toBe(1)
    expect(NODE_REGISTRY.end.outputs).toBe(0)
  })

  it('condition and loop have 2 outputs (True/False, Body/Exit)', () => {
    expect(NODE_REGISTRY.condition.outputs).toBe(2)
    expect(NODE_REGISTRY.loop.outputs).toBe(2)
  })

  it('human has 2 outputs (Approve/Reject)', () => {
    expect(NODE_REGISTRY.human.outputs).toBe(2)
  })

  it('iteration has 2 outputs (Body/Done)', () => {
    expect(NODE_REGISTRY.iteration.outputs).toBe(2)
  })

  it('router has 3 default outputs', () => {
    expect(NODE_REGISTRY.router.outputs).toBe(3)
  })

  it('parallel has 3 default outputs', () => {
    expect(NODE_REGISTRY.parallel.outputs).toBe(3)
  })

  it('agent defaultData includes tools and skills arrays', () => {
    const data = NODE_REGISTRY.agent.defaultData('A') as any
    expect(Array.isArray(data.tools)).toBe(true)
    expect(Array.isArray(data.skills)).toBe(true)
  })

  it('autonomous defaultData includes mcpServers and a2AAgents', () => {
    const data = NODE_REGISTRY.autonomous.defaultData('Auto') as any
    expect(Array.isArray(data.mcpServers)).toBe(true)
    expect(Array.isArray(data.a2AAgents)).toBe(true)
    expect(data.maxIterations).toBe(25)
  })

  it('rag defaultData has embedding config', () => {
    const data = NODE_REGISTRY.rag.defaultData('R') as any
    expect(data.ragChunkSize).toBe(512)
    expect(data.ragTopK).toBe(5)
    expect(data.ragEmbeddingModel).toBeTruthy()
  })
})

describe('NODE_COLORS', () => {
  it('has all colors used by NODE_REGISTRY', () => {
    const usedColors = new Set(Object.values(NODE_REGISTRY).map((c) => c.color))
    for (const color of usedColors) {
      expect(NODE_COLORS[color], `missing color: ${color}`).toBeDefined()
    }
  })

  it('each color has border, iconBg, iconText classes', () => {
    for (const [name, classes] of Object.entries(NODE_COLORS)) {
      expect(classes.border, `${name}.border`).toBeTruthy()
      expect(classes.iconBg, `${name}.iconBg`).toBeTruthy()
      expect(classes.iconText, `${name}.iconText`).toBeTruthy()
    }
  })
})
