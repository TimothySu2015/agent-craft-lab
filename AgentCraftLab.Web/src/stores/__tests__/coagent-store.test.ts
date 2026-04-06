import { describe, it, expect, beforeEach } from 'vitest'
import { useCoAgentStore } from '../coagent-store'
import { INITIAL_AGENT_STATE } from '../agent-state'
import type { AgentState } from '../agent-state'

describe('useCoAgentStore', () => {
  beforeEach(() => {
    // Reset store to initial state before each test
    useCoAgentStore.setState({ state: INITIAL_AGENT_STATE })
  })

  it('initializes with INITIAL_AGENT_STATE', () => {
    const { state } = useCoAgentStore.getState()
    expect(state).toEqual(INITIAL_AGENT_STATE)
  })

  it('updates state via setState action', () => {
    const newState: AgentState = {
      nodeStates: { 'Agent-1': 'executing', 'Agent-2': 'completed' },
      pendingHumanInput: null,
      recentLogs: [{ ts: '2026-01-01T00:00:00Z', level: 'info', message: 'hello' }],
    }

    useCoAgentStore.getState().setState(newState)

    const { state } = useCoAgentStore.getState()
    expect(state.nodeStates).toEqual({ 'Agent-1': 'executing', 'Agent-2': 'completed' })
    expect(state.recentLogs).toHaveLength(1)
    expect(state.recentLogs[0].message).toBe('hello')
  })

  it('updates state with pending human input', () => {
    const withHuman: AgentState = {
      nodeStates: {},
      pendingHumanInput: {
        prompt: 'Please approve',
        inputType: 'approval',
      },
      recentLogs: [],
    }

    useCoAgentStore.getState().setState(withHuman)

    const { state } = useCoAgentStore.getState()
    expect(state.pendingHumanInput).not.toBeNull()
    expect(state.pendingHumanInput!.prompt).toBe('Please approve')
    expect(state.pendingHumanInput!.inputType).toBe('approval')
  })

  it('replaces entire state on each setState call', () => {
    const first: AgentState = {
      nodeStates: { A: 'executing' },
      pendingHumanInput: null,
      recentLogs: [{ ts: '', level: 'info', message: 'log1' }],
    }
    const second: AgentState = {
      nodeStates: {},
      pendingHumanInput: null,
      recentLogs: [],
    }

    useCoAgentStore.getState().setState(first)
    expect(useCoAgentStore.getState().state.nodeStates).toHaveProperty('A')

    useCoAgentStore.getState().setState(second)
    expect(useCoAgentStore.getState().state.nodeStates).toEqual({})
    expect(useCoAgentStore.getState().state.recentLogs).toHaveLength(0)
  })

  it('supports executionStats in state', () => {
    const withStats: AgentState = {
      nodeStates: {},
      pendingHumanInput: null,
      recentLogs: [],
      executionStats: {
        durationMs: 5000,
        totalTokens: 1234,
        totalSteps: 3,
        totalToolCalls: 2,
        estimatedCost: '$0.05',
      },
    }

    useCoAgentStore.getState().setState(withStats)

    const stats = useCoAgentStore.getState().state.executionStats
    expect(stats).toBeDefined()
    expect(stats!.durationMs).toBe(5000)
    expect(stats!.totalTokens).toBe(1234)
    expect(stats!.estimatedCost).toBe('$0.05')
  })
})
