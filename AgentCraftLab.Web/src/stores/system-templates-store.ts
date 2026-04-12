/**
 * System templates store — 啟動時從 /templates.json 載入範本結構定義。
 * 範本的顯示字串（name / shortDescription / sampleMessages / categories）
 * 透過 i18next 的 'templates' namespace 解析，與本 store 解耦。
 *
 * 設計決定：
 * 1. 單次載入存 memory，後續 sync 取用（TemplatesDialog 首次開啟無延遲）
 * 2. 結構定義與 i18n 分離 — 結構檔不含任何 user-facing 字串
 * 3. 若 fetch 失敗會留空陣列 — UI 應當顯示 empty state 而非崩潰
 */
import { create } from 'zustand'
import type { TemplateDef } from '@/lib/template-builder'

/** 範本結構定義 — 對應 public/templates.json 的單一項目。 */
export interface SystemTemplateStructure {
  id: string
  icon: string
  category: string
  tags: string[]
  def: TemplateDef
}

interface SystemTemplatesFile {
  version: string
  templates: SystemTemplateStructure[]
}

interface SystemTemplatesState {
  templates: SystemTemplateStructure[]
  loaded: boolean
  loadFailed: boolean
  loadFromFile: () => Promise<void>
}

export const useSystemTemplatesStore = create<SystemTemplatesState>((set, get) => ({
  templates: [],
  loaded: false,
  loadFailed: false,

  loadFromFile: async () => {
    if (get().loaded) return

    try {
      const res = await fetch('/templates.json', { cache: 'no-cache' })
      if (!res.ok) throw new Error(`HTTP ${res.status}`)
      const data = (await res.json()) as SystemTemplatesFile
      if (!Array.isArray(data?.templates)) {
        throw new Error('Invalid templates.json: missing templates array')
      }
      set({ templates: data.templates, loaded: true, loadFailed: false })
    } catch (err) {
      console.error('[system-templates] Failed to load /templates.json', err)
      set({ templates: [], loaded: true, loadFailed: true })
    }
  },
}))
