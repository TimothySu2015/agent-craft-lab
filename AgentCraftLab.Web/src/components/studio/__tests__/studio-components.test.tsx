/**
 * StatsLine + ConsolePanel 測試
 * - StatsLine：純展示元件，依 truthy/positive 值條件渲染統計項目
 * - ConsolePanel：從 coagent-store 讀取 recentLogs，顯示可收合的執行日誌
 */
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, act } from '@testing-library/react'
import { StatsLine } from '../StatsLine'
import type { StatsLineProps } from '../StatsLine'

// ── StatsLine Mocks ──

vi.mock('@/lib/format', () => ({
  formatDuration: (ms: number) => ms >= 1000 ? `${(ms / 1000).toFixed(1)}s` : `${ms}ms`,
}))

// ── ConsolePanel Mocks ──

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string) => key }),
}))

vi.mock('../StatsLine', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../StatsLine')>()
  return {
    ...actual,
    StatsLine: Object.assign(
      (props: StatsLineProps) => <div data-testid="stats-line" />,
      { __esModule: true },
    ),
  }
})

let mockState: any = { nodeStates: {}, pendingHumanInput: null, recentLogs: [], executionStats: undefined }

vi.mock('@/stores/coagent-store', () => ({
  useCoAgentStore: (selector: any) => selector({ state: mockState }),
}))

// ── StatsLine Tests ──

// StatsLine is mocked for ConsolePanel, so we need the real implementation.
// We test StatsLine using the actual module imported before mock takes effect.
// Since vi.mock is hoisted, we use vi.importActual to get the real StatsLine.
let RealStatsLine: typeof StatsLine

beforeEach(async () => {
  const mod = await vi.importActual<typeof import('../StatsLine')>('../StatsLine')
  RealStatsLine = mod.StatsLine
  mockState = { nodeStates: {}, pendingHumanInput: null, recentLogs: [], executionStats: undefined }
})

describe('StatsLine', () => {
  it('renders nothing when all props are empty/zero', () => {
    const { container } = render(<RealStatsLine />)
    // The container div exists but should have no stat items (no <span> children)
    const spans = container.querySelectorAll('span')
    expect(spans).toHaveLength(0)
  })

  it('renders steps when provided', () => {
    render(<RealStatsLine steps={5} />)
    expect(screen.getByText('5')).toBeInTheDocument()
  })

  it('renders tokens count', () => {
    render(<RealStatsLine tokens={1500} />)
    expect(screen.getByText(/1,500/)).toBeInTheDocument()
  })

  it('renders cost', () => {
    render(<RealStatsLine cost="$0.05" />)
    expect(screen.getByText('$0.05')).toBeInTheDocument()
  })

  it('renders formatted duration', () => {
    render(<RealStatsLine durationMs={3200} />)
    expect(screen.getByText('3.2s')).toBeInTheDocument()
  })

  it('renders model name', () => {
    render(<RealStatsLine model="gpt-4o" />)
    expect(screen.getByText('gpt-4o')).toBeInTheDocument()
  })

  it('renders multiple stats together', () => {
    render(<RealStatsLine steps={3} tokens={2000} cost="$0.10" durationMs={5000} model="gpt-4o" tools={2} />)
    expect(screen.getByText('3')).toBeInTheDocument()
    expect(screen.getByText('2')).toBeInTheDocument()
    expect(screen.getByText(/2,000/)).toBeInTheDocument()
    expect(screen.getByText('$0.10')).toBeInTheDocument()
    expect(screen.getByText('5.0s')).toBeInTheDocument()
    expect(screen.getByText('gpt-4o')).toBeInTheDocument()
  })

  it('applies custom className', () => {
    const { container } = render(<RealStatsLine className="my-class" />)
    expect(container.firstElementChild).toHaveClass('my-class')
  })
})

// ── ConsolePanel Tests ──

// Import ConsolePanel after mocks are set up (vi.mock is hoisted)
// eslint-disable-next-line @typescript-eslint/no-require-imports
import { ConsolePanel } from '../ConsolePanel'

describe('ConsolePanel', () => {
  it('renders console header', () => {
    render(<ConsolePanel />)
    // The header shows the translated key 'console.title'
    expect(screen.getByText('console.title')).toBeInTheDocument()
  })

  it('shows log entries when logs exist', async () => {
    mockState = {
      ...mockState,
      recentLogs: [
        { ts: '12:00:01', level: 'info', message: 'Agent started' },
        { ts: '12:00:02', level: 'success', message: 'Task completed' },
      ],
    }
    render(<ConsolePanel />)

    // Expand the panel first by clicking the header button
    const headerButton = screen.getByText('console.title').closest('button')!
    await act(async () => {
      fireEvent.click(headerButton)
    })

    expect(screen.getByText('Agent started')).toBeInTheDocument()
    expect(screen.getByText('Task completed')).toBeInTheDocument()
  })

  it('shows expand/collapse toggle', () => {
    render(<ConsolePanel />)
    // The header itself is the toggle button
    const headerButton = screen.getByText('console.title').closest('button')
    expect(headerButton).toBeInTheDocument()
  })
})
