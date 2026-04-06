/**
 * Shared Components 測試 — ErrorBoundary / ExpandableTextarea / ProviderRow
 */
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'

vi.mock('lucide-react', async () => {
  const actual = await vi.importActual('lucide-react')
  return actual
})

vi.mock('@/lib/utils', () => ({
  cn: (...args: any[]) => args.filter(Boolean).join(' '),
}))

// ── ErrorBoundary ──

import { ErrorBoundary } from '../ErrorBoundary'

function ThrowingChild({ shouldThrow }: { shouldThrow: boolean }) {
  if (shouldThrow) throw new Error('Test error')
  return <div>Child content</div>
}

describe('ErrorBoundary', () => {
  it('renders children when no error', () => {
    render(
      <ErrorBoundary>
        <ThrowingChild shouldThrow={false} />
      </ErrorBoundary>,
    )
    expect(screen.getByText('Child content')).toBeDefined()
  })

  it('shows error UI when child throws', () => {
    const spy = vi.spyOn(console, 'error').mockImplementation(() => {})
    render(
      <ErrorBoundary>
        <ThrowingChild shouldThrow={true} />
      </ErrorBoundary>,
    )
    expect(screen.getByText('Something went wrong')).toBeDefined()
    spy.mockRestore()
  })

  it('displays error message', () => {
    const spy = vi.spyOn(console, 'error').mockImplementation(() => {})
    render(
      <ErrorBoundary>
        <ThrowingChild shouldThrow={true} />
      </ErrorBoundary>,
    )
    expect(screen.getByText('Test error')).toBeDefined()
    spy.mockRestore()
  })

  it('resets state when Try Again is clicked', () => {
    const spy = vi.spyOn(console, 'error').mockImplementation(() => {})

    // 用可控元件模擬：先 throw 再不 throw
    let shouldThrow = true
    function Controllable() {
      if (shouldThrow) throw new Error('Test error')
      return <div>Recovered</div>
    }

    const { rerender } = render(
      <ErrorBoundary>
        <Controllable />
      </ErrorBoundary>,
    )
    expect(screen.getByText('Something went wrong')).toBeDefined()

    // 切換為不丟錯，然後按 Try Again
    shouldThrow = false
    fireEvent.click(screen.getByText('Try Again'))

    // 重新 render 後應顯示正常內容
    rerender(
      <ErrorBoundary>
        <Controllable />
      </ErrorBoundary>,
    )
    expect(screen.getByText('Recovered')).toBeDefined()
    spy.mockRestore()
  })
})

// ── ExpandableTextarea ──

import { ExpandableTextarea } from '../ExpandableTextarea'

describe('ExpandableTextarea', () => {
  it('renders textarea with value', () => {
    render(<ExpandableTextarea value="hello" onChange={() => {}} />)
    const textarea = screen.getByDisplayValue('hello')
    expect(textarea).toBeDefined()
    expect(textarea.tagName).toBe('TEXTAREA')
  })

  it('calls onChange when typing', () => {
    const onChange = vi.fn()
    render(<ExpandableTextarea value="" onChange={onChange} />)
    const textarea = screen.getByRole('textbox')
    fireEvent.change(textarea, { target: { value: 'new text' } })
    expect(onChange).toHaveBeenCalledWith('new text')
  })

  it('shows expand button', () => {
    render(<ExpandableTextarea value="" onChange={() => {}} />)
    const btn = screen.getByTitle('Expand editor')
    expect(btn).toBeDefined()
  })
})

// ── ProviderRow ──

import { ProviderRow, type CredentialFieldState } from '../ProviderRow'

describe('ProviderRow', () => {
  const provider = { id: 'openai', name: 'OpenAI', models: ['gpt-4o'], needsEndpoint: false }
  const cred: CredentialFieldState = { apiKey: 'sk-test', endpoint: '', model: 'gpt-4o', showKey: false, saved: true }
  const defaultProps = {
    provider,
    cred,
    isExpanded: false,
    onToggle: vi.fn(),
    onUpdate: vi.fn(),
    hasBorder: false,
    t: (key: string) => key,
  }

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders provider name', () => {
    render(<ProviderRow {...defaultProps} />)
    expect(screen.getByText('OpenAI')).toBeDefined()
  })

  it('shows configured badge when has key and saved', () => {
    render(<ProviderRow {...defaultProps} />)
    expect(screen.getByText('studio:credentials.configured')).toBeDefined()
  })

  it('shows not set badge when no key', () => {
    render(
      <ProviderRow
        {...defaultProps}
        cred={{ ...cred, apiKey: '', saved: false }}
      />,
    )
    expect(screen.getByText('studio:credentials.notSet')).toBeDefined()
  })

  it('toggles expanded state on click', () => {
    const onToggle = vi.fn()
    render(<ProviderRow {...defaultProps} onToggle={onToggle} />)
    fireEvent.click(screen.getByText('OpenAI'))
    expect(onToggle).toHaveBeenCalledTimes(1)
  })

  it('shows API key input when expanded', () => {
    render(<ProviderRow {...defaultProps} isExpanded={true} />)
    const input = screen.getByPlaceholderText('sk-...')
    expect(input).toBeDefined()
    expect(input.getAttribute('type')).toBe('password')
  })

  it('toggle show/hide key', () => {
    const onUpdate = vi.fn()
    render(<ProviderRow {...defaultProps} isExpanded={true} onUpdate={onUpdate} />)
    // 找到 eye button（在 API Key input 旁）
    const input = screen.getByPlaceholderText('sk-...')
    expect(input.getAttribute('type')).toBe('password')

    // 點擊 eye 按鈕切換顯示
    const eyeButton = input.parentElement!.querySelector('button')!
    fireEvent.click(eyeButton)
    expect(onUpdate).toHaveBeenCalledWith('showKey', true)
  })
})
