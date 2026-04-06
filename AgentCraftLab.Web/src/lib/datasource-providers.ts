/**
 * DataSource Provider 定義 — metadata-driven 動態表單。
 * 新增 Provider 只需在 providers 陣列加一筆，前端表單自動渲染。
 */

export interface ProviderFieldDef {
  key: string
  labelKey: string
  type: 'text' | 'number' | 'password'
  required: boolean
  defaultValue?: string | number
  placeholder?: string
  gridSpan?: 1 | 2        // 1 = 半寬, 2 = 全寬（預設 2）
}

export interface ProviderDef {
  id: string
  labelKey: string
  descriptionKey: string
  fields: ProviderFieldDef[]
  testable: boolean
}

export const providers: ProviderDef[] = [
  {
    id: 'sqlite',
    labelKey: 'dataSource.provider.sqlite',
    descriptionKey: 'dataSource.provider.sqliteDesc',
    fields: [],
    testable: false,
  },
  {
    id: 'pgvector',
    labelKey: 'dataSource.provider.pgvector',
    descriptionKey: 'dataSource.provider.pgvectorDesc',
    fields: [
      { key: 'host',     labelKey: 'dataSource.field.host',     type: 'text',     required: true, defaultValue: 'localhost', gridSpan: 1 },
      { key: 'port',     labelKey: 'dataSource.field.port',     type: 'number',   required: true, defaultValue: 5432, gridSpan: 1 },
      { key: 'database', labelKey: 'dataSource.field.database', type: 'text',     required: true, gridSpan: 1 },
      { key: 'username', labelKey: 'dataSource.field.username', type: 'text',     required: true, gridSpan: 1 },
      { key: 'password', labelKey: 'dataSource.field.password', type: 'password', required: true, gridSpan: 2 },
    ],
    testable: true,
  },
  {
    id: 'qdrant',
    labelKey: 'dataSource.provider.qdrant',
    descriptionKey: 'dataSource.provider.qdrantDesc',
    fields: [
      { key: 'url',    labelKey: 'dataSource.field.url',    type: 'text',     required: true, defaultValue: 'http://localhost:6333', gridSpan: 2 },
      { key: 'apiKey', labelKey: 'dataSource.field.apiKey', type: 'password', required: false, gridSpan: 2 },
    ],
    testable: true,
  },
]

export function getProvider(id: string): ProviderDef | undefined {
  return providers.find(p => p.id === id)
}

/** 從 provider fields 建立 config 物件（含預設值）。 */
export function buildDefaultConfig(providerDef: ProviderDef): Record<string, string | number> {
  const config: Record<string, string | number> = {}
  for (const f of providerDef.fields) {
    config[f.key] = f.defaultValue ?? (f.type === 'number' ? 0 : '')
  }
  return config
}
