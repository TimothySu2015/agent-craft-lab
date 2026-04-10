/**
 * Credential Store — 雙模式儲存：
 *
 * database mode（自建平台）：
 *   - API Key 透過後端 /api/credentials 儲存（DataProtection 加密）
 *   - localStorage 僅快取 endpoint/model 等非敏感欄位
 *   - 前端不持有明文 key
 *
 * browser mode（公開 Demo）：
 *   - API Key 存 sessionStorage（關分頁即清除）
 *   - 不呼叫後端 API
 *   - 執行時透過 forwardedProps 帶入
 */
import { create } from 'zustand'
import { persist } from 'zustand/middleware'
import { api, type CredentialInfo } from '@/lib/api'
import { useAppConfigStore } from './app-config-store'

const SESSION_KEY = 'agentcraftlab-session-creds'

function loadSessionCredentials(): Record<string, { apiKey: string; endpoint: string; model: string }> {
  try {
    const raw = sessionStorage.getItem(SESSION_KEY)
    return raw ? JSON.parse(raw) : {}
  } catch { return {} }
}

function saveSessionCredentials(creds: Record<string, { apiKey: string; endpoint: string; model: string }>) {
  sessionStorage.setItem(SESSION_KEY, JSON.stringify(creds))
}

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
  /** 從後端或 sessionStorage 載入 credentials（啟動時呼叫一次） */
  loadFromBackend: () => Promise<void>;
  setCredential: (provider: string, entry: CredentialEntry) => void;
  /** 儲存到後端或 sessionStorage */
  saveToBackend: (provider: string, entry: { apiKey: string; endpoint: string; model: string }) => Promise<void>;
  /** 移除 credential（從後端或 sessionStorage 刪除） */
  removeCredential: (provider: string) => Promise<void>;
  setCopilotActionsEnabled: (enabled: boolean) => void;
  /** 轉為後端 AG-UI forwardedProps 需要的格式 */
  toProviderCredentials: () => Record<string, { apiKey: string; endpoint: string; model: string }>;
}

export const useCredentialStore = create<CredentialState>()(
  persist(
    (set, get) => ({
      credentials: {},
      copilotActionsEnabled: false,

      loadFromBackend: async () => {
        const mode = useAppConfigStore.getState().credentialMode

        if (mode === 'browser') {
          // Browser mode：從 sessionStorage 讀取
          const sessionCreds = loadSessionCredentials()
          set((s) => {
            const merged = { ...s.credentials }
            for (const [provider, cred] of Object.entries(sessionCreds)) {
              merged[provider] = {
                apiKey: cred.apiKey || '',
                endpoint: cred.endpoint || '',
                model: cred.model || '',
                saved: !!cred.apiKey,
              }
            }
            return { credentials: merged }
          })
          return
        }

        // Database mode：從後端 API 讀取
        try {
          const list = await api.credentials.list()
          set((s) => {
            const merged = { ...s.credentials }
            for (const info of list) {
              const existing = merged[info.provider]
              merged[info.provider] = {
                apiKey: existing?.apiKey ?? '',
                endpoint: info.endpoint || existing?.endpoint || '',
                model: info.model || existing?.model || '',
                backendId: info.id,
                saved: info.hasApiKey,
              }
            }
            return { credentials: merged }
          })
        } catch {
          // 後端不可用，使用 localStorage fallback
        }
      },

      setCredential: (provider, entry) => {
        set((s) => ({
          credentials: { ...s.credentials, [provider]: entry },
        }))
      },

      saveToBackend: async (provider, entry) => {
        const mode = useAppConfigStore.getState().credentialMode

        if (mode === 'browser') {
          // Browser mode：存 sessionStorage，保留 apiKey
          set((s) => ({
            credentials: {
              ...s.credentials,
              [provider]: {
                apiKey: entry.apiKey,
                endpoint: entry.endpoint,
                model: entry.model,
                saved: !!entry.apiKey,
              },
            },
          }))
          // 同步到 sessionStorage
          const allCreds = get().credentials
          const sessionData: Record<string, { apiKey: string; endpoint: string; model: string }> = {}
          for (const [key, val] of Object.entries(allCreds)) {
            if (val.apiKey || val.endpoint || val.model) {
              sessionData[key] = { apiKey: val.apiKey, endpoint: val.endpoint, model: val.model }
            }
          }
          saveSessionCredentials(sessionData)
          return
        }

        // Database mode：存到後端
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
                apiKey: '',
                endpoint: entry.endpoint,
                model: entry.model,
                backendId: info.id,
                saved: true,
              },
            },
          }))
        } catch (err) {
          console.warn('Failed to save credential to backend:', err)
          set((s) => ({
            credentials: {
              ...s.credentials,
              [provider]: { apiKey: '', endpoint: entry.endpoint, model: entry.model },
            },
          }))
        }
      },

      removeCredential: async (provider) => {
        const mode = useAppConfigStore.getState().credentialMode
        const existing = get().credentials[provider]

        if (mode === 'browser') {
          // Browser mode：從 sessionStorage 移除
          const sessionCreds = loadSessionCredentials()
          delete sessionCreds[provider]
          saveSessionCredentials(sessionCreds)
        } else if (existing?.backendId) {
          // Database mode：從後端刪除
          try {
            await api.credentials.delete(existing.backendId)
          } catch (err) {
            console.warn('Failed to delete credential from backend:', err)
          }
        }

        // 清除本地狀態
        set((s) => {
          const next = { ...s.credentials }
          delete next[provider]
          return { credentials: next }
        })
      },

      setCopilotActionsEnabled: (enabled) => set({ copilotActionsEnabled: enabled }),

      toProviderCredentials: () => {
        const creds = get().credentials
        const mode = useAppConfigStore.getState().credentialMode
        const result: Record<string, { apiKey: string; endpoint: string; model: string }> = {}

        if (mode === 'browser') {
          // Browser mode：從 sessionStorage 補回 apiKey
          const sessionCreds = loadSessionCredentials()
          for (const [provider, entry] of Object.entries(creds)) {
            const sessionEntry = sessionCreds[provider]
            const apiKey = entry.apiKey || sessionEntry?.apiKey || ''
            if (apiKey) {
              result[provider] = {
                apiKey,
                endpoint: entry.endpoint || sessionEntry?.endpoint || '',
                model: entry.model || sessionEntry?.model || '',
              }
            }
          }
          return result
        }

        // Database mode：不帶 apiKey（後端自行解密）
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
