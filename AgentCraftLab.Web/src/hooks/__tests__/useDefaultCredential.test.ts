import { describe, it, expect, beforeEach } from 'vitest'
import { renderHook } from '@testing-library/react'
import { useDefaultCredential } from '../useDefaultCredential'
import { useCredentialStore } from '@/stores/credential-store'
import { useSettingsStore } from '@/stores/settings-store'

describe('useDefaultCredential', () => {
  beforeEach(() => {
    useCredentialStore.setState({ credentials: {} })
    useSettingsStore.setState({ defaultProvider: '', defaultModel: '' })
  })

  it('returns null when no credentials', () => {
    const { result } = renderHook(() => useDefaultCredential())
    expect(result.current()).toBeNull()
  })

  it('returns credential with apiKey', () => {
    useCredentialStore.setState({
      credentials: {
        openai: { apiKey: 'sk-test', endpoint: '', model: 'gpt-4o' },
      },
    })

    const { result } = renderHook(() => useDefaultCredential())
    const cred = result.current()

    expect(cred).not.toBeNull()
    expect(cred!.provider).toBe('openai')
    expect(cred!.apiKey).toBe('sk-test')
    expect(cred!.model).toBe('gpt-4o')
  })

  it('returns credential with saved flag (no apiKey in localStorage)', () => {
    useCredentialStore.setState({
      credentials: {
        openai: { apiKey: '', endpoint: '', model: 'gpt-4o', saved: true },
      },
    })

    const { result } = renderHook(() => useDefaultCredential())
    const cred = result.current()

    expect(cred).not.toBeNull()
    expect(cred!.provider).toBe('openai')
    expect(cred!.apiKey).toBe('')
    expect(cred!.model).toBe('gpt-4o')
  })

  it('prefers defaultProvider from settings', () => {
    useCredentialStore.setState({
      credentials: {
        openai: { apiKey: 'sk-openai', endpoint: '', model: 'gpt-4o' },
        'azure-openai': { apiKey: 'sk-azure', endpoint: 'https://x', model: 'gpt-4' },
      },
    })
    useSettingsStore.setState({ defaultProvider: 'azure-openai' })

    const { result } = renderHook(() => useDefaultCredential())
    const cred = result.current()

    expect(cred!.provider).toBe('azure-openai')
    expect(cred!.apiKey).toBe('sk-azure')
  })

  it('uses defaultModel from settings over entry model', () => {
    useCredentialStore.setState({
      credentials: {
        openai: { apiKey: 'sk-test', endpoint: '', model: 'gpt-4o' },
      },
    })
    useSettingsStore.setState({ defaultProvider: 'openai', defaultModel: 'gpt-4o-mini' })

    const { result } = renderHook(() => useDefaultCredential())
    const cred = result.current()

    expect(cred!.model).toBe('gpt-4o-mini')
  })

  it('falls back to first available if defaultProvider has no key', () => {
    useCredentialStore.setState({
      credentials: {
        openai: { apiKey: '', endpoint: '', model: '' },
        claude: { apiKey: 'sk-claude', endpoint: '', model: 'claude-3' },
      },
    })
    useSettingsStore.setState({ defaultProvider: 'openai' })

    const { result } = renderHook(() => useDefaultCredential())
    const cred = result.current()

    expect(cred!.provider).toBe('claude')
  })

  it('skips entries without apiKey or saved flag', () => {
    useCredentialStore.setState({
      credentials: {
        openai: { apiKey: '', endpoint: '', model: '' },
        claude: { apiKey: '', endpoint: '', model: '', saved: true },
      },
    })

    const { result } = renderHook(() => useDefaultCredential())
    const cred = result.current()

    expect(cred!.provider).toBe('claude')
  })
})
