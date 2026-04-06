/**
 * MiddlewareConfigDialog — 每個 middleware 的詳細設定。
 * 對應後端 AgentContextBuilder.ApplyMiddleware() 的 config dictionary。
 */
import { useState, useEffect } from 'react'
import { X } from 'lucide-react'
import { useTranslation } from 'react-i18next'

interface MiddlewareConfig {
  [key: string]: { [key: string]: string }
}

interface CheckboxGroupOption {
  value: string
  label: string
}

interface FieldDef {
  key: string
  label: string
  type: 'textarea' | 'select' | 'checkbox' | 'number' | 'checkboxGroup'
  placeholder?: string
  help?: string
  options?: string[] | CheckboxGroupOption[]
  showWhen?: { [key: string]: string }
}

interface MiddlewareDef {
  id: string
  label: string
  fields: FieldDef[]
}

interface Props {
  open: boolean
  middleware: string  // comma-separated active middleware
  config: MiddlewareConfig
  onClose: () => void
  onApply: (middleware: string, config: MiddlewareConfig) => void
}

const PII_LOCALE_OPTIONS: CheckboxGroupOption[] = [
  { value: 'global', label: 'middlewareConfig.pii.localeGlobal' },
  { value: 'tw', label: 'middlewareConfig.pii.localeTW' },
  { value: 'jp', label: 'middlewareConfig.pii.localeJP' },
  { value: 'kr', label: 'middlewareConfig.pii.localeKR' },
  { value: 'us', label: 'middlewareConfig.pii.localeUS' },
  { value: 'uk', label: 'middlewareConfig.pii.localeUK' },
]

const MIDDLEWARE_DEFS: MiddlewareDef[] = [
  { id: 'guardrails', label: 'GuardRails', fields: [
    { key: 'scanAllMessages', label: 'middlewareConfig.guardrails.scanAllMessages', type: 'checkbox', help: 'middlewareConfig.guardrails.scanAllMessagesHelp' },
    { key: 'scanOutput', label: 'middlewareConfig.guardrails.scanOutput', type: 'checkbox', help: 'middlewareConfig.guardrails.scanOutputHelp' },
    { key: 'enableInjectionDetection', label: 'middlewareConfig.guardrails.injectionDetection', type: 'checkbox', help: 'middlewareConfig.guardrails.injectionDetectionHelp' },
    { key: 'blockedTerms', label: 'middlewareConfig.guardrails.blockedTerms', type: 'textarea', placeholder: 'hack, attack, 密碼', help: 'middlewareConfig.guardrails.blockedTermsHelp' },
    { key: 'warnTerms', label: 'middlewareConfig.guardrails.warnTerms', type: 'textarea', placeholder: 'gambling, 投資', help: 'middlewareConfig.guardrails.warnTermsHelp' },
    { key: 'regexRules', label: 'middlewareConfig.guardrails.regexRules', type: 'textarea', placeholder: '\\d{4}-\\d{4}-\\d{4}-\\d{4}', help: 'middlewareConfig.guardrails.regexRulesHelp' },
    { key: 'allowedTopics', label: 'middlewareConfig.guardrails.allowedTopics', type: 'textarea', placeholder: 'cooking, recipes, food', help: 'middlewareConfig.guardrails.allowedTopicsHelp' },
    { key: 'blockedResponse', label: 'middlewareConfig.guardrails.blockedResponse', type: 'textarea', placeholder: 'Sorry, this request cannot be processed.', help: 'middlewareConfig.guardrails.blockedResponseHelp' },
  ]},
  { id: 'pii', label: 'PII Masking', fields: [
    { key: 'mode', label: 'middlewareConfig.pii.mode', type: 'select', options: ['irreversible', 'reversible'], help: 'middlewareConfig.pii.modeHelp' },
    { key: 'confidenceThreshold', label: 'middlewareConfig.pii.confidenceThreshold', type: 'number', placeholder: '0.5', help: 'middlewareConfig.pii.confidenceHelp' },
    { key: 'locales', label: 'middlewareConfig.pii.locales', type: 'checkboxGroup', options: PII_LOCALE_OPTIONS, help: 'middlewareConfig.pii.localesHelp' },
    { key: 'scanOutput', label: 'middlewareConfig.pii.scanOutput', type: 'checkbox', help: 'middlewareConfig.pii.scanOutputHelp' },
    { key: 'replacement', label: 'middlewareConfig.pii.replacement', type: 'select', options: ['***', '[REDACTED]', '[PII]'], help: 'middlewareConfig.pii.replacementHelp', showWhen: { mode: 'irreversible' } },
    { key: 'customRules', label: 'middlewareConfig.pii.customRules', type: 'textarea', placeholder: 'Label:regex|Label2:regex2', help: 'middlewareConfig.pii.customRulesHelp' },
  ]},
  { id: 'ratelimit', label: 'Rate Limit', fields: [
    { key: 'maxPerMinute', label: 'Max Requests/Minute', type: 'number', placeholder: '60' },
    { key: 'cooldownMs', label: 'Cooldown (ms)', type: 'number', placeholder: '1000' },
  ]},
  { id: 'retry', label: 'Retry', fields: [
    { key: 'maxRetries', label: 'Max Retries', type: 'number', placeholder: '3' },
    { key: 'strategy', label: 'Strategy', type: 'select', options: ['exponential', 'fixed', 'linear'], help: 'Backoff strategy between retries' },
    { key: 'initialDelayMs', label: 'Initial Delay (ms)', type: 'number', placeholder: '1000' },
  ]},
  { id: 'logging', label: 'Logging', fields: [
    { key: 'level', label: 'Log Level', type: 'select', options: ['verbose', 'info', 'warning', 'error'] },
    { key: 'logPrompts', label: 'Log Prompts', type: 'checkbox' },
    { key: 'logResponses', label: 'Log Responses', type: 'checkbox' },
  ]},
]

export function MiddlewareConfigDialog({ open, middleware, config, onClose, onApply }: Props) {
  const { t } = useTranslation('studio')
  const [active, setActive] = useState<Set<string>>(new Set())
  const [localConfig, setLocalConfig] = useState<MiddlewareConfig>({})
  const [selectedMw, setSelectedMw] = useState<string>('')

  useEffect(() => {
    if (!open) return
    const items = middleware.split(',').map((s) => s.trim()).filter(Boolean)
    setActive(new Set(items))
    setLocalConfig(structuredClone(config ?? {}))
    setSelectedMw(items[0] || MIDDLEWARE_DEFS[0].id)
  }, [open, middleware, config])

  const toggleMw = (id: string) => {
    setActive((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  const setField = (mwId: string, key: string, value: string) => {
    setLocalConfig((prev) => ({
      ...prev,
      [mwId]: { ...prev[mwId], [key]: value },
    }))
  }

  const handleApply = () => {
    onApply([...active].join(','), localConfig)
    onClose()
  }

  /** Toggle a value in a comma-separated string field (for checkboxGroup) */
  const toggleGroupValue = (mwId: string, key: string, value: string) => {
    const current = (localConfig[mwId]?.[key] ?? '').split(',').map(s => s.trim()).filter(Boolean)
    const idx = current.indexOf(value)
    if (idx >= 0) {
      current.splice(idx, 1)
    } else {
      current.push(value)
    }
    setField(mwId, key, current.join(','))
  }

  /** Check if a field should be visible based on showWhen condition */
  const isFieldVisible = (mwId: string, field: FieldDef): boolean => {
    if (!field.showWhen) return true
    return Object.entries(field.showWhen).every(
      ([k, v]) => (localConfig[mwId]?.[k] ?? '') === v
    )
  }

  /** Resolve label: if it looks like an i18n key (contains '.'), translate it; otherwise return as-is */
  const resolveLabel = (label: string): string => {
    return label.includes('.') ? t(label) : label
  }

  if (!open) return null

  const selectedDef = MIDDLEWARE_DEFS.find((d) => d.id === selectedMw)

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" onClick={onClose}>
      <div className="w-[600px] max-h-[70vh] rounded-lg border border-border bg-card shadow-xl flex flex-col" onClick={(e) => e.stopPropagation()}>
        <div className="flex items-center justify-between border-b border-border px-4 py-3 shrink-0">
          <h2 className="text-sm font-semibold text-foreground">{t('middlewareConfig.title')}</h2>
          <button onClick={onClose} className="text-muted-foreground hover:text-foreground cursor-pointer"><X size={16} /></button>
        </div>

        <div className="flex flex-1 overflow-hidden">
          {/* Left: middleware list */}
          <div className="w-[180px] border-r border-border overflow-y-auto py-2">
            {MIDDLEWARE_DEFS.map((mw) => {
              const isActive = active.has(mw.id)
              const isSelected = selectedMw === mw.id
              return (
                <div key={mw.id} className={`flex items-center gap-2 px-3 py-1.5 cursor-pointer transition-colors ${isSelected ? 'bg-accent' : 'hover:bg-accent/50'}`}
                  onClick={() => setSelectedMw(mw.id)}>
                  <input type="checkbox" checked={isActive} onChange={() => toggleMw(mw.id)} onClick={(e) => e.stopPropagation()} className="accent-blue-500" />
                  <span className={`text-[11px] ${isActive ? 'text-foreground font-medium' : 'text-muted-foreground'}`}>{mw.label}</span>
                </div>
              )
            })}
          </div>

          {/* Right: config fields */}
          <div className="flex-1 overflow-y-auto p-4">
            {selectedDef ? (
              <div className="space-y-3">
                <h3 className="text-xs font-semibold text-foreground">{selectedDef.label}</h3>
                {!active.has(selectedDef.id) && (
                  <p className="text-[10px] text-muted-foreground italic">{t('middlewareConfig.enableFirst')}</p>
                )}
                {active.has(selectedDef.id) && selectedDef.fields.map((field) => {
                  if (!isFieldVisible(selectedDef.id, field)) return null
                  const val = localConfig[selectedDef.id]?.[field.key] ?? ''
                  return (
                    <div key={field.key}>
                      <label className="block text-[10px] text-muted-foreground mb-0.5">{resolveLabel(field.label)}</label>

                      {field.type === 'checkboxGroup' ? (
                        <div className="space-y-1 ml-1">
                          {(field.options as CheckboxGroupOption[])?.map((opt) => {
                            const selected = val.split(',').map(s => s.trim()).includes(opt.value)
                            return (
                              <label key={opt.value} className="flex items-center gap-1.5 cursor-pointer">
                                <input type="checkbox" checked={selected}
                                  onChange={() => toggleGroupValue(selectedDef.id, field.key, opt.value)}
                                  className="accent-blue-500" />
                                <span className="text-[10px] text-foreground">{resolveLabel(opt.label)}</span>
                              </label>
                            )
                          })}
                        </div>
                      ) : field.type === 'textarea' ? (
                        <textarea className="field-textarea text-[10px]" rows={3} value={val} placeholder={field.placeholder}
                          onChange={(e) => setField(selectedDef.id, field.key, e.target.value)} />
                      ) : field.type === 'select' ? (
                        <select className="field-input text-[10px]" value={val || (field.options as string[])?.[0]}
                          onChange={(e) => setField(selectedDef.id, field.key, e.target.value)}>
                          {(field.options as string[])?.map((o) => <option key={o} value={o}>{o}</option>)}
                        </select>
                      ) : field.type === 'checkbox' ? (
                        <label className="flex items-center gap-1.5 cursor-pointer">
                          <input type="checkbox" checked={val === 'true'} onChange={(e) => setField(selectedDef.id, field.key, String(e.target.checked))} className="accent-blue-500" />
                          <span className="text-[10px] text-foreground">{resolveLabel(field.label)}</span>
                        </label>
                      ) : (
                        <input type="number" className="field-input text-[10px]" value={val} placeholder={field.placeholder}
                          onChange={(e) => setField(selectedDef.id, field.key, e.target.value)} />
                      )}
                      {field.help && <p className="text-[8px] text-muted-foreground mt-0.5">{resolveLabel(field.help)}</p>}
                    </div>
                  )
                })}
              </div>
            ) : (
              <p className="text-xs text-muted-foreground text-center py-8">{t('middlewareConfig.selectOne')}</p>
            )}
          </div>
        </div>

        <div className="flex justify-end gap-2 border-t border-border px-4 py-2.5 shrink-0">
          <button onClick={onClose} className="rounded-md border border-border bg-secondary px-3 py-1.5 text-xs text-muted-foreground cursor-pointer">{t('cancel', { ns: 'common' })}</button>
          <button onClick={handleApply} className="rounded-md bg-blue-600 px-4 py-1.5 text-xs font-semibold text-white hover:bg-blue-500 cursor-pointer">{t('toolPicker.apply')}</button>
        </div>
      </div>
    </div>
  )
}
