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
  const setCredential = useCredentialStore((s) => s.setCredential)
  const [expandedId, setExpandedId] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

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
        saved: !!stored?.saved || !!stored?.apiKey,
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
            saved: !!stored.saved || !!stored.apiKey,
          }
        }
      }
      return next
    })
  }, [storedCredentials])

  const updateCred = (id: string, field: keyof CredentialFieldState, value: string | boolean) => {
    setCreds((prev) => ({ ...prev, [id]: { ...prev[id], [field]: value, saved: false } }))
  }

  const handleSaveAll = async () => {
    setSaving(true)
    try {
      for (const [key, entry] of Object.entries(creds)) {
        const def = providerMap.get(key)
        const shouldSaveToBackend = entry.apiKey || (def?.keyOptional && entry.endpoint)
        if (shouldSaveToBackend) {
          const apiKey = entry.apiKey || def?.defaultApiKey || ''
          await saveToBackend(key, { apiKey, endpoint: entry.endpoint, model: entry.model })
        } else {
          setCredential(key, { apiKey: '', endpoint: entry.endpoint, model: entry.model })
        }
      }
      setCreds((prev) => {
        const next = { ...prev }
        for (const key of Object.keys(next)) {
          const def = providerMap.get(key)
          const isSaved = next[key].apiKey || (def?.keyOptional && next[key].endpoint)
          if (isSaved) next[key] = { ...next[key], saved: true }
        }
        return next
      })
    } finally {
      setSaving(false)
    }
  }

  const configuredCount = Object.entries(creds).filter(
    ([key, c]) => c.apiKey || storedCredentials[key]?.saved || (providerMap.get(key)?.keyOptional && c.endpoint)
  ).length

  return { creds, expandedId, setExpandedId, updateCred, handleSaveAll, configuredCount, storedCredentials, saving }
}
