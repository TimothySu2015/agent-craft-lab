/**
 * Pages Smoke Tests — 確認各頁面可正常 render 不會 crash。
 */
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, act } from '@testing-library/react'

// ── Mocks ──

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string) => key, i18n: { changeLanguage: vi.fn() } }),
}))

vi.mock('react-router-dom', () => ({
  Navigate: ({ to }: any) => <div data-testid="navigate" data-to={to} />,
  useNavigate: () => vi.fn(),
  useLocation: () => ({ pathname: '/' }),
  Link: ({ children, to }: any) => <a href={to}>{children}</a>,
}))

vi.mock('@/lib/api', () => ({
  api: {
    apiKeys: { list: vi.fn().mockResolvedValue([]) },
    knowledgeBases: { list: vi.fn().mockResolvedValue([]) },
    services: { list: vi.fn().mockResolvedValue([]) },
    requestLogs: { list: vi.fn().mockResolvedValue({ logs: [], total: 0 }) },
    schedules: { list: vi.fn().mockResolvedValue([]) },
    skills: { list: vi.fn().mockResolvedValue({ builtin: [], custom: [] }) },
    workflows: { list: vi.fn().mockResolvedValue([]) },
  },
  type: { ApiKeyInfo: {} },
}))

vi.mock('@/stores/credential-store', () => ({
  useCredentialStore: (selector: any) => selector({
    credentials: {},
    loadFromBackend: vi.fn(),
    saveToBackend: vi.fn(),
    setCredential: vi.fn(),
  }),
}))

const settingsState = {
  defaultProvider: '',
  defaultModel: '',
  theme: 'dark',
  language: 'en',
  locale: 'en',
  setTheme: vi.fn(),
  setLanguage: vi.fn(),
  setLocale: vi.fn(),
  setDefaultProvider: vi.fn(),
  setDefaultModel: vi.fn(),
}

vi.mock('@/stores/settings-store', () => ({
  useSettingsStore: Object.assign(
    (selector?: any) => selector ? selector(settingsState) : settingsState,
    { getState: () => settingsState },
  ),
}))

vi.mock('@/hooks/useCredentialFields', () => ({
  useCredentialFields: () => ({
    creds: {},
    expandedId: null,
    setExpandedId: vi.fn(),
    updateCred: vi.fn(),
    handleSave: vi.fn(),
    handleRemove: vi.fn(),
    savingId: null,
    configuredCount: 0,
    storedCredentials: {},
  }),
}))

vi.mock('@/components/shared/ProviderRow', () => ({
  ProviderRow: () => <div data-testid="provider-row" />,
}))

vi.mock('@/components/shared/ExpandableTextarea', () => ({
  ExpandableTextarea: () => <textarea data-testid="expandable-textarea" />,
}))

vi.mock('@/lib/providers', () => ({
  PROVIDERS: [],
  CLOUD_PROVIDERS: [],
  LOCAL_PROVIDERS: [],
  TOOL_CREDENTIAL_PROVIDERS: [],
  CREDENTIAL_PROVIDERS: [],
  getModelsForProvider: () => [],
}))

vi.mock('@/stores/app-config-store', () => ({
  useAppConfigStore: (selector: any) => selector({ credentialMode: 'database', loaded: true, fetchConfig: vi.fn() }),
}))

vi.mock('@/lib/utils', () => ({
  cn: (...args: any[]) => args.filter(Boolean).join(' '),
}))

// Mock global fetch for pages that use raw fetch (RequestLogsPage)
const mockFetch = vi.fn().mockResolvedValue({
  ok: true,
  json: () => Promise.resolve([]),
})
vi.stubGlobal('fetch', mockFetch)

// ── Imports ──

import { CredentialsPage } from '../CredentialsPage'
import { SettingsPage } from '../SettingsPage'
import { ApiKeysPage } from '../ApiKeysPage'
import { KnowledgeBasePage } from '../KnowledgeBasePage'
import { SkillsPage } from '../SkillsPage'
import { PublishedServicesPage } from '../PublishedServicesPage'
import { RequestLogsPage } from '../RequestLogsPage'
import { SchedulesPage } from '../SchedulesPage'
import { ServiceTesterPage } from '../ServiceTesterPage'

// ── Tests ──

describe('CredentialsPage', () => {
  it('renders and redirects to /settings', () => {
    render(<CredentialsPage />)
    const nav = screen.getByTestId('navigate')
    expect(nav).toBeInTheDocument()
    expect(nav.getAttribute('data-to')).toBe('/settings')
  })
})

describe('SettingsPage', () => {
  it('renders without crashing', async () => {
    await act(async () => {
      render(<SettingsPage />)
    })
    // Settings page should contain a heading with the settings title
    expect(screen.getByText('studio:personal.title')).toBeInTheDocument()
  })
})

describe('ApiKeysPage', () => {
  it('renders without crashing', async () => {
    await act(async () => {
      render(<ApiKeysPage />)
    })
    // Should contain the API Keys heading
    expect(screen.getByText('nav.apiKeys')).toBeInTheDocument()
  })
})

describe('KnowledgeBasePage', () => {
  it('renders without crashing', async () => {
    await act(async () => {
      render(<KnowledgeBasePage />)
    })
    expect(screen.getByText('kb.title')).toBeInTheDocument()
  })
})

describe('SkillsPage', () => {
  it('renders without crashing', async () => {
    await act(async () => {
      render(<SkillsPage />)
    })
    expect(screen.getByText('skills.title')).toBeInTheDocument()
  })
})

describe('PublishedServicesPage', () => {
  it('renders without crashing', async () => {
    await act(async () => {
      render(<PublishedServicesPage />)
    })
    expect(screen.getByText('common:nav.published')).toBeInTheDocument()
  })
})

describe('RequestLogsPage', () => {
  it('renders without crashing', async () => {
    await act(async () => {
      render(<RequestLogsPage />)
    })
    expect(screen.getByText('nav.logs')).toBeInTheDocument()
  })
})

describe('SchedulesPage', () => {
  it('renders without crashing', async () => {
    await act(async () => {
      render(<SchedulesPage />)
    })
    expect(screen.getByText('nav.schedules')).toBeInTheDocument()
  })
})

describe('ServiceTesterPage', () => {
  it('renders without crashing', async () => {
    await act(async () => {
      render(<ServiceTesterPage />)
    })
    expect(screen.getByText('nav.tester')).toBeInTheDocument()
  })
})
