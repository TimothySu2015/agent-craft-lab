import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, act } from '@testing-library/react'
import { useCredentialFields } from '../useCredentialFields'

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string) => key }),
}))

vi.mock('@/lib/providers', () => ({
  PROVIDERS: [
    { id: 'openai', name: 'OpenAI', models: ['gpt-4o', 'gpt-4o-mini'], needsEndpoint: false },
    { id: 'azure-openai', name: 'Azure', models: ['gpt-4o'], needsEndpoint: true, defaultEndpoint: 'https://azure.test' },
  ],
  TOOL_CREDENTIAL_PROVIDERS: [
    { id: 'tavily', name: 'Tavily', models: [] },
  ],
}))

const mockLoadFromBackend = vi.fn()
const mockSaveToBackend = vi.fn().mockResolvedValue(undefined)
const mockRemoveCredential = vi.fn().mockResolvedValue(undefined)
const mockSetCredential = vi.fn()
let mockCredentials: Record<string, any> = {}

vi.mock('@/stores/credential-store', () => ({
  useCredentialStore: (selector: any) => selector({
    credentials: mockCredentials,
    loadFromBackend: mockLoadFromBackend,
    saveToBackend: mockSaveToBackend,
    removeCredential: mockRemoveCredential,
    setCredential: mockSetCredential,
  }),
}))

describe('useCredentialFields', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockCredentials = {}
  })

  it('initializes creds for all providers', () => {
    const { result } = renderHook(() => useCredentialFields())

    expect(result.current.creds).toHaveProperty('openai')
    expect(result.current.creds).toHaveProperty('azure-openai')
    expect(result.current.creds).toHaveProperty('tavily')
    expect(Object.keys(result.current.creds)).toHaveLength(3)
  })

  it('azure-openai uses defaultEndpoint', () => {
    const { result } = renderHook(() => useCredentialFields())

    expect(result.current.creds['azure-openai'].endpoint).toBe('https://azure.test')
  })

  it('openai uses first model as default', () => {
    const { result } = renderHook(() => useCredentialFields())

    expect(result.current.creds['openai'].model).toBe('gpt-4o')
  })

  it('calls loadFromBackend on mount', () => {
    renderHook(() => useCredentialFields())

    expect(mockLoadFromBackend).toHaveBeenCalled()
  })

  it('updateCred sets field value', () => {
    const { result } = renderHook(() => useCredentialFields())

    act(() => {
      result.current.updateCred('openai', 'apiKey', 'sk-new-key')
    })

    expect(result.current.creds['openai'].apiKey).toBe('sk-new-key')
  })

  it('configuredCount reflects saved entries', () => {
    mockCredentials = {
      openai: { apiKey: '', endpoint: '', model: 'gpt-4o', saved: true },
    }

    const { result } = renderHook(() => useCredentialFields())

    expect(result.current.configuredCount).toBe(1)
  })

  it('handleSave calls saveToBackend for provider with apiKey', async () => {
    const { result } = renderHook(() => useCredentialFields())

    act(() => {
      result.current.updateCred('openai', 'apiKey', 'sk-save-test')
    })

    await act(async () => {
      await result.current.handleSave('openai')
    })

    expect(mockSaveToBackend).toHaveBeenCalledWith(
      'openai',
      expect.objectContaining({ apiKey: 'sk-save-test' }),
    )
  })

  it('handleSave calls setCredential for provider without apiKey', async () => {
    const { result } = renderHook(() => useCredentialFields())

    await act(async () => {
      await result.current.handleSave('openai')
    })

    expect(mockSetCredential).toHaveBeenCalledWith(
      'openai',
      expect.objectContaining({ apiKey: '' }),
    )
  })

  it('handleRemove calls removeCredential and resets state', async () => {
    mockCredentials = {
      openai: { apiKey: '', endpoint: '', model: 'gpt-4o', saved: true, backendId: 'cred-1' },
    }

    const { result } = renderHook(() => useCredentialFields())

    await act(async () => {
      await result.current.handleRemove('openai')
    })

    expect(mockRemoveCredential).toHaveBeenCalledWith('openai')
    expect(result.current.creds['openai'].saved).toBe(false)
    expect(result.current.creds['openai'].apiKey).toBe('')
  })

  it('expandedId starts as null and can be set', () => {
    const { result } = renderHook(() => useCredentialFields())

    expect(result.current.expandedId).toBeNull()

    act(() => {
      result.current.setExpandedId('openai')
    })

    expect(result.current.expandedId).toBe('openai')
  })
})
