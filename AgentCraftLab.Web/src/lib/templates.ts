/**
 * 系統 Workflow 範本的公開 API。
 *
 * Phase F 重構：範本結構定義改從 public/templates.json 載入（system-templates-store），
 * 顯示字串改從 i18next 的 'templates' namespace 解析。本檔只負責把兩邊合併成
 * <see cref="TemplateInfo" />，給 TemplatesDialog 和其他 consumer 用。
 */
import type { TFunction } from 'i18next'
import { useTranslation } from 'react-i18next'
import { useMemo } from 'react'
import type { TemplateDef, TemplateWorkflow } from './template-builder'
import { buildTemplate } from './template-builder'
import { useSystemTemplatesStore, type SystemTemplateStructure } from '@/stores/system-templates-store'

export interface TemplateInfo {
  id: string
  name: string
  icon: string
  category: string
  shortDescription: string
  tags: string[]
  sampleMessages: string[]
  def: TemplateDef
}

/**
 * 合併範本結構定義 + i18n 字串。
 * <paramref name="tTemplates"/> 必須是 'templates' namespace 的 t 函式
 * （使用 <see cref="useTranslation" />('templates') 取得）。
 */
export function mergeTemplate(
  structure: SystemTemplateStructure,
  tTemplates: TFunction,
): TemplateInfo {
  const itemKey = `items.${structure.id}`
  const name = tTemplates(`${itemKey}.name`, structure.id)
  const shortDescription = tTemplates(`${itemKey}.shortDescription`, '')
  // sampleMessages 可能是陣列，i18next returnObjects 模式
  const sampleMessagesRaw = tTemplates(`${itemKey}.sampleMessages`, {
    returnObjects: true,
    defaultValue: [] as string[],
  })
  const sampleMessages = Array.isArray(sampleMessagesRaw)
    ? (sampleMessagesRaw as string[])
    : []
  const categoryLabel = tTemplates(`categories.${structure.category}`, structure.category)

  return {
    id: structure.id,
    name,
    icon: structure.icon,
    category: categoryLabel,
    shortDescription,
    tags: structure.tags,
    sampleMessages,
    def: structure.def,
  }
}

/**
 * React hook — 取得合併後的系統範本清單（i18n 反應式）。
 * 語言切換時自動重算。
 */
export function useBuiltinTemplates(): TemplateInfo[] {
  const { t } = useTranslation('templates')
  const structures = useSystemTemplatesStore((s) => s.templates)
  return useMemo(() => structures.map((s) => mergeTemplate(s, t)), [structures, t])
}

/** 取得範本分類清單（以 i18n 顯示字串去重，保留原順序）。 */
export function useTemplateCategories(): string[] {
  const templates = useBuiltinTemplates()
  return useMemo(() => [...new Set(templates.map((t) => t.category))], [templates])
}

/** 將範本轉為完整的 React Flow nodes + edges（供 consumer 匯入畫布用）。 */
export function getTemplateWorkflow(
  templateId: string,
  structures: SystemTemplateStructure[],
): TemplateWorkflow | null {
  const tpl = structures.find((t) => t.id === templateId)
  if (!tpl) return null
  return buildTemplate(tpl.def)
}

/**
 * Sync 版本 — 從 store 直接取結構（非 hook，給事件處理器用）。
 * 用法：const { templates } = useSystemTemplatesStore.getState();
 *       getTemplateWorkflowSync(id, templates)
 */
export function getTemplateWorkflowSync(templateId: string): TemplateWorkflow | null {
  const { templates } = useSystemTemplatesStore.getState()
  return getTemplateWorkflow(templateId, templates)
}
