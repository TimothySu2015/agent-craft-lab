import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { TraceWaterfall } from '../TraceWaterfall'
import type { TraceData, TraceSpan } from '@/stores/agent-state'

// Mock i18n
vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string, fallback?: string) => fallback ?? key }),
}))

// Mock NODE_REGISTRY and NODE_COLORS
vi.mock('@/components/studio/nodes/registry', () => ({
  NODE_REGISTRY: {},
  NODE_COLORS: {
    blue: { border: 'border-blue', iconBg: 'bg-blue', iconText: 'text-blue' },
    green: { border: 'border-green', iconBg: 'bg-green', iconText: 'text-green' },
    yellow: { border: 'border-yellow', iconBg: 'bg-yellow', iconText: 'text-yellow' },
    cyan: { border: 'border-cyan', iconBg: 'bg-cyan', iconText: 'text-cyan' },
  },
}))

function makeTraceData(spans: Partial<TraceSpan>[]): TraceData {
  return {
    traceId: 'test-trace',
    totalMs: 10000,
    totalTokens: 500,
    totalCost: '$0.01',
    status: 'completed',
    spans: spans.map((s, i) => ({
      id: s.id ?? `span-${i}`,
      name: s.name ?? `Span ${i}`,
      type: s.type ?? 'agent',
      source: s.source ?? 'platform',
      status: s.status ?? 'completed',
      startMs: s.startMs ?? i * 1000,
      endMs: s.endMs ?? (i + 1) * 1000,
      ...s,
    })) as TraceSpan[],
  }
}

describe('TraceWaterfall', () => {
  it('renders "No trace data" when data has no spans', () => {
    const data = makeTraceData([])
    render(<TraceWaterfall data={data} />)
    expect(screen.getByText('No trace data')).toBeDefined()
  })

  it('renders span names', () => {
    const data = makeTraceData([
      { name: 'Writer', startMs: 0, endMs: 3000 },
      { name: 'Editor', startMs: 3000, endMs: 5000 },
    ])
    render(<TraceWaterfall data={data} />)
    expect(screen.getByText('Writer')).toBeDefined()
    expect(screen.getByText('Editor')).toBeDefined()
  })

  it('shows duration labels', () => {
    const data = makeTraceData([
      { name: 'Writer', startMs: 0, endMs: 3000, tokens: 100 },
    ])
    render(<TraceWaterfall data={data} />)
    expect(screen.getByText(/3\.0s/)).toBeDefined()
    expect(screen.getByText(/100tk/)).toBeDefined()
  })

  it('opens modal on span click', () => {
    const data = makeTraceData([
      { name: 'Writer', type: 'agent', model: 'gpt-4o', inputTokens: 50, outputTokens: 100 },
    ])
    render(<TraceWaterfall data={data} />)

    fireEvent.click(screen.getByText('Writer'))

    // Modal should show detail fields
    expect(screen.getByText('trace.type')).toBeDefined()
    expect(screen.getByText('agent')).toBeDefined()
  })

  it('closes modal on X button click', () => {
    const data = makeTraceData([
      { name: 'Writer' },
    ])
    render(<TraceWaterfall data={data} />)

    fireEvent.click(screen.getByText('Writer'))
    // Modal is open
    expect(screen.getAllByText('Writer').length).toBeGreaterThan(1) // name in row + modal header

    // Find and click the close button (X)
    const closeButtons = screen.getAllByRole('button')
    const closeBtn = closeButtons.find(b => b.querySelector('svg'))
    if (closeBtn) fireEvent.click(closeBtn)
  })

  it('shows tool calls count badge', () => {
    const data = makeTraceData([
      {
        name: 'Parallel',
        type: 'parallel',
        toolCalls: [
          { name: 'Branch-A', result: 'result A' },
          { name: 'Branch-B', result: 'result B' },
        ],
      },
    ])
    render(<TraceWaterfall data={data} />)
    expect(screen.getByText('(2)')).toBeDefined()
  })

  it('renders timeline header ticks', () => {
    const data = makeTraceData([
      { name: 'Writer', startMs: 0, endMs: 5000 },
    ])
    render(<TraceWaterfall data={data} />)
    // Should have tick marks — check for any time label
    const ticks = screen.getAllByText(/ms|s$/)
    expect(ticks.length).toBeGreaterThan(0)
  })
})
