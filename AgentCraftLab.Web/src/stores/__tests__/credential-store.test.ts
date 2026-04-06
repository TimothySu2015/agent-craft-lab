import { describe, it, expect, beforeEach } from 'vitest'
import { useCredentialStore } from '../credential-store'

describe('useCredentialStore', () => {
  beforeEach(() => {
    // Reset store state
    useCredentialStore.setState({ credentials: {}, copilotActionsEnabled: false })
  })

  describe('setCredential', () => {
    it('sets a credential entry', () => {
      useCredentialStore.getState().setCredential('openai', {
        apiKey: 'sk-test', endpoint: '', model: 'gpt-4o',
      })

      const { credentials } = useCredentialStore.getState()
      expect(credentials.openai).toEqual({ apiKey: 'sk-test', endpoint: '', model: 'gpt-4o' })
    })

    it('overwrites existing credential', () => {
      const store = useCredentialStore.getState()
      store.setCredential('openai', { apiKey: 'old', endpoint: '', model: '' })
      store.setCredential('openai', { apiKey: 'new', endpoint: 'http://x', model: 'gpt-4o' })

      const { credentials } = useCredentialStore.getState()
      expect(credentials.openai.apiKey).toBe('new')
      expect(credentials.openai.endpoint).toBe('http://x')
    })

    it('preserves other providers', () => {
      const store = useCredentialStore.getState()
      store.setCredential('openai', { apiKey: 'sk-1', endpoint: '', model: '' })
      store.setCredential('anthropic', { apiKey: 'sk-2', endpoint: '', model: '' })

      const { credentials } = useCredentialStore.getState()
      expect(credentials.openai.apiKey).toBe('sk-1')
      expect(credentials.anthropic.apiKey).toBe('sk-2')
    })
  })

  describe('setCopilotActionsEnabled', () => {
    it('toggles the flag', () => {
      useCredentialStore.getState().setCopilotActionsEnabled(true)
      expect(useCredentialStore.getState().copilotActionsEnabled).toBe(true)

      useCredentialStore.getState().setCopilotActionsEnabled(false)
      expect(useCredentialStore.getState().copilotActionsEnabled).toBe(false)
    })
  })

  describe('toProviderCredentials', () => {
    it('returns empty object when no credentials', () => {
      const result = useCredentialStore.getState().toProviderCredentials()
      expect(result).toEqual({})
    })

    it('only includes providers with apiKey', () => {
      const store = useCredentialStore.getState()
      store.setCredential('openai', { apiKey: 'sk-test', endpoint: '', model: 'gpt-4o' })
      store.setCredential('empty', { apiKey: '', endpoint: 'http://x', model: 'gpt-4o' })

      const result = useCredentialStore.getState().toProviderCredentials()
      expect(Object.keys(result)).toEqual(['openai'])
      expect(result.openai).toEqual({ apiKey: 'sk-test', endpoint: '', model: 'gpt-4o' })
    })

    it('defaults endpoint and model to empty string', () => {
      useCredentialStore.getState().setCredential('test', {
        apiKey: 'key', endpoint: '', model: '',
      })

      const result = useCredentialStore.getState().toProviderCredentials()
      expect(result.test.endpoint).toBe('')
      expect(result.test.model).toBe('')
    })
  })
})
