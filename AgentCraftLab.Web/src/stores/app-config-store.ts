/**
 * App Config Store — 從後端 /info 取得部署設定（credentialMode 等）。
 * 啟動時呼叫一次，所有元件透過 useAppConfigStore 讀取。
 */
import { create } from 'zustand'

export type CredentialMode = 'database' | 'browser'

interface AppConfigState {
  credentialMode: CredentialMode
  loaded: boolean
  fetchConfig: () => Promise<void>
}

export const useAppConfigStore = create<AppConfigState>()((set) => ({
  credentialMode: 'database',
  loaded: false,

  fetchConfig: async () => {
    try {
      const res = await fetch('/info')
      if (!res.ok) throw new Error(`HTTP ${res.status}`)
      const data = await res.json()
      set({
        credentialMode: data.credentialMode === 'browser' ? 'browser' : 'database',
        loaded: true,
      })
    } catch {
      set({ loaded: true })
    }
  },
}))
