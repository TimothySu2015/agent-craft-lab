import { describe, it, expect, vi, beforeAll } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { getTemplateWorkflow, mergeTemplate } from '../templates'
import type { SystemTemplateStructure } from '@/stores/system-templates-store'
import type { TFunction } from 'i18next'

// Mock NODE_REGISTRY (used by buildTemplate via template-builder)
vi.mock('@/components/studio/nodes/registry', () => ({
  NODE_REGISTRY: new Proxy({}, {
    get: (_target, type: string) => ({
      type,
      defaultData: (name: string) => ({ type, name }),
    }),
  }),
}))

// 直接從 public/templates.json 讀取結構（測試環境下不走 fetch）。
let structures: SystemTemplateStructure[]

beforeAll(() => {
  const jsonPath = resolve(__dirname, '../../../public/templates.json')
  const raw = readFileSync(jsonPath, 'utf-8')
  const data = JSON.parse(raw) as { templates: SystemTemplateStructure[] }
  structures = data.templates
})

// 最小 TFunction stub：依 key 回傳 defaultValue（或 key 本身），
// sampleMessages 回空陣列 — 只要驗證 merge 邏輯不炸，不驗證翻譯內容。
const stubT: TFunction = ((key: string, opts?: unknown) => {
  if (typeof opts === 'object' && opts !== null && 'returnObjects' in opts) {
    return []
  }
  if (typeof opts === 'object' && opts !== null && 'defaultValue' in opts) {
    return (opts as { defaultValue: unknown }).defaultValue
  }
  if (typeof opts === 'string') return opts
  return key
}) as unknown as TFunction

describe('System templates (public/templates.json)', () => {
  it('has at least 20 templates', () => {
    expect(structures.length).toBeGreaterThanOrEqual(20)
  })

  it('each template structure has required fields', () => {
    for (const t of structures) {
      expect(t.id).toBeTruthy()
      expect(t.icon).toBeTruthy()
      expect(t.category).toBeTruthy()
      expect(Array.isArray(t.tags)).toBe(true)
      expect(t.def).toBeDefined()
      expect(Array.isArray(t.def.nodes)).toBe(true)
      expect(Array.isArray(t.def.connections)).toBe(true)
    }
  })

  it('has unique IDs', () => {
    const ids = structures.map((t) => t.id)
    expect(new Set(ids).size).toBe(ids.length)
  })

  it('all connection indices reference valid nodes', () => {
    for (const t of structures) {
      const maxIndex = t.def.nodes.length - 1
      for (const conn of t.def.connections) {
        expect(conn.from).toBeGreaterThanOrEqual(0)
        expect(conn.from).toBeLessThanOrEqual(maxIndex)
        expect(conn.to).toBeGreaterThanOrEqual(0)
        expect(conn.to).toBeLessThanOrEqual(maxIndex)
      }
    }
  })

  it('all node types are valid', () => {
    const validTypes = [
      'agent', 'tool', 'rag', 'condition', 'loop', 'router',
      'a2a-agent', 'human', 'code', 'iteration', 'parallel',
      'http-request', 'autonomous', 'start', 'end',
    ]
    for (const t of structures) {
      for (const nd of t.def.nodes) {
        expect(validTypes).toContain(nd.type)
      }
    }
  })

  it('uses known category keys', () => {
    const validCategories = [
      'basic', 'tools-search', 'rag-files', 'advanced',
      'human-in-the-loop', 'skills', 'autonomous',
    ]
    for (const t of structures) {
      expect(validCategories).toContain(t.category)
    }
  })
})

describe('mergeTemplate', () => {
  it('merges structure with i18n strings', () => {
    const sample = structures.find((s) => s.id === 'sequential')!
    const merged = mergeTemplate(sample, stubT)
    expect(merged.id).toBe('sequential')
    expect(merged.icon).toBe(sample.icon)
    expect(merged.tags).toEqual(sample.tags)
    expect(merged.def).toBe(sample.def)
    expect(Array.isArray(merged.sampleMessages)).toBe(true)
  })
})

describe('getTemplateWorkflow', () => {
  it('returns workflow for valid template ID', () => {
    const result = getTemplateWorkflow('sequential', structures)
    expect(result).not.toBeNull()
    expect(result!.nodes.length).toBeGreaterThan(0)
    expect(Array.isArray(result!.edges)).toBe(true)
  })

  it('returns null for unknown template ID', () => {
    expect(getTemplateWorkflow('nonexistent', structures)).toBeNull()
  })

  it('includes Start and End nodes', () => {
    const result = getTemplateWorkflow('sequential', structures)!
    expect(result.nodes[0].type).toBe('start')
    expect(result.nodes[result.nodes.length - 1].type).toBe('end')
  })
})

describe('i18n locale files parity', () => {
  const locales = ['en', 'zh-TW', 'ja']

  for (const lng of locales) {
    it(`locale '${lng}' has entries for all templates`, () => {
      const jsonPath = resolve(__dirname, `../../../public/locales/${lng}/templates.json`)
      const raw = readFileSync(jsonPath, 'utf-8')
      const data = JSON.parse(raw) as {
        categories: Record<string, string>
        items: Record<string, { name: string; shortDescription: string; sampleMessages: string[] }>
      }

      // 所有範本 id 都有對應的 i18n 項目
      for (const t of structures) {
        expect(data.items[t.id], `locale ${lng} missing item: ${t.id}`).toBeDefined()
        const item = data.items[t.id]
        expect(item.name).toBeTruthy()
        expect(item.shortDescription).toBeTruthy()
        expect(Array.isArray(item.sampleMessages)).toBe(true)
        expect(item.sampleMessages.length).toBeGreaterThan(0)
      }

      // 所有結構用到的 category 都有對應翻譯
      const usedCategories = new Set(structures.map((t) => t.category))
      for (const cat of usedCategories) {
        expect(data.categories[cat], `locale ${lng} missing category: ${cat}`).toBeTruthy()
      }
    })
  }
})
