/**
 * DataSourceSection — Settings 頁面的資料來源連線管理區塊。
 * 展開式列表 + 新增/編輯 Dialog + 測試連線。
 */
import { useState, useEffect, useCallback } from 'react'
import { useConfirmDialog } from '@/components/shared/ConfirmDialog'
import { useTranslation } from 'react-i18next'
import { Database, Plus, Trash2, Edit3, X, ChevronDown, ChevronRight, Loader2, CheckCircle, XCircle, Eye, EyeOff } from 'lucide-react'
import { api, type DataSourceDocument } from '@/lib/api'
import { providers, getProvider, buildDefaultConfig, type ProviderDef } from '@/lib/datasource-providers'
import { notify } from '@/lib/notify'

interface DataSourceSectionProps {
  SectionCard: React.ComponentType<{
    icon: React.ElementType; title: string; description?: string; children: React.ReactNode; iconColor?: string
  }>
}

export function DataSourceSection({ SectionCard }: DataSourceSectionProps) {
  const { t } = useTranslation(['studio', 'common'])
  const { t: tn } = useTranslation('notifications')
  const { confirm, confirmDialog } = useConfirmDialog()
  const [dataSources, setDataSources] = useState<DataSourceDocument[]>([])
  const [loading, setLoading] = useState(true)
  const [expandedId, setExpandedId] = useState<string | null>(null)

  // dialog state
  const [showDialog, setShowDialog] = useState(false)
  const [editingDs, setEditingDs] = useState<DataSourceDocument | null>(null)
  const [formName, setFormName] = useState('')
  const [formDesc, setFormDesc] = useState('')
  const [formProvider, setFormProvider] = useState('sqlite')
  const [formConfig, setFormConfig] = useState<Record<string, string | number>>({})
  const [saving, setSaving] = useState(false)

  // test state
  const [testingId, setTestingId] = useState<string | null>(null)
  const [testResult, setTestResult] = useState<{ id: string; success: boolean; message: string } | null>(null)

  // password visibility per field
  const [visiblePasswords, setVisiblePasswords] = useState<Set<string>>(new Set())

  const fetchDataSources = useCallback(async () => {
    try {
      const data = await api.dataSources.list()
      setDataSources(data)
    } catch (err) {
      console.error('Failed to load data sources:', err)
      notify.error(tn('loadFailed.dataSources'))
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { fetchDataSources() }, [fetchDataSources])

  const openCreateDialog = () => {
    setEditingDs(null)
    setFormName('')
    setFormDesc('')
    setFormProvider('sqlite')
    setFormConfig({})
    setVisiblePasswords(new Set())
    setShowDialog(true)
  }

  const openEditDialog = (ds: DataSourceDocument) => {
    setEditingDs(ds)
    setFormName(ds.name)
    setFormDesc(ds.description)
    setFormProvider(ds.provider)
    try {
      setFormConfig(JSON.parse(ds.configJson))
    } catch {
      setFormConfig({})
    }
    setVisiblePasswords(new Set())
    setShowDialog(true)
  }

  const handleProviderChange = (providerId: string) => {
    setFormProvider(providerId)
    const def = getProvider(providerId)
    if (def) {
      setFormConfig(buildDefaultConfig(def))
    }
  }

  const handleSave = async () => {
    if (!formName.trim()) return
    setSaving(true)
    try {
      const configJson = JSON.stringify(formConfig)
      if (editingDs) {
        await api.dataSources.update(editingDs.id, { name: formName, description: formDesc, provider: formProvider, configJson })
      } else {
        await api.dataSources.create({ name: formName, description: formDesc, provider: formProvider, configJson })
      }
      setShowDialog(false)
      await fetchDataSources()
    } catch (err) {
      console.error('Failed to save data source:', err)
      notify.error(tn('saveFailed.dataSource'), { description: (err as any)?.message })
    } finally {
      setSaving(false)
    }
  }

  const handleDelete = async (ds: DataSourceDocument) => {
    if (!await confirm(t('dataSource.confirmDelete', { name: ds.name }))) return
    try {
      await api.dataSources.delete(ds.id)
      if (expandedId === ds.id) setExpandedId(null)
      await fetchDataSources()
    } catch (err: any) {
      if (err?.code === 'DS_IN_USE') {
        notify.error(t('dataSource.inUse', { count: err.params?.count ?? '?' }))
      } else {
        console.error('Failed to delete data source:', err)
        notify.error(tn('deleteFailed.dataSource'), { description: (err as any)?.message })
      }
    }
  }

  const handleTest = async (ds: DataSourceDocument) => {
    setTestingId(ds.id)
    setTestResult(null)
    try {
      const result = await api.dataSources.test(ds.id)
      setTestResult({ id: ds.id, success: result.success, message: result.success ? t('dataSource.testSuccess', { ms: result.latencyMs }) : result.message })
    } catch {
      setTestResult({ id: ds.id, success: false, message: t('dataSource.testFailed', { error: 'Network error' }) })
    } finally {
      setTestingId(null)
    }
  }

  const togglePasswordVisibility = (fieldKey: string) => {
    setVisiblePasswords(prev => {
      const next = new Set(prev)
      if (next.has(fieldKey)) next.delete(fieldKey)
      else next.add(fieldKey)
      return next
    })
  }

  const providerDef = getProvider(formProvider)

  return (
    <>
      <SectionCard icon={Database} title={t('dataSource.title')}
        description={t('dataSource.description')} iconColor="text-teal-400">

        {loading ? (
          <p className="text-xs text-muted-foreground">{t('common:loading')}</p>
        ) : (
          <div className="space-y-0 rounded-lg border border-border overflow-hidden">
            {dataSources.length === 0 && (
              <div className="px-4 py-6 text-center">
                <p className="text-xs text-muted-foreground mb-3">{t('dataSource.empty')}</p>
              </div>
            )}

            {dataSources.map((ds, i) => {
              const isExpanded = expandedId === ds.id
              const pDef = getProvider(ds.provider)
              const configObj = (() => { try { return JSON.parse(ds.configJson) } catch { return {} } })()

              return (
                <div key={ds.id} className={i > 0 ? 'border-t border-border' : ''}>
                  <div
                    className="flex items-center gap-3 px-4 py-2.5 cursor-pointer hover:bg-secondary/30 transition-colors group"
                    onClick={() => setExpandedId(isExpanded ? null : ds.id)}
                  >
                    {isExpanded ? <ChevronDown size={13} className="text-muted-foreground shrink-0" /> : <ChevronRight size={13} className="text-muted-foreground shrink-0" />}
                    <div className="flex-1 min-w-0">
                      <div className="flex items-center gap-2">
                        <span className="text-xs font-medium text-foreground truncate">{ds.name}</span>
                        <span className="rounded bg-muted/50 px-1.5 py-0.5 text-[9px] text-muted-foreground">{pDef ? t(pDef.labelKey) : ds.provider}</span>
                      </div>
                    </div>
                    <div className="flex items-center gap-1 opacity-0 group-hover:opacity-100 transition-opacity">
                      {pDef?.testable && (
                        <button onClick={(e) => { e.stopPropagation(); handleTest(ds) }}
                          className="text-muted-foreground hover:text-blue-400 cursor-pointer p-1" title={t('dataSource.testConnection')}>
                          {testingId === ds.id ? <Loader2 size={12} className="animate-spin" /> : <CheckCircle size={12} />}
                        </button>
                      )}
                      <button onClick={(e) => { e.stopPropagation(); openEditDialog(ds) }}
                        className="text-muted-foreground hover:text-foreground cursor-pointer p-1">
                        <Edit3 size={12} />
                      </button>
                      <button onClick={(e) => { e.stopPropagation(); handleDelete(ds) }}
                        className="text-muted-foreground hover:text-red-400 cursor-pointer p-1">
                        <Trash2 size={12} />
                      </button>
                    </div>
                  </div>

                  {isExpanded && (
                    <div className="px-4 pb-3 pl-10">
                      {ds.description && <p className="text-[10px] text-muted-foreground mb-2">{ds.description}</p>}
                      <div className="flex flex-wrap gap-x-4 gap-y-1 text-[10px] text-muted-foreground">
                        {pDef?.fields.filter(f => f.type !== 'password').map(f => (
                          <span key={f.key}><span className="text-muted-foreground/60">{t(f.labelKey)}:</span> {configObj[f.key] ?? '-'}</span>
                        ))}
                      </div>
                      {testResult?.id === ds.id && (
                        <div className={`mt-2 flex items-center gap-1.5 text-[10px] ${testResult.success ? 'text-green-400' : 'text-red-400'}`}>
                          {testResult.success ? <CheckCircle size={11} /> : <XCircle size={11} />}
                          {testResult.message}
                        </div>
                      )}
                    </div>
                  )}
                </div>
              )
            })}

            <div className={dataSources.length > 0 ? 'border-t border-border' : ''}>
              <button onClick={openCreateDialog}
                className="w-full flex items-center justify-center gap-1.5 px-4 py-2.5 text-xs text-muted-foreground hover:text-foreground hover:bg-secondary/30 transition-colors cursor-pointer">
                <Plus size={13} /> {t('dataSource.create')}
              </button>
            </div>
          </div>
        )}
      </SectionCard>

      {/* Create/Edit Dialog */}
      {showDialog && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" onClick={() => setShowDialog(false)}>
          <div className="w-[440px] rounded-lg border border-border bg-card shadow-xl" onClick={(e) => e.stopPropagation()}>
            <div className="flex items-center justify-between border-b border-border px-4 py-3">
              <h2 className="text-sm font-semibold text-foreground">
                {editingDs ? t('dataSource.edit') : t('dataSource.create')}
              </h2>
              <button onClick={() => setShowDialog(false)} className="text-muted-foreground hover:text-foreground cursor-pointer"><X size={16} /></button>
            </div>
            <div className="p-4 space-y-3">
              <div>
                <label className="block text-[10px] text-muted-foreground mb-1">{t('dataSource.field.name')}</label>
                <input className="field-input" value={formName} onChange={(e) => setFormName(e.target.value)} autoFocus />
              </div>
              <div>
                <label className="block text-[10px] text-muted-foreground mb-1">{t('dataSource.field.description')}</label>
                <input className="field-input" value={formDesc} onChange={(e) => setFormDesc(e.target.value)} />
              </div>
              <div>
                <label className="block text-[10px] text-muted-foreground mb-1">{t('dataSource.field.provider')}</label>
                <select className="field-input" value={formProvider}
                  onChange={(e) => handleProviderChange(e.target.value)}
                  disabled={!!editingDs}>
                  {providers.map(p => (
                    <option key={p.id} value={p.id}>{t(p.labelKey)}</option>
                  ))}
                </select>
                {providerDef && <p className="text-[9px] text-muted-foreground mt-0.5">{t(providerDef.descriptionKey)}</p>}
              </div>

              {/* Dynamic Provider Fields */}
              {providerDef && providerDef.fields.length > 0 && (
                <div className="grid grid-cols-2 gap-3">
                  {providerDef.fields.map(f => (
                    <div key={f.key} className={f.gridSpan === 1 ? 'col-span-1' : 'col-span-2'}>
                      <label className="block text-[10px] text-muted-foreground mb-1">
                        {t(f.labelKey)}{f.required && <span className="text-red-400 ml-0.5">*</span>}
                      </label>
                      <div className="relative">
                        <input
                          type={f.type === 'password' && !visiblePasswords.has(f.key) ? 'password' : f.type === 'number' ? 'number' : 'text'}
                          className="field-input pr-8"
                          value={formConfig[f.key] ?? ''}
                          placeholder={f.placeholder ?? (f.defaultValue !== undefined ? String(f.defaultValue) : '')}
                          onChange={(e) => setFormConfig(prev => ({
                            ...prev,
                            [f.key]: f.type === 'number' ? (parseInt(e.target.value) || 0) : e.target.value
                          }))}
                        />
                        {f.type === 'password' && (
                          <button
                            type="button"
                            onClick={() => togglePasswordVisibility(f.key)}
                            className="absolute right-2 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground cursor-pointer"
                          >
                            {visiblePasswords.has(f.key) ? <EyeOff size={12} /> : <Eye size={12} />}
                          </button>
                        )}
                      </div>
                    </div>
                  ))}
                </div>
              )}

              <button
                onClick={handleSave}
                disabled={!formName.trim() || saving}
                className="w-full rounded-md bg-blue-600 px-3 py-2 text-xs font-semibold text-white hover:bg-blue-500 disabled:opacity-50 transition-colors cursor-pointer"
              >
                {saving ? <Loader2 size={13} className="animate-spin mx-auto" /> : (editingDs ? t('common:save') : t('dataSource.create'))}
              </button>
            </div>
          </div>
        </div>
      )}
      {confirmDialog}
    </>
  )
}
