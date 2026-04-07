import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { Plus, X as XIcon } from 'lucide-react'
import { Field } from '../PropertiesPanel'
import { ExpandableTextarea } from '@/components/shared/ExpandableTextarea'
import { api } from '@/lib/api'
import { notify } from '@/lib/notify'
import { EMBEDDING_MODELS, type NodeData, type RouterNodeData, type IterationNodeData, type ParallelNodeData, type RagNodeData, type HttpRequestNodeData } from '@/types/workflow'

type SimpleData = RouterNodeData | IterationNodeData | ParallelNodeData | RagNodeData | HttpRequestNodeData

interface Props {
  data: SimpleData
  onUpdate: (partial: Partial<NodeData>) => void
}

export function SimpleForm({ data, onUpdate }: Props) {
  switch (data.type) {
    case 'router': return <RouterForm data={data} onUpdate={onUpdate} />
    case 'iteration': return <IterationForm data={data} onUpdate={onUpdate} />
    case 'parallel': return <ParallelForm data={data} onUpdate={onUpdate} />
    case 'rag': return <RagForm data={data} onUpdate={onUpdate} />
    case 'http-request': return <HttpForm data={data} onUpdate={onUpdate} />
    default: return null
  }
}

// ─── Router ───
function RouterForm({ data, onUpdate }: { data: RouterNodeData; onUpdate: (p: Partial<NodeData>) => void }) {
  const { t } = useTranslation('studio')
  const routes = (data.routes ?? '').split(',').map((s) => s.trim()).filter(Boolean)
  const [newRoute, setNewRoute] = useState('')
  const addRoute = () => { if (newRoute.trim()) { onUpdate({ routes: [...routes, newRoute.trim()].join(',') }); setNewRoute('') } }
  const removeRoute = (i: number) => onUpdate({ routes: routes.filter((_, j) => j !== i).join(',') })
  return (
    <>
      <Field label={t('form.routes')}>
        <div className="space-y-1 mb-1.5">
          {routes.map((r, i) => (
            <div key={`${r}-${i}`} className="flex items-center gap-1.5">
              <span className={`text-[9px] w-5 text-center ${i === routes.length - 1 ? 'text-muted-foreground' : 'text-blue-400'}`}>{i + 1}</span>
              <span className="flex-1 text-[10px] text-foreground">{r}</span>
              {i === routes.length - 1 && <span className="text-[8px] text-muted-foreground">(default)</span>}
              <button onClick={() => removeRoute(i)} className="text-muted-foreground hover:text-red-400 cursor-pointer"><XIcon size={11} /></button>
            </div>
          ))}
        </div>
        <div className="flex gap-1.5">
          <input className="field-input flex-1 text-[10px]" value={newRoute} onChange={(e) => setNewRoute(e.target.value)}
            placeholder="New route name" onKeyDown={(e) => { if (e.key === 'Enter') addRoute() }} />
          <button onClick={addRoute} className="rounded-md border border-border bg-secondary px-2 py-1 text-muted-foreground hover:text-foreground cursor-pointer"><Plus size={12} /></button>
        </div>
      </Field>
      <Field label={t('form.classificationPrompt')}>
        <ExpandableTextarea
          value={data.conditionExpression}
          onChange={(v) => onUpdate({ conditionExpression: v })}
          rows={3}
          placeholder="Classify the input into one of the routes..."
          label="Router — Classification Prompt"
        />
      </Field>
    </>
  )
}

// ─── Iteration ───
function IterationForm({ data, onUpdate }: { data: IterationNodeData; onUpdate: (p: Partial<NodeData>) => void }) {
  const { t } = useTranslation('studio')
  return (
    <>
      <Field label={t('form.splitMode')}>
        <select className="field-input" value={data.splitMode} onChange={(e) => onUpdate({ splitMode: e.target.value })}>
          <option value="json-array">JSON Array</option>
          <option value="delimiter">Delimiter</option>
        </select>
      </Field>
      {data.splitMode === 'delimiter' && (
        <Field label={t('form.delimiter')}>
          <input className="field-input font-mono text-[10px]" value={data.iterationDelimiter} onChange={(e) => onUpdate({ iterationDelimiter: e.target.value })} placeholder="\n" />
        </Field>
      )}
      <Field label={t('form.maxItems')}>
        <input type="number" className="field-input" value={data.maxItems} onChange={(e) => onUpdate({ maxItems: Number(e.target.value) })} min={1} max={200} />
        <p className="text-[8px] text-muted-foreground mt-0.5">Safety limit to prevent runaway loops</p>
      </Field>
      <Field label={t('form.maxConcurrency', 'Concurrency')}>
        <input type="number" className="field-input" value={data.maxConcurrency ?? 1} onChange={(e) => onUpdate({ maxConcurrency: Number(e.target.value) })} min={1} max={10} />
        <p className="text-[8px] text-muted-foreground mt-0.5">1 = sequential, &gt;1 = parallel (risk of 429 rate limit)</p>
      </Field>
    </>
  )
}

// ─── Parallel ───
function ParallelForm({ data, onUpdate }: { data: ParallelNodeData; onUpdate: (p: Partial<NodeData>) => void }) {
  const { t } = useTranslation('studio')
  const branchStr = Array.isArray(data.branches)
    ? (data.branches as any[]).map((b: any) => typeof b === 'string' ? b : b.name).join(',')
    : data.branches ?? ''
  const branches = branchStr.split(',').map((s) => s.trim()).filter(Boolean)
  const [newBranch, setNewBranch] = useState('')
  const addBranch = () => { if (newBranch.trim()) { onUpdate({ branches: [...branches, newBranch.trim()].join(',') }); setNewBranch('') } }
  const removeBranch = (i: number) => onUpdate({ branches: branches.filter((_, j) => j !== i).join(',') })
  return (
    <>
      <Field label={t('form.branches')}>
        <div className="space-y-1 mb-1.5">
          {branches.map((b, i) => (
            <div key={`${b}-${i}`} className="flex items-center gap-1.5">
              <span className="text-[9px] w-5 text-center text-green-400">{i + 1}</span>
              <span className="flex-1 text-[10px] text-foreground">{b}</span>
              <button onClick={() => removeBranch(i)} className="text-muted-foreground hover:text-red-400 cursor-pointer"><XIcon size={11} /></button>
            </div>
          ))}
        </div>
        <div className="flex gap-1.5">
          <input className="field-input flex-1 text-[10px]" value={newBranch} onChange={(e) => setNewBranch(e.target.value)}
            placeholder="New branch name" onKeyDown={(e) => { if (e.key === 'Enter') addBranch() }} />
          <button onClick={addBranch} className="rounded-md border border-border bg-secondary px-2 py-1 text-muted-foreground hover:text-foreground cursor-pointer"><Plus size={12} /></button>
        </div>
      </Field>
      <Field label={t('form.mergeStrategy')}>
        <select className="field-input" value={data.mergeStrategy} onChange={(e) => onUpdate({ mergeStrategy: e.target.value })}>
          <option value="labeled">Labeled (branch name: result)</option>
          <option value="join">Join (concatenate)</option>
          <option value="json">JSON (structured)</option>
        </select>
      </Field>
    </>
  )
}

// ─── Shared ───
function CollapsibleSection({ label, hint, children }: { label: string; hint?: string; children: React.ReactNode }) {
  const [open, setOpen] = useState(false)
  return (
    <div className="mt-1">
      <button
        type="button"
        className="flex items-center gap-1 text-[9px] text-muted-foreground hover:text-foreground cursor-pointer mb-1"
        onClick={() => setOpen(!open)}
      >
        <span style={{ display: 'inline-block', transform: open ? 'rotate(90deg)' : 'none', transition: 'transform 0.15s' }}>▸</span>
        {label}
      </button>
      {open && (
        <div className="pl-2 border-l border-border/50 space-y-1.5">
          {hint && <p className="text-[8px] text-muted-foreground/70 italic mb-1">{hint}</p>}
          {children}
        </div>
      )}
    </div>
  )
}

// ─── RAG ───
const SEARCH_QUALITY_PRESETS = [
  { topK: 3, minScore: 0.01 },   // 0 = 精確
  { topK: 5, minScore: 0.005 },  // 1 = 平衡
  { topK: 10, minScore: 0.001 }, // 2 = 涵蓋
] as const

function RagForm({ data, onUpdate }: { data: RagNodeData; onUpdate: (p: Partial<NodeData>) => void }) {
  const { t } = useTranslation('studio')
  const { t: tn } = useTranslation('notifications')
  const [kbs, setKbs] = useState<{ id: string; name: string; fileCount: number; embeddingModel?: string }[]>([])
  useEffect(() => {
    api.knowledgeBases.list()
      .then(setKbs)
      .catch((err) => { console.error('Failed to load knowledge bases:', err); notify.error(tn('loadFailed.knowledgeBases')) })
  }, [])

  const selectedKb = kbs.find((kb) => kb.id === (data.knowledgeBaseIds ?? [])[0])

  // 判斷目前設定是否對應某個 preset
  const currentQuality = SEARCH_QUALITY_PRESETS.findIndex(
    (p) => p.topK === (data.ragTopK ?? 5) && p.minScore === (data.ragMinScore ?? 0.005)
  )
  const qualityValue = data.ragSearchQuality ?? (currentQuality >= 0 ? currentQuality : 1)
  const qualityLabels = [t('form.precise'), t('form.balanced'), t('form.broad')]
  const isCustom = currentQuality < 0 && data.ragSearchQuality === undefined

  const handleQualityChange = (v: number) => {
    const preset = SEARCH_QUALITY_PRESETS[v]
    onUpdate({
      ragSearchQuality: v,
      ragTopK: preset.topK,
      ragMinScore: preset.minScore,
    })
  }

  return (
    <>
      <Field label={t('form.knowledgeBase')}>
        <select className="field-input" value={(data.knowledgeBaseIds ?? [])[0] ?? ''}
          onChange={(e) => onUpdate({ knowledgeBaseIds: e.target.value ? [e.target.value] : [], ragDataSource: 'knowledge-base' })}>
          <option value="">-- {t('form.selectKb')} --</option>
          {kbs.map((kb) => <option key={kb.id} value={kb.id}>{kb.name} ({kb.fileCount} files)</option>)}
        </select>
      </Field>
      {selectedKb?.embeddingModel && (
        <Field label={t('form.embeddingModel')}>
          <div className="field-input bg-muted/30 text-muted-foreground cursor-not-allowed">
            {selectedKb.embeddingModel}
          </div>
          <p className="text-[8px] text-muted-foreground/70 italic mt-0.5">{t('form.embeddingModelKbHint')}</p>
        </Field>
      )}

      {/* 搜尋品質滑桿 */}
      <Field label={t('form.searchQuality')}>
        <div className="flex items-center gap-2">
          <span className="text-[9px] text-muted-foreground">{t('form.precise')}</span>
          <input type="range" min={0} max={2} step={1}
            value={isCustom ? 1 : qualityValue}
            onChange={(e) => handleQualityChange(Number(e.target.value))}
            className="flex-1" style={{ accentColor: 'var(--primary)' }} />
          <span className="text-[9px] text-muted-foreground">{t('form.broad')}</span>
        </div>
        <div className="text-center text-[9px] text-muted-foreground mt-0.5">
          {isCustom ? t('form.custom') : qualityLabels[qualityValue]}
        </div>
      </Field>

      {/* 搜尋設定 */}
      <CollapsibleSection label={t('form.searchSettings')}>
        <Field label={t('form.topK')}>
          <input type="number" className="field-input" value={data.ragTopK ?? 5} min={1} max={20}
            onChange={(e) => onUpdate({ ragTopK: Number(e.target.value), ragSearchQuality: undefined })} />
        </Field>
        <Field label={t('form.searchMode')}>
          <select className="field-input" value={data.ragSearchMode ?? 'hybrid'}
            onChange={(e) => onUpdate({ ragSearchMode: e.target.value })}>
            <option value="hybrid">Hybrid</option>
            <option value="vector">Vector</option>
            <option value="fulltext">Full Text</option>
          </select>
        </Field>
        <Field label={t('form.minScore')}>
          <input type="number" className="field-input" value={data.ragMinScore ?? 0.005} min={0} max={0.1} step={0.001}
            onChange={(e) => onUpdate({ ragMinScore: Number(e.target.value), ragSearchQuality: undefined })} />
        </Field>
        <Field label={t('form.queryExpansion')}>
          <label className="flex items-center gap-1.5 cursor-pointer">
            <input type="checkbox" checked={data.ragQueryExpansion ?? true}
              onChange={(e) => onUpdate({ ragQueryExpansion: e.target.checked })} />
            <span className="text-[9px] text-muted-foreground">{t('form.queryExpansionHint')}</span>
          </label>
        </Field>
        <Field label={t('form.fileNameFilter')}>
          <input type="text" className="field-input" value={data.ragFileNameFilter ?? ''}
            placeholder={t('form.fileNameFilterPlaceholder')}
            onChange={(e) => onUpdate({ ragFileNameFilter: e.target.value })} />
        </Field>
        <Field label={t('form.contextCompression')}>
          <label className="flex items-center gap-1.5 cursor-pointer">
            <input type="checkbox" checked={data.ragContextCompression ?? false}
              onChange={(e) => onUpdate({ ragContextCompression: e.target.checked })} />
            <span className="text-[9px] text-muted-foreground">{t('form.contextCompressionHint')}</span>
          </label>
        </Field>
        {(data.ragContextCompression ?? false) && (
          <Field label={t('form.tokenBudget')}>
            <input type="number" className="field-input" value={data.ragTokenBudget ?? 1500} min={200} max={8000} step={100}
              onChange={(e) => onUpdate({ ragTokenBudget: Number(e.target.value) })} />
          </Field>
        )}
      </CollapsibleSection>
    </>
  )
}

// ─── HTTP Request ───
const HTTP_METHODS = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE'] as const
const HTTP_CONTENT_TYPES = [
  'application/json',
  'text/plain',
  'text/csv',
  'text/xml',
  'application/x-www-form-urlencoded',
  'multipart/form-data',
] as const

function HttpForm({ data, onUpdate }: { data: HttpRequestNodeData; onUpdate: (p: Partial<NodeData>) => void }) {
  const { t } = useTranslation('studio')
  const isInline = !data.httpApiId
  const hasBody = data.httpMethod === 'POST' || data.httpMethod === 'PUT' || data.httpMethod === 'PATCH'
  return (
    <>
      <Field label={t('form.httpUrl')}>
        <input className="field-input font-mono text-[10px]" value={data.httpUrl ?? ''} onChange={(e) => onUpdate({ httpUrl: e.target.value })} placeholder="https://api.example.com/v1/{param}" />
      </Field>
      <Field label={t('form.httpMethod')}>
        <select className="field-input text-[10px]" value={data.httpMethod ?? 'GET'} onChange={(e) => onUpdate({ httpMethod: e.target.value })}>
          {HTTP_METHODS.map((m) => <option key={m} value={m}>{m}</option>)}
        </select>
      </Field>
      {hasBody && (
        <Field label={t('form.httpContentType')}>
          <select className="field-input text-[10px]" value={data.httpContentType ?? 'application/json'} onChange={(e) => onUpdate({ httpContentType: e.target.value })}>
            {HTTP_CONTENT_TYPES.map((ct) => <option key={ct} value={ct}>{ct}</option>)}
          </select>
        </Field>
      )}
      <Field label={t('form.httpHeaders')}>
        <ExpandableTextarea
          className="font-mono text-[10px]"
          value={data.httpHeaders ?? ''}
          onChange={(v) => onUpdate({ httpHeaders: v })}
          rows={2}
          label={`HTTP — ${t('form.httpHeaders')}`}
          placeholder={'Authorization: Bearer xxx\nContent-Type: application/json'} />
        <p className="text-[8px] text-muted-foreground mt-0.5">{t('form.httpHeadersHint')}</p>
      </Field>
      {hasBody && (
        <Field label={t('form.httpBodyTemplate')}>
          <ExpandableTextarea
            className="font-mono text-[10px]"
            value={data.httpBodyTemplate ?? ''}
            onChange={(v) => onUpdate({ httpBodyTemplate: v })}
            rows={3}
            label={`HTTP — ${t('form.httpBodyTemplate')}`}
            language={data.httpContentType === 'application/json' || data.httpContentType === 'multipart/form-data' ? 'json' : undefined}
            placeholder={data.httpContentType === 'multipart/form-data'
              ? '{"parts":[{"name":"file","filename":"report.csv","contentType":"text/csv","data":"{input}"},{"name":"channel","value":"#reports"}]}'
              : data.httpContentType === 'application/x-www-form-urlencoded'
                ? '{"key": "value"} or key=value&key2=value2'
                : '{"query": "{input}"'} />
        </Field>
      )}
      <Field label={t('form.argsTemplate')}>
        <ExpandableTextarea
          className="font-mono text-[10px]"
          value={data.httpArgsTemplate}
          onChange={(v) => onUpdate({ httpArgsTemplate: v })}
          rows={3}
          label={`HTTP — ${t('form.argsTemplate')}`}
          language="json"
          placeholder={'{"query": "{input}"'} />
        <p className="text-[8px] text-muted-foreground mt-0.5">{t('form.httpArgsHint')}</p>
      </Field>
      <Field label={t('form.httpResponseMaxLength')}>
        <input
          type="number"
          className="field-input text-[10px] w-24"
          value={data.httpResponseMaxLength ?? 2000}
          onChange={(e) => onUpdate({ httpResponseMaxLength: Math.max(0, parseInt(e.target.value) || 0) })}
          min={0}
          max={50000} />
        <p className="text-[8px] text-muted-foreground mt-0.5">{t('form.httpResponseMaxLengthHint')}</p>
      </Field>
      <Field label={t('form.httpTimeoutSeconds')}>
        <input
          type="number"
          className="field-input text-[10px] w-24"
          value={data.httpTimeoutSeconds ?? 15}
          onChange={(e) => onUpdate({ httpTimeoutSeconds: Math.max(0, Math.min(300, parseInt(e.target.value) || 0)) })}
          min={0}
          max={300} />
        <p className="text-[8px] text-muted-foreground mt-0.5">{t('form.httpTimeoutHint')}</p>
      </Field>
      <Field label={t('form.httpAuthMode')}>
        <select className="field-input text-[10px]" value={data.httpAuthMode ?? 'none'} onChange={(e) => onUpdate({ httpAuthMode: e.target.value })}>
          <option value="none">None</option>
          <option value="bearer">Bearer Token</option>
          <option value="basic">Basic Auth</option>
          <option value="apikey-header">API Key (Header)</option>
          <option value="apikey-query">API Key (Query)</option>
        </select>
      </Field>
      {data.httpAuthMode && data.httpAuthMode !== 'none' && (
        <>
          <Field label={data.httpAuthMode === 'basic' ? t('form.httpAuthCredentialBasic') : t('form.httpAuthCredential')}>
            <input
              type="password"
              className="field-input font-mono text-[10px]"
              value={data.httpAuthCredential ?? ''}
              onChange={(e) => onUpdate({ httpAuthCredential: e.target.value })}
              placeholder={data.httpAuthMode === 'basic' ? 'user:password' : 'your-token-or-key'} />
          </Field>
          {(data.httpAuthMode === 'apikey-header' || data.httpAuthMode === 'apikey-query') && (
            <Field label={t('form.httpAuthKeyName')}>
              <input
                className="field-input font-mono text-[10px]"
                value={data.httpAuthKeyName ?? ''}
                onChange={(e) => onUpdate({ httpAuthKeyName: e.target.value })}
                placeholder={data.httpAuthMode === 'apikey-header' ? 'X-Api-Key' : 'api_key'} />
            </Field>
          )}
        </>
      )}
      <Field label={t('form.httpRetryCount')}>
        <input
          type="number"
          className="field-input text-[10px] w-24"
          value={data.httpRetryCount ?? 0}
          onChange={(e) => onUpdate({ httpRetryCount: Math.max(0, Math.min(5, parseInt(e.target.value) || 0)) })}
          min={0}
          max={5} />
        <p className="text-[8px] text-muted-foreground mt-0.5">{t('form.httpRetryHint')}</p>
      </Field>
      <Field label={t('form.httpResponseFormat')}>
        <select className="field-input text-[10px]" value={data.httpResponseFormat ?? 'text'} onChange={(e) => onUpdate({ httpResponseFormat: e.target.value })}>
          <option value="text">Text (raw)</option>
          <option value="json">JSON (pretty print)</option>
          <option value="jsonpath">JSONPath (extract field)</option>
        </select>
      </Field>
      {data.httpResponseFormat === 'jsonpath' && (
        <Field label={t('form.httpResponseJsonPath')}>
          <input
            className="field-input font-mono text-[10px]"
            value={data.httpResponseJsonPath ?? ''}
            onChange={(e) => onUpdate({ httpResponseJsonPath: e.target.value })}
            placeholder="data.items[0].name" />
        </Field>
      )}
      <Field label={t('form.httpApiId')}>
        <input className="field-input font-mono text-[10px]" value={data.httpApiId} onChange={(e) => onUpdate({ httpApiId: e.target.value })} placeholder="my-api" />
        {isInline && <p className="text-[8px] text-muted-foreground mt-0.5">{t('form.httpApiIdHint')}</p>}
      </Field>
    </>
  )
}
