import { describe, it, expect } from 'vitest'
import { NodeStatus, INITIAL_AGENT_STATE } from '../agent-state'
import type { AgentState } from '../agent-state'

describe('NodeStatus', () => {
  it('has expected constant values', () => {
    expect(NodeStatus.Executing).toBe('executing')
    expect(NodeStatus.Completed).toBe('completed')
  })
})

describe('INITIAL_AGENT_STATE', () => {
  it('starts with empty nodeStates', () => {
    expect(INITIAL_AGENT_STATE.nodeStates).toEqual({})
  })

  it('starts with no pending human input', () => {
    expect(INITIAL_AGENT_STATE.pendingHumanInput).toBeNull()
  })

  it('starts with empty logs', () => {
    expect(INITIAL_AGENT_STATE.recentLogs).toEqual([])
  })

  it('has no executionStats initially', () => {
    expect(INITIAL_AGENT_STATE.executionStats).toBeUndefined()
  })

  it('satisfies AgentState type shape', () => {
    const state: AgentState = INITIAL_AGENT_STATE
    expect(state).toHaveProperty('nodeStates')
    expect(state).toHaveProperty('pendingHumanInput')
    expect(state).toHaveProperty('recentLogs')
  })
})
