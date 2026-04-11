/**
 * WorkflowSettingsDialog — Workflow 設定彈窗（兩個 Tab：General + Hooks）。
 */
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { X, Plus, Trash2 } from 'lucide-react'
import { useWorkflowStore, type WorkflowSettings } from '@/stores/workflow-store'

const WORKFLOW_TYPES: { value: WorkflowSettings['type']; labelKey: string; descKey: string }[] = [
  { value: 'auto', labelKey: 'settings.typeAuto', descKey: 'settings.typeAutoDesc' },
  { value: 'imperative', labelKey: 'settings.typeImperative', descKey: 'settings.typeImperativeDesc' },
  { value: 'sequential', labelKey: 'settings.typeSequential', descKey: 'settings.typeSequentialDesc' },
  { value: 'concurrent', labelKey: 'settings.typeConcurrent', descKey: 'settings.typeConcurrentDesc' },
  { value: 'handoff', labelKey: 'settings.typeHandoff', descKey: 'settings.typeHandoffDesc' },
]

const HOOK_POINTS = [
  { id: 'onInput', labelKey: 'settings.hookOnInput', descKey: 'settings.hookOnInputDesc' },
  { id: 'preExecute', labelKey: 'settings.hookPreExecute', descKey: 'settings.hookPreExecuteDesc' },
  { id: 'preAgent', labelKey: 'settings.hookPreAgent', descKey: 'settings.hookPreAgentDesc' },
  { id: 'postAgent', labelKey: 'settings.hookPostAgent', descKey: 'settings.hookPostAgentDesc' },
  { id: 'onComplete', labelKey: 'settings.hookOnComplete', descKey: 'settings.hookOnCompleteDesc' },
  { id: 'onError', labelKey: 'settings.hookOnError', descKey: 'settings.hookOnErrorDesc' },
]

const TRANSFORM_TYPES = ['template', 'regex-extract', 'regex-replace', 'json-path', 'trim', 'split-take', 'upper', 'lower']

export interface WorkflowHook {
  type: 'code' | 'webhook'
  transformType?: string
  template?: string
  pattern?: string
  replacement?: string
  url?: string
  method?: string
  blockPattern?: string
}

interface Props {
  open: boolean
  onClose: () => void
}

export function WorkflowSettingsDialog({ open, onClose }: Props) {
  const { t } = useTranslation('studio')
  const settings = useWorkflowStore((s) => s.workflowSettings)
  const updateSettings = useWorkflowStore((s) => s.updateSettings)
  const [tab, setTab] = useState<'general' | 'hooks' | 'variables'>('general')

  if (!open) return null

  const hooks = settings.hooks ?? {}

  const setHook = (point: string, hook: WorkflowHook | null) => {
    const next = { ...hooks }
    if (hook) next[point] = hook
    else delete next[point]
    updateSettings({ hooks: next })
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" onClick={onClose}>
      <div className="w-[540px] max-h-[80vh] rounded-lg border border-border bg-card shadow-xl flex flex-col" onClick={(e) => e.stopPropagation()}>
        {/* Header */}
        <div className="flex items-center justify-between border-b border-border px-4 py-3 shrink-0">
          <h2 className="text-sm font-semibold text-foreground">{t('settings.title')}</h2>
          <button onClick={onClose} className="text-muted-foreground hover:text-foreground cursor-pointer"><X size={16} /></button>
        </div>

        {/* Tabs */}
        <div className="flex border-b border-border shrink-0">
          <button onClick={() => setTab('general')}
            className={`flex-1 px-4 py-2 text-xs font-medium cursor-pointer ${tab === 'general' ? 'text-foreground border-b-2 border-blue-500' : 'text-muted-foreground'}`}>
            {t('settings.tabGeneral')}
          </button>
          <button onClick={() => setTab('hooks')}
            className={`flex-1 px-4 py-2 text-xs font-medium cursor-pointer ${tab === 'hooks' ? 'text-foreground border-b-2 border-blue-500' : 'text-muted-foreground'}`}>
            {t('settings.tabHooks')} {Object.keys(hooks).length > 0 && <span className="ml-1 text-[9px] text-blue-400">({Object.keys(hooks).length})</span>}
          </button>
          <button onClick={() => setTab('variables')}
            className={`flex-1 px-4 py-2 text-xs font-medium cursor-pointer ${tab === 'variables' ? 'text-foreground border-b-2 border-blue-500' : 'text-muted-foreground'}`}>
            {t('settings.variables')} {(settings.variables?.length ?? 0) > 0 && <span className="ml-1 text-[9px] text-blue-400">({settings.variables!.length})</span>}
          </button>
        </div>

        <div className="flex-1 overflow-y-auto p-4 space-y-5">
          {tab === 'general' && (
            <>
              {/* Workflow Type */}
              <div>
                <label className="block text-xs font-medium text-muted-foreground mb-2">{t('settings.workflowType')}</label>
                <div className="space-y-1.5">
                  {WORKFLOW_TYPES.map((wt) => (
                    <label key={wt.value} className={`flex items-start gap-2.5 rounded-md border px-3 py-2 cursor-pointer transition-colors ${
                      settings.type === wt.value ? 'border-blue-500/50 bg-blue-500/5' : 'border-border hover:bg-muted/30'
                    }`}>
                      <input type="radio" name="workflowType" value={wt.value} checked={settings.type === wt.value}
                        onChange={() => updateSettings({ type: wt.value })} className="mt-0.5 accent-blue-500" />
                      <div>
                        <div className="text-xs font-medium text-foreground">{t(wt.labelKey)}</div>
                        <div className="text-[10px] text-muted-foreground">{t(wt.descKey)}</div>
                      </div>
                    </label>
                  ))}
                </div>
              </div>

              {/* Max Turns */}
              <div>
                <label className="block text-xs font-medium text-muted-foreground mb-1.5">{t('settings.maxTurns')}</label>
                <div className="flex items-center gap-3">
                  <input type="range" min={1} max={50} value={settings.maxTurns}
                    onChange={(e) => updateSettings({ maxTurns: parseInt(e.target.value) })} className="flex-1 accent-blue-500" />
                  <span className="text-xs text-foreground w-8 text-right">{settings.maxTurns}</span>
                </div>
                <p className="text-[10px] text-muted-foreground mt-1">{t('settings.maxTurnsDesc')}</p>
              </div>

              {/* Termination Strategy */}
              <div>
                <label className="block text-xs font-medium text-muted-foreground mb-1.5">{t('settings.termination')}</label>
                <select className="field-input text-xs" value={settings.terminationStrategy ?? 'none'}
                  onChange={(e) => updateSettings({ terminationStrategy: e.target.value as any })}>
                  <option value="none">{t('settings.termNone')}</option>
                  <option value="maxturns">{t('settings.termMaxTurns')}</option>
                  <option value="keyword">{t('settings.termKeyword')}</option>
                  <option value="combined">{t('settings.termCombined')}</option>
                </select>
              </div>

              {(settings.terminationStrategy === 'keyword' || settings.terminationStrategy === 'combined') && (
                <div>
                  <label className="block text-xs font-medium text-muted-foreground mb-1.5">{t('settings.terminationKeyword')}</label>
                  <input className="field-input text-xs" value={settings.terminationKeyword ?? ''} placeholder="TERMINATE"
                    onChange={(e) => updateSettings({ terminationKeyword: e.target.value })} />
                </div>
              )}

              {/* Context Passing */}
              <div>
                <label className="block text-xs font-medium text-muted-foreground mb-1.5">{t('settings.contextPassing')}</label>
                <select className="field-input text-xs" value={settings.contextPassing ?? 'previous-only'}
                  onChange={(e) => updateSettings({ contextPassing: e.target.value as any })}>
                  <option value="previous-only">{t('settings.ctxPreviousOnly')}</option>
                  <option value="with-original">{t('settings.ctxWithOriginal')}</option>
                  <option value="accumulate">{t('settings.ctxAccumulate')}</option>
                </select>
                <p className="text-[10px] text-muted-foreground mt-1">{t('settings.contextPassingDesc')}</p>
              </div>

              {/* Aggregator (concurrent mode) */}
              {settings.type === 'concurrent' && (
                <div>
                  <label className="block text-xs font-medium text-muted-foreground mb-1.5">{t('settings.aggregator')}</label>
                  <select className="field-input text-xs" value={settings.aggregatorStrategy ?? 'default'}
                    onChange={(e) => updateSettings({ aggregatorStrategy: e.target.value as any })}>
                    <option value="default">{t('settings.aggDefault')}</option>
                    <option value="custom">{t('settings.aggCustom')}</option>
                  </select>
                </div>
              )}
            </>
          )}

          {tab === 'variables' && (
            <VariablesTab
              variables={settings.variables ?? []}
              onChange={(vars) => updateSettings({ variables: vars })}
              t={t}
            />
          )}

          {tab === 'hooks' && (
            <div className="space-y-3">
              <p className="text-[10px] text-muted-foreground">{t('settings.hooksDesc')}</p>
              {HOOK_POINTS.map((hp) => {
                const hook: WorkflowHook | undefined = hooks[hp.id]
                return (
                  <div key={hp.id} className="rounded-md border border-border p-3">
                    <div className="flex items-center justify-between mb-1">
                      <div>
                        <span className="text-[11px] font-medium text-foreground">{t(hp.labelKey)}</span>
                        <span className="text-[9px] text-muted-foreground ml-2">{t(hp.descKey)}</span>
                      </div>
                      {hook ? (
                        <button onClick={() => setHook(hp.id, null)} className="text-muted-foreground hover:text-red-400 cursor-pointer"><Trash2 size={12} /></button>
                      ) : (
                        <button onClick={() => setHook(hp.id, { type: 'code', transformType: 'template', template: '{{input}}' })}
                          className="flex items-center gap-0.5 text-[9px] text-blue-400 hover:text-blue-300 cursor-pointer"><Plus size={11} /> {t('settings.hookAdd')}</button>
                      )}
                    </div>
                    {hook && (
                      <div className="mt-2 space-y-2">
                        <select className="field-input text-[10px]" value={hook.type}
                          onChange={(e) => setHook(hp.id, { ...hook, type: e.target.value as 'code' | 'webhook' })}>
                          <option value="code">Code (Transform)</option>
                          <option value="webhook">Webhook (HTTP)</option>
                        </select>
                        {hook.type === 'code' && (
                          <>
                            <select className="field-input text-[10px]" value={hook.transformType ?? 'template'}
                              onChange={(e) => setHook(hp.id, { ...hook, transformType: e.target.value })}>
                              {TRANSFORM_TYPES.map((tt) => <option key={tt} value={tt}>{tt}</option>)}
                            </select>
                            <textarea className="field-textarea text-[10px] font-mono" rows={2} value={hook.template ?? ''}
                              onChange={(e) => setHook(hp.id, { ...hook, template: e.target.value })} placeholder="{{input}}" />
                          </>
                        )}
                        {hook.type === 'webhook' && (
                          <>
                            <input className="field-input text-[10px]" value={hook.url ?? ''} placeholder="https://..."
                              onChange={(e) => setHook(hp.id, { ...hook, url: e.target.value })} />
                            <select className="field-input text-[10px]" value={hook.method ?? 'POST'}
                              onChange={(e) => setHook(hp.id, { ...hook, method: e.target.value })}>
                              <option value="POST">POST</option>
                              <option value="PUT">PUT</option>
                              <option value="GET">GET</option>
                            </select>
                          </>
                        )}
                        {(hp.id === 'onInput' || hp.id === 'preExecute') && (
                          <div>
                            <label className="text-[9px] text-muted-foreground">Block Pattern (regex)</label>
                            <input className="field-input text-[10px] font-mono" value={hook.blockPattern ?? ''}
                              onChange={(e) => setHook(hp.id, { ...hook, blockPattern: e.target.value })} placeholder="Optional regex to block input" />
                          </div>
                        )}
                      </div>
                    )}
                  </div>
                )
              })}
            </div>
          )}
        </div>
      </div>
    </div>
  )
}

// ─── Variables Tab ───

import type { WorkflowVariable } from '@/types/workflow'

const VAR_TYPES: WorkflowVariable['type'][] = ['string', 'number', 'boolean', 'json']

function VariablesTab({ variables, onChange, t }: {
  variables: WorkflowVariable[]
  onChange: (vars: WorkflowVariable[]) => void
  t: (key: string) => string
}) {
  const addVariable = () => {
    onChange([...variables, { name: '', type: 'string', defaultValue: '', description: '' }])
  }

  const updateVar = (index: number, partial: Partial<WorkflowVariable>) => {
    const next = [...variables]
    next[index] = { ...next[index], ...partial }
    onChange(next)
  }

  const removeVar = (index: number) => {
    onChange(variables.filter((_, i) => i !== index))
  }

  return (
    <div className="space-y-3">
      <p className="text-[10px] text-muted-foreground">{t('settings.variablesDesc')}</p>

      {variables.map((v, i) => (
        <div key={i} className="rounded-md border border-border p-3 space-y-2">
          <div className="flex items-center gap-2">
            <input
              className="field-input"
              style={{ flex: 1, minWidth: 0 }}
              value={v.name}
              onChange={(e) => updateVar(i, { name: e.target.value.replace(/\s/g, '_') })}
              placeholder={t('settings.varName')}
            />
            <select
              className="field-input"
              style={{ width: 96 }}
              value={v.type}
              onChange={(e) => updateVar(i, { type: e.target.value as WorkflowVariable['type'] })}
            >
              {VAR_TYPES.map((vt) => <option key={vt} value={vt}>{vt}</option>)}
            </select>
            <button onClick={() => removeVar(i)}
              className="text-muted-foreground hover:text-red-400 cursor-pointer p-1">
              <Trash2 size={13} />
            </button>
          </div>
          <input
            className="field-input"
            value={v.defaultValue}
            onChange={(e) => updateVar(i, { defaultValue: e.target.value })}
            placeholder={t('settings.varDefault')}
          />
          <input
            className="field-input text-[10px]"
            value={v.description}
            onChange={(e) => updateVar(i, { description: e.target.value })}
            placeholder={t('settings.varDescription')}
          />
        </div>
      ))}

      <button onClick={addVariable}
        className="flex items-center gap-1 rounded-md border border-dashed border-border px-3 py-2 text-[11px] text-muted-foreground hover:text-foreground hover:border-muted-foreground/50 transition-colors cursor-pointer w-full justify-center">
        <Plus size={12} /> {t('settings.addVariable')}
      </button>
    </div>
  )
}
