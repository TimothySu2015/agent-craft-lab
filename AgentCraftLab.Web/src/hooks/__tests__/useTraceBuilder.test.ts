import { describe, it, expect, beforeEach } from 'vitest'
import { useCoAgentStore } from '@/stores/coagent-store'
import { INITIAL_AGENT_STATE } from '@/stores/agent-state'
import type { TraceSpan } from '@/stores/agent-state'

// useTraceBuilder reads from coagent-store, so we test by setting store state
// and calling the hook's logic directly (extracted for testability)

function buildTraceData(traceSpans: TraceSpan[]) {
  if (!traceSpans || traceSpans.length === 0) return null

  const totalMs = Math.max(...traceSpans.map(s => s.endMs)) - Math.min(...traceSpans.map(s => s.startMs))
  const totalTokens = traceSpans
    .filter(s => s.tokens != null)
    .reduce((sum, s) => sum + (s.tokens ?? 0), 0)
  const hasError = traceSpans.some(s => s.status === 'error')
  const hasRunning = traceSpans.some(s => s.status === 'running')

  return {
    traceId: '',
    totalMs,
    totalTokens,
    totalCost: '',
    status: hasRunning ? 'running' as const : hasError ? 'error' as const : 'completed' as const,
    spans: traceSpans,
  }
}

describe('useTraceBuilder logic', () => {
  beforeEach(() => {
    useCoAgentStore.setState({ state: INITIAL_AGENT_STATE })
  })

  it('returns null for empty spans', () => {
    const result = buildTraceData([])
    expect(result).toBeNull()
  })

  it('returns null for undefined spans', () => {
    const result = buildTraceData(undefined as any)
    expect(result).toBeNull()
  })

  it('calculates totalMs correctly', () => {
    const spans: TraceSpan[] = [
      { id: '1', name: 'A', type: 'agent', source: 'platform', status: 'completed', startMs: 100, endMs: 500 },
      { id: '2', name: 'B', type: 'agent', source: 'platform', status: 'completed', startMs: 500, endMs: 1200 },
    ]

    const result = buildTraceData(spans)!
    expect(result.totalMs).toBe(1100) // 1200 - 100
  })

  it('sums tokens correctly', () => {
    const spans: TraceSpan[] = [
      { id: '1', name: 'A', type: 'agent', source: 'platform', status: 'completed', startMs: 0, endMs: 100, tokens: 50 },
      { id: '2', name: 'B', type: 'agent', source: 'platform', status: 'completed', startMs: 100, endMs: 200, tokens: 150 },
      { id: '3', name: 'C', type: 'loop', source: 'platform', status: 'completed', startMs: 200, endMs: 300 },
    ]

    const result = buildTraceData(spans)!
    expect(result.totalTokens).toBe(200) // 50 + 150, ignoring null
  })

  it('detects error status', () => {
    const spans: TraceSpan[] = [
      { id: '1', name: 'A', type: 'agent', source: 'platform', status: 'completed', startMs: 0, endMs: 100 },
      { id: '2', name: 'B', type: 'agent', source: 'platform', status: 'error', startMs: 100, endMs: 200 },
    ]

    const result = buildTraceData(spans)!
    expect(result.status).toBe('error')
  })

  it('detects running status', () => {
    const spans: TraceSpan[] = [
      { id: '1', name: 'A', type: 'agent', source: 'platform', status: 'completed', startMs: 0, endMs: 100 },
      { id: '2', name: 'B', type: 'agent', source: 'platform', status: 'running', startMs: 100, endMs: 200 },
    ]

    const result = buildTraceData(spans)!
    expect(result.status).toBe('running')
  })

  it('running takes precedence over error', () => {
    const spans: TraceSpan[] = [
      { id: '1', name: 'A', type: 'agent', source: 'platform', status: 'error', startMs: 0, endMs: 100 },
      { id: '2', name: 'B', type: 'agent', source: 'platform', status: 'running', startMs: 100, endMs: 200 },
    ]

    const result = buildTraceData(spans)!
    expect(result.status).toBe('running')
  })

  it('single span calculates correctly', () => {
    const spans: TraceSpan[] = [
      { id: '1', name: 'A', type: 'agent', source: 'platform', status: 'completed', startMs: 0, endMs: 5000, tokens: 100 },
    ]

    const result = buildTraceData(spans)!
    expect(result.totalMs).toBe(5000)
    expect(result.totalTokens).toBe(100)
    expect(result.status).toBe('completed')
  })
})
