/**
 * Credential Store — 雙層儲存：
 * 1. 後端 /api/credentials（DPAPI 加密，主要來源）
 * 2. localStorage 作為 UI 快取（endpoint/model 等非敏感欄位）
 *
 * API Key 只透過後端 API 儲存/讀取，前端不再持有明文 key。
 * 後端執行時直接從 ICredentialStore 讀取，不再依賴 forwardedProps。
 */
import { create } from 'zustand'
import { persist } from 'zustand/middleware'
import { api, type CredentialInfo } from '@/lib/api'

interface CredentialEntry {
  apiKey: string;
  endpoint: string;
  model: string;
  /** 後端 credential document ID（用於 PUT 更新） */
  backendId?: string;
  /** 後端已儲存（hasApiKey=true） */
  saved?: boolean;
}

interface CredentialState {
  credentials: Record<string, CredentialEntry>;
  /** Generative UI 開關 — 啟用後 CopilotKit Actions 會在 Chat 中渲染互動式 UI */
  copilotActionsEnabled: boolean;
  /** 從後端載入 credentials（啟動時呼叫一次） */
  loadFromBackend: () => Promise<void>;
  setCredential: (provider: string, entry: CredentialEntry) => void;
  /** 儲存到後端（Settings 頁 Save All 時呼叫） */
  saveToBackend: (provider: string, entry: { apiKey: string; endpoint: string; model: string }) => Promise<void>;
  setCopilotActionsEnabled: (enabled: boolean) => void;
  /** 轉為後端 AG-UI forwardedProps 需要的格式（向後相容 fallback） */
  toProviderCredentials: () => Record<string, { apiKey: string; endpoint: string; model: string }>;
}

export const useCredentialStore = create<CredentialState>()(
  persist(
    (set, get) => ({
      credentials: {},
      copilotActionsEnabled: false,

      loadFromBackend: async () => {
        try {
          const list = await api.credentials.list()
          set((s) => {
            const merged = { ...s.credentials }
            for (const info of list) {
              const existing = merged[info.provider]
              merged[info.provider] = {
                apiKey: existing?.apiKey ?? '', // 前端不拿明文 key，保留本地快取
                endpoint: info.endpoint || existing?.endpoint || '',
                model: info.model || existing?.model || '',
                backendId: info.id,
                saved: info.hasApiKey,
              }
            }
            return { credentials: merged }
          })
        } catch {
          // 後端不可用（開發模式等），使用 localStorage fallback
        }
      },

      setCredential: (provider, entry) => {
        set((s) => ({
          credentials: { ...s.credentials, [provider]: entry },
        }))
      },

      saveToBackend: async (provider, entry) => {
        try {
          const existing = get().credentials[provider]
          let info: CredentialInfo
          if (existing?.backendId) {
            info = await api.credentials.update(existing.backendId, {
              provider,
              name: provider,
              apiKey: entry.apiKey,
              endpoint: entry.endpoint,
              model: entry.model,
            })
          } else {
            info = await api.credentials.save({
              provider,
              name: provider,
              apiKey: entry.apiKey,
              endpoint: entry.endpoint,
              model: entry.model,
            })
          }
          set((s) => ({
            credentials: {
              ...s.credentials,
              [provider]: {
                apiKey: '', // 已存後端（DPAPI 加密），本地不保留明文
                endpoint: entry.endpoint,
                model: entry.model,
                backendId: info.id,
                saved: true,
              },
            },
          }))
        } catch (err) {
          // 後端不可用 — 只存非敏感欄位（endpoint/model），不存明文 apiKey 到 localStorage
          console.warn('Failed to save credential to backend:', err)
          set((s) => ({
            credentials: {
              ...s.credentials,
              [provider]: { apiKey: '', endpoint: entry.endpoint, model: entry.model },
            },
          }))
        }
      },

      setCopilotActionsEnabled: (enabled) => set({ copilotActionsEnabled: enabled }),

      toProviderCredentials: () => {
        const creds = get().credentials
        const result: Record<string, { apiKey: string; endpoint: string; model: string }> = {}
        for (const [provider, entry] of Object.entries(creds)) {
          if (entry.apiKey || entry.saved) {
            result[provider] = {
              apiKey: entry.apiKey || '',
              endpoint: entry.endpoint || '',
              model: entry.model || '',
            }
          }
        }
        return result
      },
    }),
    { name: 'agentcraftlab-credentials' },
  ),
)
