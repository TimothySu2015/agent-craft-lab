/**
 * NodeShell 元件測試 — 驗證 Handle 渲染邏輯、顏色映射、執行狀態樣式。
 */
import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { Bot } from 'lucide-react'

// Mock @xyflow/react Handle + hooks
vi.mock('@xyflow/react', () => ({
  Handle: ({ id, type, style, className }: any) => (
    <div
      data-testid={`handle-${type}-${id ?? 'default'}`}
      style={style}
      className={className}
    />
  ),
  Position: { Left: 'left', Right: 'right', Top: 'top', Bottom: 'bottom' },
  useNodeId: () => 'test-node',
  useUpdateNodeInternals: () => vi.fn(),
}))

// Mock coagent store — return configurable state
let mockNodeStates: Record<string, string> = {}
vi.mock('@/stores/coagent-store', () => ({
  useCoAgentStore: (selector: any) => selector({ state: { nodeStates: mockNodeStates } }),
}))

// Mock workflow store — return default LR direction
vi.mock('@/stores/workflow-store', () => ({
  useWorkflowStore: (selector: any) => selector({ layoutDirection: 'LR' }),
}))

import { NodeShell } from '../shared/NodeShell'

describe('NodeShell', () => {
  describe('handles', () => {
    it('renders input handle when inputs > 0', () => {
      const { container } = render(
        <NodeShell color="blue" icon={Bot} title="T" inputs={1} outputs={0} />,
      )
      expect(container.querySelector('[data-testid="handle-target-default"]')).toBeInTheDocument()
    })

    it('does not render input handle when inputs = 0', () => {
      const { container } = render(
        <NodeShell color="blue" icon={Bot} title="T" inputs={0} outputs={0} />,
      )
      expect(container.querySelector('[data-testid="handle-target-default"]')).not.toBeInTheDocument()
    })

    it('renders single output handle for outputs = 1', () => {
      const { container } = render(
        <NodeShell color="blue" icon={Bot} title="T" inputs={0} outputs={1} />,
      )
      expect(container.querySelector('[data-testid="handle-source-default"]')).toBeInTheDocument()
    })

    it('renders multiple numbered handles for outputs >= 2', () => {
      const { container } = render(
        <NodeShell color="blue" icon={Bot} title="T" inputs={0} outputs={3} />,
      )
      expect(container.querySelector('[data-testid="handle-source-output_1"]')).toBeInTheDocument()
      expect(container.querySelector('[data-testid="handle-source-output_2"]')).toBeInTheDocument()
      expect(container.querySelector('[data-testid="handle-source-output_3"]')).toBeInTheDocument()
    })

    it('positions multi-output handles evenly', () => {
      const { container } = render(
        <NodeShell color="blue" icon={Bot} title="T" inputs={0} outputs={2} />,
      )
      const h1 = container.querySelector('[data-testid="handle-source-output_1"]')
      const h2 = container.querySelector('[data-testid="handle-source-output_2"]')

      // 2 outputs → pct = (1/3)*100 ≈ 33.33%, (2/3)*100 ≈ 66.66%
      expect(h1?.getAttribute('style')).toContain('33.33')
      expect(h2?.getAttribute('style')).toContain('66.66')
    })

    it('last output handle uses red border, others green', () => {
      const { container } = render(
        <NodeShell color="blue" icon={Bot} title="T" inputs={0} outputs={2} />,
      )
      const h1 = container.querySelector('[data-testid="handle-source-output_1"]')
      const h2 = container.querySelector('[data-testid="handle-source-output_2"]')

      expect(h1?.className).toContain('border-green-500')
      expect(h2?.className).toContain('border-red-500')
    })
  })

  describe('title and subtitle', () => {
    it('renders title', () => {
      render(<NodeShell color="blue" icon={Bot} title="MyNode" inputs={0} outputs={0} />)
      expect(screen.getByText('MyNode')).toBeInTheDocument()
    })

    it('renders subtitle when provided', () => {
      render(<NodeShell color="blue" icon={Bot} title="T" subtitle="Sub" inputs={0} outputs={0} />)
      expect(screen.getByText('Sub')).toBeInTheDocument()
    })

    it('omits subtitle when not provided', () => {
      render(<NodeShell color="blue" icon={Bot} title="T" inputs={0} outputs={0} />)
      expect(screen.queryByText('Sub')).not.toBeInTheDocument()
    })
  })

  describe('children', () => {
    it('renders children in body', () => {
      render(
        <NodeShell color="blue" icon={Bot} title="T" inputs={0} outputs={0}>
          <p>Body content</p>
        </NodeShell>,
      )
      expect(screen.getByText('Body content')).toBeInTheDocument()
    })

    it('omits body wrapper when no children', () => {
      const { container } = render(
        <NodeShell color="blue" icon={Bot} title="T" inputs={0} outputs={0} />,
      )
      // Body wrapper has specific classes
      expect(container.querySelectorAll('.pb-2')).toHaveLength(0)
    })
  })

  describe('selection state', () => {
    it('applies ring when selected', () => {
      const { container } = render(
        <NodeShell color="blue" icon={Bot} title="T" inputs={0} outputs={0} selected />,
      )
      const root = container.firstElementChild!
      expect(root.className).toContain('ring-blue-500')
    })
  })

  describe('execution state', () => {
    it('shows amber ring for executing state', () => {
      mockNodeStates = { TestNode: 'executing' }
      const { container } = render(
        <NodeShell color="blue" icon={Bot} title="TestNode" inputs={0} outputs={0} />,
      )
      const root = container.firstElementChild!
      expect(root.className).toContain('ring-amber-400')
    })

    it('shows green ring for completed state', () => {
      mockNodeStates = { TestNode: 'completed' }
      const { container } = render(
        <NodeShell color="blue" icon={Bot} title="TestNode" inputs={0} outputs={0} />,
      )
      const root = container.firstElementChild!
      expect(root.className).toContain('ring-green-400')
    })

    it('shows pulse indicator for executing', () => {
      mockNodeStates = { ExecNode: 'executing' }
      const { container } = render(
        <NodeShell color="blue" icon={Bot} title="ExecNode" inputs={0} outputs={0} />,
      )
      expect(container.querySelector('.animate-pulse')).toBeInTheDocument()
    })

    it('shows completed indicator dot', () => {
      mockNodeStates = { DoneNode: 'completed' }
      const { container } = render(
        <NodeShell color="blue" icon={Bot} title="DoneNode" inputs={0} outputs={0} />,
      )
      expect(container.querySelector('.bg-green-400')).toBeInTheDocument()
    })
  })

  describe('color fallback', () => {
    it('falls back to blue when unknown color provided', () => {
      const { container } = render(
        <NodeShell color="nonexistent" icon={Bot} title="T" inputs={0} outputs={0} />,
      )
      const root = container.firstElementChild!
      // Should use blue fallback border
      expect(root.className).toContain('border-blue-500')
    })
  })
})
