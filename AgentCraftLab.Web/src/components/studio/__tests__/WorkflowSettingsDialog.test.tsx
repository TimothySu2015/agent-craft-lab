/**
 * WorkflowSettingsDialog 測試 — 驗證 hook CRUD、條件欄位顯示、tab 切換。
 */
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { WorkflowSettingsDialog } from '../WorkflowSettingsDialog'

// Mock i18n
vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string) => key }),
}))

// Mock workflow store — use controllable state
let mockSettings: Record<string, any> = { type: 'auto', maxTurns: 10, hooks: {} }
const mockUpdateSettings = vi.fn((partial: any) => {
  mockSettings = { ...mockSettings, ...partial }
})

vi.mock('@/stores/workflow-store', () => ({
  useWorkflowStore: (selector: any) => selector({
    workflowSettings: mockSettings,
    updateSettings: mockUpdateSettings,
  }),
}))

describe('WorkflowSettingsDialog', () => {
  beforeEach(() => {
    mockSettings = { type: 'auto', maxTurns: 10, hooks: {} } as Record<string, any>
    mockUpdateSettings.mockClear()
  })

  it('does not render when closed', () => {
    const { container } = render(<WorkflowSettingsDialog open={false} onClose={vi.fn()} />)
    expect(container.innerHTML).toBe('')
  })

  it('renders when open', () => {
    render(<WorkflowSettingsDialog open={true} onClose={vi.fn()} />)
    expect(screen.getByText('settings.title')).toBeInTheDocument()
  })

  describe('General tab', () => {
    it('shows workflow type options', () => {
      render(<WorkflowSettingsDialog open={true} onClose={vi.fn()} />)

      // 5 workflow types as radio buttons
      const radios = screen.getAllByRole('radio')
      expect(radios).toHaveLength(5)
    })

    it('renders context passing select with three options', () => {
      render(<WorkflowSettingsDialog open={true} onClose={vi.fn()} />)

      const select = screen.getByDisplayValue('settings.ctxPreviousOnly')
      expect(select).toBeInTheDocument()

      const options = select.querySelectorAll('option')
      expect(options).toHaveLength(3)
      expect(options[0].value).toBe('previous-only')
      expect(options[1].value).toBe('with-original')
      expect(options[2].value).toBe('accumulate')
    })
  })

  describe('Hooks tab', () => {
    it('switches to hooks tab', () => {
      render(<WorkflowSettingsDialog open={true} onClose={vi.fn()} />)

      fireEvent.click(screen.getByText(/Hooks/))
      // Should show all 6 hook points
      expect(screen.getByText('settings.hookOnInput')).toBeInTheDocument()
      expect(screen.getByText('settings.hookPreExecute')).toBeInTheDocument()
      expect(screen.getByText('settings.hookPreAgent')).toBeInTheDocument()
      expect(screen.getByText('settings.hookPostAgent')).toBeInTheDocument()
      expect(screen.getByText('settings.hookOnComplete')).toBeInTheDocument()
      expect(screen.getByText('settings.hookOnError')).toBeInTheDocument()
    })

    it('shows Add button for hook without config', () => {
      render(<WorkflowSettingsDialog open={true} onClose={vi.fn()} />)
      fireEvent.click(screen.getByText(/Hooks/))

      const addButtons = screen.getAllByText('settings.hookAdd')
      expect(addButtons.length).toBe(6) // All 6 hooks unconfigured
    })

    it('adds a hook with default code config', () => {
      render(<WorkflowSettingsDialog open={true} onClose={vi.fn()} />)
      fireEvent.click(screen.getByText(/Hooks/))

      const addButtons = screen.getAllByText('settings.hookAdd')
      fireEvent.click(addButtons[0]) // Add to first hook (onInput)

      expect(mockUpdateSettings).toHaveBeenCalledWith({
        hooks: {
          onInput: { type: 'code', kind: 'template', expression: '{{input}}' },
        },
      })
    })

    it('removes a hook', () => {
      mockSettings = {
        ...mockSettings,
        hooks: { onInput: { type: 'code', kind: 'template', expression: '{{input}}' } },
      }
      render(<WorkflowSettingsDialog open={true} onClose={vi.fn()} />)
      fireEvent.click(screen.getByText(/Hooks/))

      // Trash button should be present for configured hook
      const trashButtons = screen.getAllByRole('button').filter((b) => {
        const svg = b.querySelector('svg')
        return svg && b.closest('[class*="rounded-md border"]')
      })
      // Find and click the delete button (Trash2 icon)
      const hookSection = screen.getByText('settings.hookOnInput').closest('div[class*="rounded-md"]')!
      const deleteBtn = hookSection.querySelector('button')!
      fireEvent.click(deleteBtn)

      expect(mockUpdateSettings).toHaveBeenCalledWith({
        hooks: {},
      })
    })

    it('shows hook count badge', () => {
      mockSettings = {
        ...mockSettings,
        hooks: {
          onInput: { type: 'code' },
          preAgent: { type: 'webhook' },
        },
      }
      render(<WorkflowSettingsDialog open={true} onClose={vi.fn()} />)

      expect(screen.getByText('(2)')).toBeInTheDocument()
    })

    it('shows code fields for code hook type', () => {
      mockSettings = {
        ...mockSettings,
        hooks: { onInput: { type: 'code', kind: 'template', expression: '{{input}}' } },
      }
      render(<WorkflowSettingsDialog open={true} onClose={vi.fn()} />)
      fireEvent.click(screen.getByText(/Hooks/))

      // Should show transform type dropdown and template textarea
      expect(screen.getByDisplayValue('template')).toBeInTheDocument()
      expect(screen.getByPlaceholderText('{{input}}')).toBeInTheDocument()
    })

    it('shows webhook fields for webhook hook type', () => {
      mockSettings = {
        ...mockSettings,
        hooks: { onInput: { type: 'webhook', url: 'https://example.com', method: 'POST' } },
      }
      render(<WorkflowSettingsDialog open={true} onClose={vi.fn()} />)
      fireEvent.click(screen.getByText(/Hooks/))

      expect(screen.getByPlaceholderText('https://...')).toBeInTheDocument()
    })

    it('shows blockPattern only for onInput and preExecute', () => {
      mockSettings = {
        ...mockSettings,
        hooks: {
          onInput: { type: 'code', kind: 'template', expression: '' },
          postAgent: { type: 'code', kind: 'template', expression: '' },
        },
      }
      render(<WorkflowSettingsDialog open={true} onClose={vi.fn()} />)
      fireEvent.click(screen.getByText(/Hooks/))

      // blockPattern label should appear for onInput but not postAgent
      const blockLabels = screen.getAllByText('settings.hookBlockPattern')
      expect(blockLabels).toHaveLength(1) // Only onInput has it
    })
  })
})
