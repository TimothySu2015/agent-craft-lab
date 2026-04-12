import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Wrench, Zap, ChevronDown, ChevronRight, RotateCcw, Settings } from 'lucide-react'
import { Field } from '../PropertiesPanel'
import { PROVIDERS, getModelsForProvider } from '@/lib/providers'
import { useCredentialStore } from '@/stores/credential-store'
import { useDefaultCredential } from '@/hooks/useDefaultCredential'
import { ToolPickerDialog } from './ToolPickerDialog'
import { SkillPickerDialog } from './SkillPickerDialog'
import { MiddlewareConfigDialog } from './MiddlewareConfigDialog'
import { ExpandableTextarea } from '@/components/shared/ExpandableTextarea'
import { useVariableSuggestions } from '@/hooks/useVariableSuggestions'
import type { PromptRefinerResult } from '@/components/shared/PromptRefinerDialog'
import type {
  AgentNodeData,
  HistoryProvider,
  MiddlewareBinding,
  NodeData,
  OutputFormat,
} from '@/types/workflow'

interface Props {
  data: AgentNodeData
  onUpdate: (partial: Partial<NodeData>) => void
}

export function AgentForm({ data, onUpdate }: Props) {
  const { t } = useTranslation('studio')
  const suggestions = useVariableSuggestions()
  const provider = data.model?.provider ?? 'openai'
  const providerModels = getModelsForProvider(provider)
  const credentials = useCredentialStore((s) => s.credentials)
  const hasKey = (id: string) => !!credentials[id]?.apiKey || !!credentials[id]?.saved
  const currentHasKey = hasKey(provider)
  const [showToolPicker, setShowToolPicker] = useState(false)
  const [showSkillPicker, setShowSkillPicker] = useState(false)
  const [showAdvanced, setShowAdvanced] = useState(false)
  const [showMiddlewareConfig, setShowMiddlewareConfig] = useState(false)
  const getDefaultCred = useDefaultCredential()

  const handleOptimize = async (text: string): Promise<PromptRefinerResult | null> => {
    const cred = getDefaultCred()
    if (!cred) return null
    try {
      const res = await fetch('/api/prompt-refiner', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          prompt: text,
          provider: data.model.provider,
          model: data.model.model,
          apiKey: cred.apiKey,
          endpoint: cred.endpoint,
        }),
      })
      if (res.ok) return await res.json()
    } catch { /* handled by caller */ }
    return null
  }

  // Middleware: keep dialog API as comma-joined string + config dict, convert at the boundary
  const middlewareList = data.middleware ?? []
  const middlewareString = middlewareList.map((b) => b.key).join(',')
  const middlewareConfig: Record<string, Record<string, string>> = {}
  for (const b of middlewareList) middlewareConfig[b.key] = b.options ?? {}
  const activeMw = middlewareList.map((b) => b.key)

  const updateMiddleware = (mwString: string, cfg: Record<string, Record<string, string>>) => {
    const keys = mwString.split(',').map((s) => s.trim()).filter(Boolean)
    const next: MiddlewareBinding[] = keys.map((k) => ({ key: k, options: cfg[k] ?? {} }))
    onUpdate({ middleware: next })
  }

  return (
    <>
      {/* ─── Provider & Model ─── */}
      <Field label={t('form.provider')}>
        <select className="field-input" value={data.model.provider}
          onChange={(e) => onUpdate({
            model: {
              ...data.model,
              provider: e.target.value,
              model: getModelsForProvider(e.target.value)[0] ?? 'gpt-4o-mini',
            },
          })}>
          {PROVIDERS.map((p) => (
            <option key={p.id} value={p.id}>{hasKey(p.id) ? '\u25CF' : '\u25CB'} {p.name}</option>
          ))}
        </select>
        {!currentHasKey && <p className="text-[9px] text-yellow-400 mt-0.5">{t('credentials.noKeyWarning')}</p>}
      </Field>

      <Field label={t('form.model')}>
        <select className="field-input" value={data.model.model}
          onChange={(e) => onUpdate({ model: { ...data.model, model: e.target.value } })}>
          {providerModels.map((m) => <option key={m} value={m}>{m}</option>)}
        </select>
      </Field>

      {/* ─── Instructions ─── */}
      <Field label={t('form.instructions')}>
        <ExpandableTextarea
          value={data.instructions}
          onChange={(v) => onUpdate({ instructions: v })}
          rows={3}
          placeholder="Describe what this agent should do..."
          label={`${data.name || 'Agent'} — Instructions`}
          language="markdown"
          onOptimize={handleOptimize}
          suggestions={suggestions}
        />
      </Field>

      {/* ─── Output Format ─── */}
      <Field label={t('form.outputFormat')}>
        <select className="field-input" value={data.output?.kind ?? 'text'}
          onChange={(e) => onUpdate({ output: { ...data.output, kind: e.target.value as OutputFormat } })}>
          <option value="text">Text (default)</option>
          <option value="json">JSON</option>
          <option value="jsonSchema">JSON Schema</option>
        </select>
      </Field>

      {data.output?.kind === 'jsonSchema' && (
        <Field label={t('form.jsonSchema')}>
          <ExpandableTextarea
            className="font-mono text-[9px]"
            value={data.output?.schemaJson ?? ''}
            onChange={(v) => onUpdate({ output: { ...data.output, schemaJson: v } })}
            rows={4}
            placeholder='{"type":"object","properties":{"title":{"type":"string"}}}'
            label="JSON Schema"
            language="json"
          />
        </Field>
      )}

      {/* ─── Tools & Skills ─── */}
      <Field label={t('form.tools')}>
        <div className="flex items-center gap-2">
          <button onClick={() => setShowToolPicker(true)}
            className="flex items-center gap-1 rounded-md border border-border bg-secondary px-2.5 py-1 text-[10px] text-muted-foreground hover:text-foreground hover:bg-accent transition-colors cursor-pointer">
            <Wrench size={11} /> {t('toolPicker.manage')}
          </button>
          {data.tools?.length > 0 && <span className="text-[10px] text-blue-400">{data.tools?.length} {t('toolPicker.selected')}</span>}
        </div>
        {data.tools?.length > 0 && (
          <div className="flex flex-wrap gap-1 mt-1.5">
            {data.tools?.map((id) => <span key={id} className="rounded bg-blue-500/10 border border-blue-500/20 px-1.5 py-0.5 text-[9px] text-blue-400 font-mono">{id}</span>)}
          </div>
        )}
        <ToolPickerDialog open={showToolPicker} selected={data.tools ?? []} onClose={() => setShowToolPicker(false)} onApply={(tools) => onUpdate({ tools })} />
      </Field>

      <Field label={t('form.skills')}>
        <div className="flex items-center gap-2">
          <button onClick={() => setShowSkillPicker(true)}
            className="flex items-center gap-1 rounded-md border border-border bg-secondary px-2.5 py-1 text-[10px] text-muted-foreground hover:text-foreground hover:bg-accent transition-colors cursor-pointer">
            <Zap size={11} /> {t('skillPicker.manage')}
          </button>
          {(data.skills?.length ?? 0) > 0 && <span className="text-[10px] text-violet-400">{data.skills?.length} {t('skillPicker.selected')}</span>}
        </div>
        {(data.skills?.length ?? 0) > 0 && (
          <div className="flex flex-wrap gap-1 mt-1.5">
            {data.skills?.map((id) => <span key={id} className="rounded bg-violet-500/10 border border-violet-500/20 px-1.5 py-0.5 text-[9px] text-violet-400 font-mono">{id}</span>)}
          </div>
        )}
        <SkillPickerDialog open={showSkillPicker} selected={data.skills ?? []} onClose={() => setShowSkillPicker(false)} onApply={(skills) => onUpdate({ skills })} />
      </Field>

      {/* ─── Advanced Parameters (collapsible) ─── */}
      <button onClick={() => setShowAdvanced(!showAdvanced)}
        className="flex items-center gap-1 w-full py-1 text-[10px] text-muted-foreground hover:text-foreground cursor-pointer">
        {showAdvanced ? <ChevronDown size={12} /> : <ChevronRight size={12} />}
        {t('form.advanced')}
      </button>

      {showAdvanced && (
        <>
          <Field label={t('form.temperature')}>
            <SliderWithReset value={data.model.temperature ?? 0.7} min={0} max={2} step={0.1} defaultVal={0.7}
              onChange={(v) => onUpdate({ model: { ...data.model, temperature: v } })} />
          </Field>

          <Field label={t('form.topP')}>
            <SliderWithReset value={data.model.topP ?? 1} min={0} max={1} step={0.05} defaultVal={1}
              onChange={(v) => onUpdate({ model: { ...data.model, topP: v } })} />
          </Field>

          <Field label={t('form.maxOutputTokens')}>
            <div className="flex items-center gap-2">
              <input type="number" className="field-input flex-1" value={data.model.maxOutputTokens ?? ''} placeholder="(default)"
                onChange={(e) => onUpdate({ model: { ...data.model, maxOutputTokens: e.target.value ? Number(e.target.value) : undefined } })} />
              <button onClick={() => onUpdate({ model: { ...data.model, maxOutputTokens: undefined } })} className="text-muted-foreground hover:text-foreground cursor-pointer" title="Reset">
                <RotateCcw size={12} />
              </button>
            </div>
          </Field>

          <Field label={t('form.chatHistory')}>
            <select className="field-input" value={data.history?.provider ?? 'none'}
              onChange={(e) => onUpdate({ history: { ...data.history, provider: e.target.value as HistoryProvider } })}>
              <option value="none">None</option>
              <option value="inMemory">In-Memory (sliding window)</option>
              <option value="database">Database</option>
            </select>
          </Field>

          {data.history?.provider === 'inMemory' && (
            <Field label={t('form.maxMessages')}>
              <input type="number" className="field-input" min={1} max={200} value={data.history?.maxMessages ?? 20}
                onChange={(e) => onUpdate({ history: { ...data.history, maxMessages: Number(e.target.value) } })} />
              <p className="text-[8px] text-muted-foreground mt-0.5">Sliding window size for conversation history</p>
            </Field>
          )}

          <Field label={t('form.middleware')}>
            <div className="flex items-center gap-2">
              <button onClick={() => setShowMiddlewareConfig(true)}
                className="flex items-center gap-1 rounded-md border border-border bg-secondary px-2.5 py-1 text-[10px] text-muted-foreground hover:text-foreground hover:bg-accent transition-colors cursor-pointer">
                <Settings size={11} /> {t('form.configure')}
              </button>
              {activeMw.length > 0 && <span className="text-[10px] text-green-400">{activeMw.length} {t('form.active')}</span>}
            </div>
            {activeMw.length > 0 && (
              <div className="flex flex-wrap gap-1 mt-1.5">
                {activeMw.map((id) => (
                  <span key={id} className="rounded bg-green-500/10 border border-green-500/20 px-1.5 py-0.5 text-[9px] text-green-400">{id}</span>
                ))}
              </div>
            )}
            <MiddlewareConfigDialog
              open={showMiddlewareConfig}
              middleware={middlewareString}
              config={middlewareConfig}
              onClose={() => setShowMiddlewareConfig(false)}
              onApply={(mw, cfg) => updateMiddleware(mw, cfg)}
            />
          </Field>
        </>
      )}

    </>
  )
}

// ─── Shared Components ───

function SliderWithReset({ value, min, max, step, defaultVal, onChange }: {
  value: number; min: number; max: number; step: number; defaultVal: number; onChange: (v: number) => void
}) {
  return (
    <div className="flex items-center gap-2">
      <input type="range" min={min} max={max} step={step} value={value}
        onChange={(e) => onChange(Number(e.target.value))} className="flex-1" style={{ accentColor: 'var(--primary)' }} />
      <span className="text-[10px] text-muted-foreground w-8 text-right">{value.toFixed(step < 1 ? (step < 0.1 ? 2 : 1) : 0)}</span>
      <button onClick={() => onChange(defaultVal)} className="text-muted-foreground hover:text-foreground cursor-pointer" title="Reset">
        <RotateCcw size={11} />
      </button>
    </div>
  )
}
