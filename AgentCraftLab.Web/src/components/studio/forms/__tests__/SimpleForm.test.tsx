/**
 * SimpleForm 測試 — 驗證 Router/Parallel 的列表解析與增刪、
 * Iteration 的條件渲染。
 *
 * F3 重寫：routes / branches 從 CSV 字串 改為 RouteConfig[] / BranchConfig[] 結構化陣列。
 */
import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { SimpleForm } from '../SimpleForm'
import type { NodeData, RouterNodeData, ParallelNodeData, IterationNodeData } from '@/types/workflow'

// Mock i18n — 直接回傳 key
vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string) => key }),
}))

// Mock api (RagForm 使用)
vi.mock('@/lib/api', () => ({
  api: { knowledgeBases: { list: vi.fn().mockResolvedValue([]) } },
}))

// Mock PropertiesPanel Field
vi.mock('../../PropertiesPanel', () => ({
  Field: ({ label, children }: any) => <div data-testid={`field-${label}`}>{children}</div>,
}))

describe('SimpleForm — Router', () => {
  const baseData: RouterNodeData = {
    type: 'router',
    name: 'R',
    routes: [
      { name: 'billing', keywords: [], isDefault: false },
      { name: 'technical', keywords: [], isDefault: false },
      { name: 'general', keywords: [], isDefault: true },
    ],
  }

  it('parses and renders route list', () => {
    const onUpdate = vi.fn()
    render(<SimpleForm data={baseData} onUpdate={onUpdate} />)

    expect(screen.getByText('billing')).toBeInTheDocument()
    expect(screen.getByText('technical')).toBeInTheDocument()
    expect(screen.getByText('general')).toBeInTheDocument()
  })

  it('marks last route as default', () => {
    const onUpdate = vi.fn()
    render(<SimpleForm data={baseData} onUpdate={onUpdate} />)

    expect(screen.getByText('(default)')).toBeInTheDocument()
  })

  it('adds a new route via button click', () => {
    const onUpdate = vi.fn()
    render(<SimpleForm data={baseData} onUpdate={onUpdate} />)

    const input = screen.getByPlaceholderText('New route name')
    fireEvent.change(input, { target: { value: 'vip' } })
    const addButton = input.closest('.flex')!.querySelector('button')!
    fireEvent.click(addButton)

    expect(onUpdate).toHaveBeenCalled()
    const call = onUpdate.mock.calls[0][0] as { routes: RouterNodeData['routes'] }
    expect(call.routes.map((r) => r.name)).toEqual(['billing', 'technical', 'general', 'vip'])
  })

  it('adds a new route via Enter key', () => {
    const onUpdate = vi.fn()
    render(<SimpleForm data={baseData} onUpdate={onUpdate} />)

    const input = screen.getByPlaceholderText('New route name')
    fireEvent.change(input, { target: { value: 'vip' } })
    fireEvent.keyDown(input, { key: 'Enter' })

    expect(onUpdate).toHaveBeenCalled()
    const call = onUpdate.mock.calls[0][0] as { routes: RouterNodeData['routes'] }
    expect(call.routes.some((r) => r.name === 'vip')).toBe(true)
  })

  it('does not add empty route', () => {
    const onUpdate = vi.fn()
    render(<SimpleForm data={baseData} onUpdate={onUpdate} />)

    const input = screen.getByPlaceholderText('New route name')
    fireEvent.change(input, { target: { value: '   ' } })
    fireEvent.keyDown(input, { key: 'Enter' })

    expect(onUpdate).not.toHaveBeenCalled()
  })

  it('removes a route by index', () => {
    const onUpdate = vi.fn()
    render(<SimpleForm data={baseData} onUpdate={onUpdate} />)

    const removeButtons = screen.getAllByRole('button').filter((b) => b.querySelector('svg'))
    fireEvent.click(removeButtons[0])

    expect(onUpdate).toHaveBeenCalled()
    const call = onUpdate.mock.calls[0][0] as { routes: RouterNodeData['routes'] }
    expect(call.routes.map((r) => r.name)).toEqual(['technical', 'general'])
  })

  it('handles empty routes array', () => {
    const onUpdate = vi.fn()
    const data: RouterNodeData = { ...baseData, routes: [] }
    render(<SimpleForm data={data} onUpdate={onUpdate} />)

    expect(screen.queryByText('(default)')).not.toBeInTheDocument()
  })
})

describe('SimpleForm — Parallel', () => {
  const baseData: ParallelNodeData = {
    type: 'parallel',
    name: 'P',
    branches: [
      { name: 'Legal', goal: '' },
      { name: 'Technical', goal: '' },
      { name: 'Financial', goal: '' },
    ],
    merge: 'labeled',
  }

  it('parses and renders branch list', () => {
    const onUpdate = vi.fn()
    render(<SimpleForm data={baseData} onUpdate={onUpdate} />)

    expect(screen.getByText('Legal')).toBeInTheDocument()
    expect(screen.getByText('Technical')).toBeInTheDocument()
    expect(screen.getByText('Financial')).toBeInTheDocument()
  })

  it('adds a new branch', () => {
    const onUpdate = vi.fn()
    render(<SimpleForm data={baseData} onUpdate={onUpdate} />)

    const input = screen.getByPlaceholderText('New branch name')
    fireEvent.change(input, { target: { value: 'Marketing' } })
    fireEvent.keyDown(input, { key: 'Enter' })

    expect(onUpdate).toHaveBeenCalled()
    const call = onUpdate.mock.calls[0][0] as { branches: ParallelNodeData['branches'] }
    expect(call.branches.map((b) => b.name)).toEqual(['Legal', 'Technical', 'Financial', 'Marketing'])
  })

  it('removes a branch', () => {
    const onUpdate = vi.fn()
    render(<SimpleForm data={baseData} onUpdate={onUpdate} />)

    const removeButtons = screen.getAllByRole('button').filter((b) => b.querySelector('svg'))
    fireEvent.click(removeButtons[1])

    expect(onUpdate).toHaveBeenCalled()
    const call = onUpdate.mock.calls[0][0] as { branches: ParallelNodeData['branches'] }
    expect(call.branches.map((b) => b.name)).toEqual(['Legal', 'Financial'])
  })
})

describe('SimpleForm — Iteration', () => {
  const baseData: IterationNodeData = {
    type: 'iteration',
    name: 'I',
    split: 'jsonArray',
    delimiter: '\n',
    maxItems: 50,
    maxConcurrency: 1,
  }

  it('hides delimiter field for jsonArray mode', () => {
    const onUpdate = vi.fn()
    render(<SimpleForm data={baseData} onUpdate={onUpdate} />)

    expect(screen.queryByPlaceholderText('\\n')).not.toBeInTheDocument()
  })

  it('shows delimiter field for delimiter mode', () => {
    const onUpdate = vi.fn()
    const data: IterationNodeData = { ...baseData, split: 'delimiter' }
    render(<SimpleForm data={data} onUpdate={onUpdate} />)

    expect(screen.getByPlaceholderText('\\n')).toBeInTheDocument()
  })
})

describe('SimpleForm — type routing', () => {
  it('returns null for unknown type', () => {
    const onUpdate = vi.fn()
    const data = { type: 'unknown' } as unknown as NodeData
    const { container } = render(<SimpleForm data={data as any} onUpdate={onUpdate} />)
    expect(container.innerHTML).toBe('')
  })
})
