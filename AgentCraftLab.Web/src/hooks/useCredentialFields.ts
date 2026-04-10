import { useState, useEffect, useMemo } from 'react'
import { PROVIDERS, TOOL_CREDENTIAL_PROVIDERS, type ProviderConfig } from '@/lib/providers'
import { useCredentialStore } from '@/stores/credential-store'
import type { CredentialFieldState } from '@/components/shared/ProviderRow'

const allProviders = [...PROVIDERS, ...TOOL_CREDENTIAL_PROVIDERS]

/** Credential 表單狀態管理 — 供 SettingsPage 共用。啟動時從後端同步。 */
export function useCredentialFields() {
  const storedCredentials = useCredentialStore((s) => s.credentials)
  const loadFromBackend = useCredentialStore((s) => s.loadFromBackend)
  const saveToBackend = useCredentialStore((s) => s.saveToBackend)
  const removeCredential = useCredentialStore((s) => s.removeCredential)
  const setCredential = useCredentialStore((s) => s.setCredential)
  const [expandedId, setExpandedId] = useState<string | null>(null)
  const [savingId, setSavingId] = useState<string | null>(null)

  const providerMap = useMemo(
    () => new Map<string, ProviderConfig>(allProviders.map((p) => [p.id, p])),
    []
  )

  // 啟動時從後端同步 credentials
  useEffect(() => { loadFromBackend() }, [loadFromBackend])

  const [creds, setCreds] = useState<Record<string, CredentialFieldState>>(() => {
    const init: Record<string, CredentialFieldState> = {}
    for (const p of allProviders) {
      const stored = storedCredentials[p.id]
      init[p.id] = {
        apiKey: stored?.apiKey ?? '',
        endpoint: stored?.endpoint ?? (p.needsEndpoint ? (p.defaultEndpoint ?? '') : ''),
        model: stored?.model ?? (p.models[0] ?? ''),
        showKey: false,
        saved: !!stored?.saved,
      }
    }
    return init
  })

  // 後端載入後更新表單狀態
  useEffect(() => {
    setCreds((prev) => {
      const next = { ...prev }
      for (const p of allProviders) {
        const stored = storedCredentials[p.id]
        if (stored) {
          next[p.id] = {
            ...next[p.id],
            endpoint: stored.endpoint || next[p.id].endpoint,
            model: stored.model || next[p.id].model,
            saved: !!stored.saved,
          }
        } else {
          // credential 被移除後重置 saved 狀態
          next[p.id] = { ...next[p.id], saved: false }
        }
      }
      return next
    })
  }, [storedCredentials])

  const updateCred = (id: string, field: keyof CredentialFieldState, value: string | boolean) => {
    setCreds((prev) => ({ ...prev, [id]: { ...prev[id], [field]: value } }))
  }

  const handleSave = async (providerId: string) => {
    setSavingId(providerId)
    try {
      const entry = creds[providerId]
      const def = providerMap.get(providerId)
      const apiKey = entry.apiKey || def?.defaultApiKey || ''
      const shouldSave = apiKey || (def?.keyOptional && entry.endpoint)
      if (shouldSave) {
        await saveToBackend(providerId, { apiKey, endpoint: entry.endpoint, model: entry.model })
        setCreds((prev) => ({ ...prev, [providerId]: { ...prev[providerId], apiKey: '', saved: true } }))
      } else {
        setCredential(providerId, { apiKey: '', endpoint: entry.endpoint, model: entry.model })
      }
    } finally {
      setSavingId(null)
    }
  }

  const handleRemove = async (providerId: string) => {
    setSavingId(providerId)
    try {
      await removeCredential(providerId)
      const def = providerMap.get(providerId)
      setCreds((prev) => ({
        ...prev,
        [providerId]: {
          apiKey: '',
          endpoint: def?.needsEndpoint ? (def.defaultEndpoint ?? '') : '',
          model: def?.models[0] ?? '',
          showKey: false,
          saved: false,
        },
      }))
    } finally {
      setSavingId(null)
    }
  }

  const configuredCount = Object.entries(creds).filter(
    ([, c]) => c.saved
  ).length

  return { creds, expandedId, setExpandedId, updateCred, handleSave, handleRemove, savingId, configuredCount, storedCredentials }
}
