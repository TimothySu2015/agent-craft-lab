/**
 * AgentForm / CodeForm / ConditionForm / HumanForm / A2AForm 測試
 */
import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'

// ─── Mocks（必須在 import 元件之前） ───

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string) => key }),
}))

vi.mock('../../PropertiesPanel', () => ({
  Field: ({ label, children }: any) => <div data-testid={`field-${label}`}>{children}</div>,
}))

vi.mock('@/stores/credential-store', () => ({
  useCredentialStore: (selector: any) => selector({
    credentials: { openai: { apiKey: 'sk-test', saved: true } },
  }),
}))

vi.mock('@/hooks/useDefaultCredential', () => ({
  useDefaultCredential: () => () => ({ provider: 'openai', apiKey: 'sk-test', model: 'gpt-4o', endpoint: '' }),
}))

vi.mock('@/components/shared/ExpandableTextarea', () => ({
  ExpandableTextarea: ({ value, onChange, placeholder }: any) => (
    <textarea data-testid="expandable-textarea" value={value} onChange={(e: any) => onChange(e.target.value)} placeholder={placeholder} />
  ),
}))

// ToolPickerDialog / SkillPickerDialog / MiddlewareConfigDialog — 空 stub
vi.mock('../ToolPickerDialog', () => ({ ToolPickerDialog: () => null }))
vi.mock('../SkillPickerDialog', () => ({ SkillPickerDialog: () => null }))
vi.mock('../MiddlewareConfigDialog', () => ({ MiddlewareConfigDialog: () => null }))

// ─── Import 元件（在 mock 之後） ───

import { AgentForm } from '../AgentForm'
import { CodeForm } from '../CodeForm'
import { ConditionForm } from '../ConditionForm'
import { HumanForm } from '../HumanForm'
import { A2AForm } from '../A2AForm'
import type { AgentNodeData, CodeNodeData, ConditionNodeData, HumanNodeData, A2ANodeData } from '@/types/workflow'

// ─── Test Data ───

const agentData: AgentNodeData = {
  type: 'agent', name: 'TestAgent', instructions: 'Be helpful', model: 'gpt-4o',
  provider: 'openai', endpoint: '', deploymentName: '', historyProvider: 'none',
  maxMessages: 20, middleware: '', tools: ['web_search'], skills: [],
}

const codeData: CodeNodeData = {
  type: 'code', name: 'Transform', transformType: 'template', pattern: '',
  replacement: '', template: '{{input}}', maxLength: 0, delimiter: '\\n', splitIndex: 0,
}

const conditionData: ConditionNodeData = {
  type: 'condition', name: 'Check', conditionType: 'contains',
  conditionExpression: 'success', maxIterations: 5,
}

const humanData: HumanNodeData = {
  type: 'human', name: 'Review', prompt: 'Please approve',
  inputType: 'text', choices: '', timeoutSeconds: 0,
}

const a2aData: A2ANodeData = {
  type: 'a2a-agent', name: 'Remote', instructions: 'Handle task',
  a2AUrl: 'http://localhost:5001', a2AFormat: 'auto',
}

// ═══════════════════════════════════════
// AgentForm
// ═══════════════════════════════════════

describe('AgentForm', () => {
  it('renders provider select with PROVIDERS', () => {
    render(<AgentForm data={agentData} onUpdate={vi.fn()} />)
    const select = screen.getAllByRole('combobox')[0] as HTMLSelectElement
    expect(select.value).toBe('openai')
    const options = Array.from(select.options).map((o) => o.value)
    expect(options).toContain('openai')
  })

  it('renders model select', () => {
    render(<AgentForm data={agentData} onUpdate={vi.fn()} />)
    const selects = screen.getAllByRole('combobox')
    const modelSelect = selects[1] as HTMLSelectElement
    const options = Array.from(modelSelect.options).map((o) => o.value)
    expect(options).toContain('gpt-4o')
  })

  it('renders tools count badge', () => {
    render(<AgentForm data={agentData} onUpdate={vi.fn()} />)
    expect(screen.getByText('1 toolPicker.selected')).toBeInTheDocument()
  })

  it('calls onUpdate when provider changes', () => {
    const onUpdate = vi.fn()
    render(<AgentForm data={agentData} onUpdate={onUpdate} />)
    const select = screen.getAllByRole('combobox')[0]
    fireEvent.change(select, { target: { value: 'anthropic' } })
    expect(onUpdate).toHaveBeenCalledWith(
      expect.objectContaining({ provider: 'anthropic' }),
    )
  })

  it('shows advanced section on toggle', () => {
    render(<AgentForm data={agentData} onUpdate={vi.fn()} />)
    // Temperature field should not exist yet
    expect(screen.queryByTestId('field-form.temperature')).not.toBeInTheDocument()
    // Click advanced toggle
    const advBtn = screen.getByText('form.advanced')
    fireEvent.click(advBtn)
    expect(screen.getByTestId('field-form.temperature')).toBeInTheDocument()
  })
})

// ═══════════════════════════════════════
// CodeForm
// ═══════════════════════════════════════

describe('CodeForm', () => {
  it('renders transformType selector with all options', () => {
    render(<CodeForm data={codeData} onUpdate={vi.fn()} />)
    const select = screen.getByRole('combobox') as HTMLSelectElement
    const options = Array.from(select.options).map((o) => o.value)
    expect(options).toContain('template')
    expect(options).toContain('regex-extract')
    expect(options).toContain('script')
  })

  it('shows template textarea for template type', () => {
    render(<CodeForm data={codeData} onUpdate={vi.fn()} />)
    expect(screen.getByTestId('expandable-textarea')).toBeInTheDocument()
  })

  it('calls onUpdate when transformType changes', () => {
    const onUpdate = vi.fn()
    render(<CodeForm data={codeData} onUpdate={onUpdate} />)
    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'trim' } })
    expect(onUpdate).toHaveBeenCalledWith({ transformType: 'trim' })
  })
})

// ═══════════════════════════════════════
// ConditionForm
// ═══════════════════════════════════════

describe('ConditionForm', () => {
  it('renders condition type selector', () => {
    render(<ConditionForm data={conditionData} onUpdate={vi.fn()} />)
    const select = screen.getByRole('combobox') as HTMLSelectElement
    const options = Array.from(select.options).map((o) => o.value)
    expect(options).toContain('contains')
    expect(options).toContain('regex')
    expect(options).toContain('llm-judge')
  })

  it('renders expression textarea', () => {
    render(<ConditionForm data={conditionData} onUpdate={vi.fn()} />)
    const textarea = screen.getByRole('textbox') as HTMLTextAreaElement
    expect(textarea.value).toBe('success')
  })

  it('calls onUpdate on expression change', () => {
    const onUpdate = vi.fn()
    render(<ConditionForm data={conditionData} onUpdate={onUpdate} />)
    fireEvent.change(screen.getByRole('textbox'), { target: { value: 'failure' } })
    expect(onUpdate).toHaveBeenCalledWith({ conditionExpression: 'failure' })
  })

  it('renders max iterations input', () => {
    render(<ConditionForm data={conditionData} onUpdate={vi.fn()} />)
    const input = screen.getByRole('spinbutton') as HTMLInputElement
    expect(input.value).toBe('5')
  })
})

// ═══════════════════════════════════════
// HumanForm
// ═══════════════════════════════════════

describe('HumanForm', () => {
  it('renders input type select', () => {
    render(<HumanForm data={humanData} onUpdate={vi.fn()} />)
    const select = screen.getByRole('combobox') as HTMLSelectElement
    const options = Array.from(select.options).map((o) => o.value)
    expect(options).toContain('text')
    expect(options).toContain('choice')
    expect(options).toContain('approval')
  })

  it('renders prompt textarea', () => {
    render(<HumanForm data={humanData} onUpdate={vi.fn()} />)
    const textarea = screen.getByRole('textbox') as HTMLTextAreaElement
    expect(textarea.value).toBe('Please approve')
  })

  it('shows choices field when inputType is choice', () => {
    const choiceData = { ...humanData, inputType: 'choice' }
    render(<HumanForm data={choiceData} onUpdate={vi.fn()} />)
    expect(screen.getByTestId('field-Choices (comma-separated)')).toBeInTheDocument()
  })

  it('hides choices field for non-choice types', () => {
    render(<HumanForm data={humanData} onUpdate={vi.fn()} />)
    expect(screen.queryByTestId('field-Choices (comma-separated)')).not.toBeInTheDocument()
  })

  it('renders timeout input', () => {
    render(<HumanForm data={humanData} onUpdate={vi.fn()} />)
    const input = screen.getByRole('spinbutton') as HTMLInputElement
    expect(input.value).toBe('0')
  })
})

// ═══════════════════════════════════════
// A2AForm
// ═══════════════════════════════════════

describe('A2AForm', () => {
  it('renders URL input', () => {
    render(<A2AForm data={a2aData} onUpdate={vi.fn()} />)
    const input = screen.getByDisplayValue('http://localhost:5001') as HTMLInputElement
    expect(input).toBeInTheDocument()
  })

  it('renders format select', () => {
    render(<A2AForm data={a2aData} onUpdate={vi.fn()} />)
    const select = screen.getByRole('combobox') as HTMLSelectElement
    const options = Array.from(select.options).map((o) => o.value)
    expect(options).toContain('auto')
    expect(options).toContain('google')
    expect(options).toContain('microsoft')
  })

  it('renders instructions textarea', () => {
    render(<A2AForm data={a2aData} onUpdate={vi.fn()} />)
    const textarea = screen.getByDisplayValue('Handle task') as HTMLTextAreaElement
    expect(textarea.tagName).toBe('TEXTAREA')
  })

  it('calls onUpdate when URL changes', () => {
    const onUpdate = vi.fn()
    render(<A2AForm data={a2aData} onUpdate={onUpdate} />)
    const input = screen.getByDisplayValue('http://localhost:5001')
    fireEvent.change(input, { target: { value: 'http://localhost:9999' } })
    expect(onUpdate).toHaveBeenCalledWith({ a2AUrl: 'http://localhost:9999' })
  })
})
