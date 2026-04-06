import { create } from 'zustand'
import { persist } from 'zustand/middleware'

export type Locale = 'en' | 'zh-TW' | 'ja'
export type Theme = 'dark' | 'light' | 'system'

interface SettingsState {
  // Profile
  displayName: string

  // Appearance
  locale: Locale
  theme: Theme

  // Default Model
  defaultProvider: string
  defaultModel: string

  // Budget
  dailyTokenLimit: number       // 0 = unlimited
  costAlertThreshold: number    // 0 = no alert (USD)

  // Advanced
  httpProxy: string

  // Actions
  setDisplayName: (name: string) => void
  setLocale: (locale: Locale) => void
  setTheme: (theme: Theme) => void
  setDefaultProvider: (provider: string) => void
  setDefaultModel: (model: string) => void
  setDailyTokenLimit: (limit: number) => void
  setCostAlertThreshold: (threshold: number) => void
  setHttpProxy: (proxy: string) => void
}

export const useSettingsStore = create<SettingsState>()(
  persist(
    (set) => ({
      displayName: '',
      locale: 'zh-TW',
      theme: 'dark',
      defaultProvider: '',
      defaultModel: '',
      dailyTokenLimit: 0,
      costAlertThreshold: 0,
      httpProxy: '',

      setDisplayName: (name) => set({ displayName: name }),
      setLocale: (locale) => set({ locale }),
      setTheme: (theme) => set({ theme }),
      setDefaultProvider: (provider) => set({ defaultProvider: provider }),
      setDefaultModel: (model) => set({ defaultModel: model }),
      setDailyTokenLimit: (limit) => set({ dailyTokenLimit: limit }),
      setCostAlertThreshold: (threshold) => set({ costAlertThreshold: threshold }),
      setHttpProxy: (proxy) => set({ httpProxy: proxy }),
    }),
    { name: 'agentcraftlab-settings' },
  ),
)
