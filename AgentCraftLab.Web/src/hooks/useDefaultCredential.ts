import { useCallback } from 'react'
import { useCredentialStore } from '@/stores/credential-store'
import { useSettingsStore } from '@/stores/settings-store'

/** 取得預設 credential：優先 Settings 的 defaultProvider，fallback 第一個已設定的 provider。 */
export function useDefaultCredential() {
  const credentials = useCredentialStore((s) => s.credentials)
  const defaultProvider = useSettingsStore((s) => s.defaultProvider)
  const defaultModel = useSettingsStore((s) => s.defaultModel)

  return useCallback(() => {
    const hasKey = (entry: { apiKey: string; saved?: boolean }) => !!entry.apiKey || !!entry.saved

    if (defaultProvider && credentials[defaultProvider] && hasKey(credentials[defaultProvider])) {
      const entry = credentials[defaultProvider]
      return { provider: defaultProvider, apiKey: entry.apiKey, endpoint: entry.endpoint, model: defaultModel || entry.model }
    }
    for (const [provider, entry] of Object.entries(credentials)) {
      if (hasKey(entry)) return { provider, ...entry }
    }
    return null
  }, [credentials, defaultProvider, defaultModel])
}
