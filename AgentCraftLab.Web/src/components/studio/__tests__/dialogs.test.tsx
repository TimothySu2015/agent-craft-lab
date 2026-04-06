/**
 * Studio Dialog 元件測試 — SaveDialog / LoadDialog / ExportDialog / CodeDialog / TemplatesDialog
 */
import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'

// ── Mocks ──

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string) => key }),
}))

vi.mock('@/lib/api', () => ({
  api: {
    workflows: {
      create: vi.fn().mockResolvedValue({ id: '1', name: 'Test' }),
      update: vi.fn().mockResolvedValue(undefined),
      list: vi.fn().mockResolvedValue([]),
      delete: vi.fn().mockResolvedValue(undefined),
    },
  },
}))

vi.mock('@/stores/workflow-store', () => {
  const store = {
    nodes: [],
    edges: [],
  }
  return {
    useWorkflowStore: Object.assign(
      (selector: any) => selector(store),
      { getState: () => store },
    ),
  }
})

vi.mock('@/stores/custom-templates-store', () => ({
  useCustomTemplatesStore: (selector: any) => selector({
    templates: [],
    removeTemplate: vi.fn(),
  }),
}))

vi.mock('@/lib/codegen', () => ({
  generateCSharpCode: () => '// generated code',
}))

vi.mock('@/lib/workflow-io', () => ({
  exportWorkflow: vi.fn(),
}))

vi.mock('@/lib/export-package', () => ({
  exportDeployPackage: vi.fn().mockResolvedValue(undefined),
}))

vi.mock('@/lib/templates', () => ({
  BUILTIN_TEMPLATES: [
    { id: 'tpl1', name: 'ChatBot', shortDescription: 'A chatbot', category: 'Basic', tags: ['chat'], icon: 'Sparkles' },
  ],
  TEMPLATE_CATEGORIES: ['Basic', 'Advanced'],
}))

vi.mock('prism-react-renderer', () => ({
  Highlight: ({ children }: any) => children({ style: {}, tokens: [], getLineProps: () => ({}), getTokenProps: () => ({}) }),
  themes: { oneDark: {} },
  Prism: { languages: {} },
}))

vi.mock('prismjs', () => ({ default: { languages: {} } }))
vi.mock('prismjs/components/prism-csharp', () => ({}))
vi.mock('prismjs/components/prism-json', () => ({}))

vi.mock('@/lib/utils', () => ({
  cn: (...args: any[]) => args.filter(Boolean).join(' '),
}))

// ── Imports ──

import { SaveDialog } from '../SaveDialog'
import { LoadDialog } from '../LoadDialog'
import { ExportDialog } from '../ExportDialog'
import { CodeDialog } from '../CodeDialog'
import { TemplatesDialog } from '../TemplatesDialog'

// ── SaveDialog ──

describe('SaveDialog', () => {
  const defaultProps = {
    open: false,
    onClose: vi.fn(),
    currentId: null,
    currentName: '',
    onSaved: vi.fn(),
  }

  it('returns null when not open', () => {
    const { container } = render(<SaveDialog {...defaultProps} open={false} />)
    expect(container.innerHTML).toBe('')
  })

  it('renders save form when open', () => {
    render(<SaveDialog {...defaultProps} open={true} />)
    expect(screen.getByDisplayValue('My Workflow')).toBeInTheDocument()
  })

  it('shows update title when currentId provided', () => {
    render(<SaveDialog {...defaultProps} open={true} currentId="abc" />)
    expect(screen.getByText('studio:dialog.updateWorkflow')).toBeInTheDocument()
  })

  it('shows save title when no currentId', () => {
    render(<SaveDialog {...defaultProps} open={true} currentId={null} />)
    expect(screen.getByText('studio:dialog.saveWorkflow')).toBeInTheDocument()
  })
})

// ── LoadDialog ──

describe('LoadDialog', () => {
  const defaultProps = {
    open: false,
    onClose: vi.fn(),
    onLoaded: vi.fn(),
  }

  it('returns null when not open', () => {
    const { container } = render(<LoadDialog {...defaultProps} open={false} />)
    expect(container.innerHTML).toBe('')
  })

  it('renders dialog when open', () => {
    render(<LoadDialog {...defaultProps} open={true} />)
    expect(screen.getByText('studio:dialog.loadWorkflow')).toBeInTheDocument()
  })
})

// ── ExportDialog ──

describe('ExportDialog', () => {
  const defaultProps = {
    open: false,
    onClose: vi.fn(),
    workflowName: 'TestWorkflow',
  }

  it('returns null when not open', () => {
    const { container } = render(<ExportDialog {...defaultProps} open={false} />)
    expect(container.innerHTML).toBe('')
  })

  it('renders 4 export mode options', () => {
    render(<ExportDialog {...defaultProps} open={true} />)
    expect(screen.getByText('export.jsonTitle')).toBeInTheDocument()
    expect(screen.getByText('export.projectTitle')).toBeInTheDocument()
    expect(screen.getByText('export.teamsTitle')).toBeInTheDocument()
    expect(screen.getByText('export.consoleTitle')).toBeInTheDocument()
  })

  it('selects project mode by default', () => {
    render(<ExportDialog {...defaultProps} open={true} />)
    const radios = screen.getAllByRole('radio') as HTMLInputElement[]
    // project is index 1 (json=0, project=1, teams=2, console=3)
    expect(radios[1].checked).toBe(true)
  })
})

// ── CodeDialog ──

describe('CodeDialog', () => {
  const defaultProps = {
    open: false,
    onClose: vi.fn(),
  }

  it('returns null when not open', () => {
    const { container } = render(<CodeDialog {...defaultProps} open={false} />)
    expect(container.innerHTML).toBe('')
  })

  it('renders code dialog with title', () => {
    render(<CodeDialog {...defaultProps} open={true} />)
    expect(screen.getByText('code.title')).toBeInTheDocument()
  })
})

// ── TemplatesDialog ──

describe('TemplatesDialog', () => {
  const defaultProps = {
    open: false,
    onClose: vi.fn(),
    onSelect: vi.fn(),
    onSelectCustom: vi.fn(),
  }

  it('returns null when not open', () => {
    const { container } = render(<TemplatesDialog {...defaultProps} open={false} />)
    expect(container.innerHTML).toBe('')
  })

  it('renders template list when open', () => {
    render(<TemplatesDialog {...defaultProps} open={true} />)
    expect(screen.getByText('ChatBot')).toBeInTheDocument()
  })

  it('filters templates by search', () => {
    render(<TemplatesDialog {...defaultProps} open={true} />)
    const searchInput = screen.getByPlaceholderText('Search templates...')
    fireEvent.change(searchInput, { target: { value: 'nonexistent' } })
    expect(screen.queryByText('ChatBot')).not.toBeInTheDocument()
    expect(screen.getByText('No templates match your search.')).toBeInTheDocument()
  })
})
