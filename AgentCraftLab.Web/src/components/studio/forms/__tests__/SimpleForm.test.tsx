/**
 * SimpleForm 測試 — 驗證 Router/Parallel 的列表解析與增刪、
 * Iteration 的條件渲染、Tool 的 MCP/approval 切換。
 */
import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { SimpleForm } from '../SimpleForm'
import type { NodeData, RouterNodeData, ParallelNodeData, IterationNodeData, ToolNodeData } from '@/types/workflow'

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
    type: 'router', name: 'R', conditionExpression: '', routes: 'billing,technical,general',
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
    // The Plus button is the one next to the input field (in the same flex container)
    const addButton = input.closest('.flex')!.querySelector('button')!
    fireEvent.click(addButton)

    expect(onUpdate).toHaveBeenCalledWith({
      routes: 'billing,technical,general,vip',
    })
  })

  it('adds a new route via Enter key', () => {
    const onUpdate = vi.fn()
    render(<SimpleForm data={baseData} onUpdate={onUpdate} />)

    const input = screen.getByPlaceholderText('New route name')
    fireEvent.change(input, { target: { value: 'vip' } })
    fireEvent.keyDown(input, { key: 'Enter' })

    expect(onUpdate).toHaveBeenCalledWith({
      routes: 'billing,technical,general,vip',
    })
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

    // 3 routes → 3 remove buttons
    const removeButtons = screen.getAllByRole('button').filter((b) => b.querySelector('svg'))
    // Click the first remove button (after Plus button)
    fireEvent.click(removeButtons[0])

    expect(onUpdate).toHaveBeenCalledWith({
      routes: 'technical,general',
    })
  })

  it('handles empty routes string', () => {
    const onUpdate = vi.fn()
    const data = { ...baseData, routes: '' }
    render(<SimpleForm data={data} onUpdate={onUpdate} />)

    // No route items rendered
    expect(screen.queryByText('(default)')).not.toBeInTheDocument()
  })

  it('trims whitespace from routes', () => {
    const onUpdate = vi.fn()
    const data = { ...baseData, routes: ' billing , technical , general ' }
    render(<SimpleForm data={data} onUpdate={onUpdate} />)

    expect(screen.getByText('billing')).toBeInTheDocument()
    expect(screen.getByText('technical')).toBeInTheDocument()
  })
})

describe('SimpleForm — Parallel', () => {
  const baseData: ParallelNodeData = {
    type: 'parallel', name: 'P', branches: 'Legal,Technical,Financial', mergeStrategy: 'labeled',
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

    expect(onUpdate).toHaveBeenCalledWith({
      branches: 'Legal,Technical,Financial,Marketing',
    })
  })

  it('removes a branch', () => {
    const onUpdate = vi.fn()
    render(<SimpleForm data={baseData} onUpdate={onUpdate} />)

    const removeButtons = screen.getAllByRole('button').filter((b) => b.querySelector('svg'))
    fireEvent.click(removeButtons[1]) // Remove second branch

    expect(onUpdate).toHaveBeenCalledWith({
      branches: 'Legal,Financial',
    })
  })
})

describe('SimpleForm — Iteration', () => {
  const baseData: IterationNodeData = {
    type: 'iteration', name: 'I', splitMode: 'json-array', iterationDelimiter: '\\n', maxItems: 50,
  }

  it('hides delimiter field for json-array mode', () => {
    const onUpdate = vi.fn()
    render(<SimpleForm data={baseData} onUpdate={onUpdate} />)

    expect(screen.queryByPlaceholderText('\\n')).not.toBeInTheDocument()
  })

  it('shows delimiter field for delimiter mode', () => {
    const onUpdate = vi.fn()
    const data = { ...baseData, splitMode: 'delimiter' }
    render(<SimpleForm data={data} onUpdate={onUpdate} />)

    expect(screen.getByPlaceholderText('\\n')).toBeInTheDocument()
  })
})

describe('SimpleForm — Tool', () => {
  const baseData: ToolNodeData = {
    type: 'tool', name: 'T', description: 'A tool', parameters: '',
    toolSource: 'function', mcpServerUrl: '',
  }

  it('hides MCP URL for function source', () => {
    const onUpdate = vi.fn()
    render(<SimpleForm data={baseData} onUpdate={onUpdate} />)

    expect(screen.queryByPlaceholderText('http://localhost:3001/mcp')).not.toBeInTheDocument()
  })

  it('shows MCP URL for mcp source', () => {
    const onUpdate = vi.fn()
    const data = { ...baseData, toolSource: 'mcp' }
    render(<SimpleForm data={data} onUpdate={onUpdate} />)

    expect(screen.getByPlaceholderText('http://localhost:3001/mcp')).toBeInTheDocument()
  })

  it('hides approval reason when approval is off', () => {
    const onUpdate = vi.fn()
    render(<SimpleForm data={baseData} onUpdate={onUpdate} />)

    expect(screen.queryByPlaceholderText('Why does this tool need approval?')).not.toBeInTheDocument()
  })

  it('shows approval reason when approval is on', () => {
    const onUpdate = vi.fn()
    const data = { ...baseData, requireApproval: true }
    render(<SimpleForm data={data} onUpdate={onUpdate} />)

    expect(screen.getByPlaceholderText('Why does this tool need approval?')).toBeInTheDocument()
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
