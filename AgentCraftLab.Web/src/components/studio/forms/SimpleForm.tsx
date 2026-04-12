import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { Plus, X as XIcon } from 'lucide-react'
import { Field } from '../PropertiesPanel'
import { ExpandableTextarea } from '@/components/shared/ExpandableTextarea'
import { useVariableSuggestions } from '@/hooks/useVariableSuggestions'
import { api } from '@/lib/api'
import { notify } from '@/lib/notify'
import type {
  BranchConfig,
  CatalogHttpRef,
  HttpAuth,
  HttpHeader,
  HttpMethod,
  HttpRequestNodeData,
  HttpRequestSpec,
  InlineHttpRequest,
  IterationNodeData,
  MergeStrategy,
  NodeData,
  ParallelNodeData,
  RagNodeData,
  RagSearchMode,
  ResponseParser,
  RouteConfig,
  RouterNodeData,
  SplitModeKind,
} from '@/types/workflow'

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
  const routes = data.routes ?? []
  const [newRoute, setNewRoute] = useState('')

  const addRoute = () => {
    if (!newRoute.trim()) return
    const next: RouteConfig[] = [...routes, { name: newRoute.trim(), keywords: [], isDefault: false }]
    onUpdate({ routes: next })
    setNewRoute('')
  }
  const removeRoute = (i: number) => {
    onUpdate({ routes: routes.filter((_, j) => j !== i) })
  }

  return (
    <>
      <Field label={t('form.routes')}>
        <div className="space-y-1 mb-1.5">
          {routes.map((r, i) => (
            <div key={`${r.name}-${i}`} className="flex items-center gap-1.5">
              <span className={`text-[9px] w-5 text-center ${i === routes.length - 1 ? 'text-muted-foreground' : 'text-blue-400'}`}>{i + 1}</span>
              <span className="flex-1 text-[10px] text-foreground">{r.name}</span>
              {(r.isDefault || i === routes.length - 1) && <span className="text-[8px] text-muted-foreground">(default)</span>}
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
    </>
  )
}

// ─── Iteration ───
function IterationForm({ data, onUpdate }: { data: IterationNodeData; onUpdate: (p: Partial<NodeData>) => void }) {
  const { t } = useTranslation('studio')
  return (
    <>
      <Field label={t('form.splitMode')}>
        <select className="field-input" value={data.split} onChange={(e) => onUpdate({ split: e.target.value as SplitModeKind })}>
          <option value="jsonArray">JSON Array</option>
          <option value="delimiter">Delimiter</option>
        </select>
      </Field>
      {data.split === 'delimiter' && (
        <Field label={t('form.delimiter')}>
          <input className="field-input font-mono text-[10px]" value={data.delimiter} onChange={(e) => onUpdate({ delimiter: e.target.value })} placeholder="\n" />
        </Field>
      )}
      <Field label={t('form.maxItems')}>
        <input type="number" className="field-input" value={data.maxItems} onChange={(e) => onUpdate({ maxItems: Number(e.target.value) })} min={1} max={200} />
        <p className="text-[8px] text-muted-foreground mt-0.5">Safety limit to prevent runaway loops</p>
      </Field>
      <Field label={t('form.maxConcurrency', 'Concurrency')}>
        <input type="number" className="field-input" value={data.maxConcurrency} onChange={(e) => onUpdate({ maxConcurrency: Number(e.target.value) })} min={1} max={10} />
        <p className="text-[8px] text-muted-foreground mt-0.5">1 = sequential, &gt;1 = parallel (risk of 429 rate limit)</p>
      </Field>
    </>
  )
}

// ─── Parallel ───
function ParallelForm({ data, onUpdate }: { data: ParallelNodeData; onUpdate: (p: Partial<NodeData>) => void }) {
  const { t } = useTranslation('studio')
  const branches = data.branches ?? []
  const [newBranch, setNewBranch] = useState('')

  const addBranch = () => {
    if (!newBranch.trim()) return
    const next: BranchConfig[] = [...branches, { name: newBranch.trim(), goal: '' }]
    onUpdate({ branches: next })
    setNewBranch('')
  }
  const removeBranch = (i: number) => {
    onUpdate({ branches: branches.filter((_, j) => j !== i) })
  }

  return (
    <>
      <Field label={t('form.branches')}>
        <div className="space-y-1 mb-1.5">
          {branches.map((b, i) => (
            <div key={`${b.name}-${i}`} className="flex items-center gap-1.5">
              <span className="text-[9px] w-5 text-center text-green-400">{i + 1}</span>
              <span className="flex-1 text-[10px] text-foreground">{b.name}</span>
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
        <select className="field-input" value={data.merge} onChange={(e) => onUpdate({ merge: e.target.value as MergeStrategy })}>
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
  // ragSearchQuality 已從 schema 移除，改為純前端 local state
  const [searchQuality, setSearchQuality] = useState<number | undefined>(undefined)

  useEffect(() => {
    api.knowledgeBases.list()
      .then(setKbs)
      .catch((err) => { console.error('Failed to load knowledge bases:', err); notify.error(tn('loadFailed.knowledgeBases')) })
  }, [tn])

  const rag = data.rag
  const selectedKb = kbs.find((kb) => kb.id === (data.knowledgeBaseIds ?? [])[0])

  // 判斷目前設定是否對應某個 preset
  const currentQuality = SEARCH_QUALITY_PRESETS.findIndex(
    (p) => p.topK === (rag?.topK ?? 5) && p.minScore === (rag?.minScore ?? 0.005)
  )
  const qualityValue = searchQuality ?? (currentQuality >= 0 ? currentQuality : 1)
  const qualityLabels = [t('form.precise'), t('form.balanced'), t('form.broad')]
  const isCustom = currentQuality < 0 && searchQuality === undefined

  const handleQualityChange = (v: number) => {
    const preset = SEARCH_QUALITY_PRESETS[v]
    setSearchQuality(v)
    onUpdate({
      rag: {
        ...rag,
        topK: preset.topK,
        minScore: preset.minScore,
      },
    })
  }

  return (
    <>
      <Field label={t('form.knowledgeBase')}>
        <select className="field-input" value={(data.knowledgeBaseIds ?? [])[0] ?? ''}
          onChange={(e) => onUpdate({
            knowledgeBaseIds: e.target.value ? [e.target.value] : [],
            rag: { ...rag, dataSource: 'knowledge-base' },
          })}>
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
          <input type="number" className="field-input" value={rag?.topK ?? 5} min={1} max={20}
            onChange={(e) => { setSearchQuality(undefined); onUpdate({ rag: { ...rag, topK: Number(e.target.value) } }) }} />
        </Field>
        <Field label={t('form.searchMode')}>
          <select className="field-input" value={rag?.searchMode ?? 'hybrid'}
            onChange={(e) => onUpdate({ rag: { ...rag, searchMode: e.target.value as RagSearchMode } })}>
            <option value="hybrid">Hybrid</option>
            <option value="vector">Vector</option>
            <option value="fulltext">Full Text</option>
          </select>
        </Field>
        <Field label={t('form.minScore')}>
          <input type="number" className="field-input" value={rag?.minScore ?? 0.005} min={0} max={0.1} step={0.001}
            onChange={(e) => { setSearchQuality(undefined); onUpdate({ rag: { ...rag, minScore: Number(e.target.value) } }) }} />
        </Field>
        <Field label={t('form.queryExpansion')}>
          <label className="flex items-center gap-1.5 cursor-pointer">
            <input type="checkbox" checked={rag?.queryExpansion ?? true}
              onChange={(e) => onUpdate({ rag: { ...rag, queryExpansion: e.target.checked } })} />
            <span className="text-[9px] text-muted-foreground">{t('form.queryExpansionHint')}</span>
          </label>
        </Field>
        <Field label={t('form.fileNameFilter')}>
          <input type="text" className="field-input" value={rag?.fileNameFilter ?? ''}
            placeholder={t('form.fileNameFilterPlaceholder')}
            onChange={(e) => onUpdate({ rag: { ...rag, fileNameFilter: e.target.value } })} />
        </Field>
        <Field label={t('form.contextCompression')}>
          <label className="flex items-center gap-1.5 cursor-pointer">
            <input type="checkbox" checked={rag?.contextCompression ?? false}
              onChange={(e) => onUpdate({ rag: { ...rag, contextCompression: e.target.checked } })} />
            <span className="text-[9px] text-muted-foreground">{t('form.contextCompressionHint')}</span>
          </label>
        </Field>
        {(rag?.contextCompression ?? false) && (
          <Field label={t('form.tokenBudget')}>
            <input type="number" className="field-input" value={rag?.tokenBudget ?? 1500} min={200} max={8000} step={100}
              onChange={(e) => onUpdate({ rag: { ...rag, tokenBudget: Number(e.target.value) } })} />
          </Field>
        )}
      </CollapsibleSection>
    </>
  )
}

// ─── HTTP Request ───
const HTTP_METHODS: HttpMethod[] = ['get', 'post', 'put', 'patch', 'delete']
const HTTP_CONTENT_TYPES = [
  'application/json',
  'text/plain',
  'text/csv',
  'text/xml',
  'application/x-www-form-urlencoded',
  'multipart/form-data',
] as const

type HttpAuthMode = 'none' | 'bearer' | 'basic' | 'apikey-header' | 'apikey-query'

function parseHeaders(text: string): HttpHeader[] {
  if (!text) return []
  const out: HttpHeader[] = []
  for (const line of text.split(/\r?\n/)) {
    const idx = line.indexOf(':')
    if (idx <= 0) continue
    const name = line.slice(0, idx).trim()
    const value = line.slice(idx + 1).trim()
    if (name) out.push({ name, value })
  }
  return out
}

function stringifyHeaders(headers: HttpHeader[] | undefined): string {
  if (!headers || headers.length === 0) return ''
  return headers.map((h) => `${h.name}: ${h.value}`).join('\n')
}

function getAuthMode(auth: HttpAuth | undefined): HttpAuthMode {
  if (!auth) return 'none'
  return auth.kind as HttpAuthMode
}

function getAuthCredential(auth: HttpAuth | undefined): string {
  if (!auth) return ''
  switch (auth.kind) {
    case 'bearer': return auth.token
    case 'basic': return auth.userPass
    case 'apikey-header':
    case 'apikey-query': return auth.value
    default: return ''
  }
}

function getAuthKeyName(auth: HttpAuth | undefined): string {
  if (!auth) return ''
  if (auth.kind === 'apikey-header' || auth.kind === 'apikey-query') return auth.keyName
  return ''
}

function buildAuth(mode: HttpAuthMode, credential: string, keyName: string): HttpAuth {
  switch (mode) {
    case 'bearer': return { kind: 'bearer', token: credential }
    case 'basic': return { kind: 'basic', userPass: credential }
    case 'apikey-header': return { kind: 'apikey-header', keyName, value: credential }
    case 'apikey-query': return { kind: 'apikey-query', keyName, value: credential }
    default: return { kind: 'none' }
  }
}

function getResponseFormat(parser: ResponseParser | undefined): 'text' | 'json' | 'jsonPath' {
  return parser?.kind ?? 'text'
}

function getResponseJsonPath(parser: ResponseParser | undefined): string {
  if (parser?.kind === 'jsonPath') return parser.path
  return ''
}

function buildResponseParser(format: 'text' | 'json' | 'jsonPath', path: string): ResponseParser {
  if (format === 'json') return { kind: 'json' }
  if (format === 'jsonPath') return { kind: 'jsonPath', path }
  return { kind: 'text' }
}

const DEFAULT_INLINE: InlineHttpRequest = {
  kind: 'inline',
  url: '',
  method: 'get',
  headers: [],
  contentType: 'application/json',
  auth: { kind: 'none' },
  retry: { count: 0, delayMs: 0 },
  timeoutSeconds: 15,
  response: { kind: 'text' },
  responseMaxLength: 2000,
}

function HttpForm({ data, onUpdate }: { data: HttpRequestNodeData; onUpdate: (p: Partial<NodeData>) => void }) {
  const { t } = useTranslation('studio')
  const suggestions = useVariableSuggestions()

  // Ensure we have a spec — default to inline if undefined
  const spec: HttpRequestSpec = data.spec ?? DEFAULT_INLINE
  const isCatalog = spec.kind === 'catalog'
  const inline = isCatalog ? DEFAULT_INLINE : (spec as InlineHttpRequest)
  const catalog = isCatalog ? (spec as CatalogHttpRef) : null

  const updateInline = (patch: Partial<InlineHttpRequest>) => {
    onUpdate({ spec: { ...inline, ...patch } })
  }

  const updateCatalog = (patch: Partial<CatalogHttpRef>) => {
    const base: CatalogHttpRef = catalog ?? { kind: 'catalog', apiId: '', args: {} }
    onUpdate({ spec: { ...base, ...patch } })
  }

  const switchToCatalog = (apiId: string) => {
    onUpdate({ spec: { kind: 'catalog', apiId, args: {} } })
  }

  const switchToInline = () => {
    onUpdate({ spec: DEFAULT_INLINE })
  }

  const hasBody = !isCatalog && (inline.method === 'post' || inline.method === 'put' || inline.method === 'patch')
  const authMode = getAuthMode(inline.auth)
  const authCredential = getAuthCredential(inline.auth)
  const authKeyName = getAuthKeyName(inline.auth)
  const responseFormat = getResponseFormat(inline.response)
  const responseJsonPath = getResponseJsonPath(inline.response)
  const headersText = stringifyHeaders(inline.headers)
  const bodyContent = typeof inline.body?.content === 'string' ? inline.body.content : (inline.body?.content !== undefined ? JSON.stringify(inline.body.content) : '')
  const argsText = catalog?.args !== undefined
    ? (typeof catalog.args === 'string' ? catalog.args : JSON.stringify(catalog.args, null, 2))
    : ''

  return (
    <>
      {/* Mode toggle: inline vs catalog */}
      <Field label={t('form.httpApiId')}>
        <input
          className="field-input font-mono text-[10px]"
          value={catalog?.apiId ?? ''}
          onChange={(e) => {
            if (e.target.value) switchToCatalog(e.target.value)
            else if (isCatalog) switchToInline()
          }}
          placeholder="my-api (leave empty for inline mode)"
        />
        {!isCatalog && <p className="text-[8px] text-muted-foreground mt-0.5">{t('form.httpApiIdHint')}</p>}
      </Field>

      {isCatalog ? (
        <Field label={t('form.argsTemplate')}>
          <ExpandableTextarea
            className="font-mono text-[10px]"
            value={argsText}
            onChange={(v) => {
              try {
                updateCatalog({ args: v ? JSON.parse(v) : {} })
              } catch {
                // Keep raw string while user is typing — backend tolerates string args
                updateCatalog({ args: v })
              }
            }}
            rows={3}
            label={`HTTP — ${t('form.argsTemplate')}`}
            language="json"
            placeholder={'{"query": "{input}"}'} />
          <p className="text-[8px] text-muted-foreground mt-0.5">{t('form.httpArgsHint')}</p>
        </Field>
      ) : (
        <>
          <Field label={t('form.httpUrl')}>
            <input className="field-input font-mono text-[10px]" value={inline.url} onChange={(e) => updateInline({ url: e.target.value })} placeholder="https://api.example.com/v1/{param}" />
          </Field>
          <Field label={t('form.httpMethod')}>
            <select className="field-input text-[10px]" value={inline.method} onChange={(e) => updateInline({ method: e.target.value as HttpMethod })}>
              {HTTP_METHODS.map((m) => <option key={m} value={m}>{m.toUpperCase()}</option>)}
            </select>
          </Field>
          {hasBody && (
            <Field label={t('form.httpContentType')}>
              <select className="field-input text-[10px]" value={inline.contentType} onChange={(e) => updateInline({ contentType: e.target.value })}>
                {HTTP_CONTENT_TYPES.map((ct) => <option key={ct} value={ct}>{ct}</option>)}
              </select>
            </Field>
          )}
          <Field label={t('form.httpHeaders')}>
            <ExpandableTextarea
              className="font-mono text-[10px]"
              value={headersText}
              onChange={(v) => updateInline({ headers: parseHeaders(v) })}
              rows={2}
              label={`HTTP — ${t('form.httpHeaders')}`}
              placeholder={'Authorization: Bearer xxx\nContent-Type: application/json'}
              suggestions={suggestions} />
            <p className="text-[8px] text-muted-foreground mt-0.5">{t('form.httpHeadersHint')}</p>
          </Field>
          {hasBody && (
            <Field label={t('form.httpBodyTemplate')}>
              <ExpandableTextarea
                className="font-mono text-[10px]"
                value={bodyContent}
                onChange={(v) => updateInline({ body: v ? { content: v } : undefined })}
                rows={3}
                label={`HTTP — ${t('form.httpBodyTemplate')}`}
                suggestions={suggestions}
                language={inline.contentType === 'application/json' || inline.contentType === 'multipart/form-data' ? 'json' : undefined}
                placeholder={inline.contentType === 'multipart/form-data'
                  ? '{"parts":[{"name":"file","filename":"report.csv","contentType":"text/csv","data":"{input}"},{"name":"channel","value":"#reports"}]}'
                  : inline.contentType === 'application/x-www-form-urlencoded'
                    ? '{"key": "value"} or key=value&key2=value2'
                    : '{"query": "{input}"}'} />
            </Field>
          )}
          <Field label={t('form.httpResponseMaxLength')}>
            <input
              type="number"
              className="field-input text-[10px] w-24"
              value={inline.responseMaxLength}
              onChange={(e) => updateInline({ responseMaxLength: Math.max(0, parseInt(e.target.value) || 0) })}
              min={0}
              max={50000} />
            <p className="text-[8px] text-muted-foreground mt-0.5">{t('form.httpResponseMaxLengthHint')}</p>
          </Field>
          <Field label={t('form.httpTimeoutSeconds')}>
            <input
              type="number"
              className="field-input text-[10px] w-24"
              value={inline.timeoutSeconds}
              onChange={(e) => updateInline({ timeoutSeconds: Math.max(0, Math.min(300, parseInt(e.target.value) || 0)) })}
              min={0}
              max={300} />
            <p className="text-[8px] text-muted-foreground mt-0.5">{t('form.httpTimeoutHint')}</p>
          </Field>
          <Field label={t('form.httpAuthMode')}>
            <select className="field-input text-[10px]" value={authMode}
              onChange={(e) => updateInline({ auth: buildAuth(e.target.value as HttpAuthMode, authCredential, authKeyName) })}>
              <option value="none">None</option>
              <option value="bearer">Bearer Token</option>
              <option value="basic">Basic Auth</option>
              <option value="apikey-header">API Key (Header)</option>
              <option value="apikey-query">API Key (Query)</option>
            </select>
          </Field>
          {authMode !== 'none' && (
            <>
              <Field label={authMode === 'basic' ? t('form.httpAuthCredentialBasic') : t('form.httpAuthCredential')}>
                <input
                  type="password"
                  className="field-input font-mono text-[10px]"
                  value={authCredential}
                  onChange={(e) => updateInline({ auth: buildAuth(authMode, e.target.value, authKeyName) })}
                  placeholder={authMode === 'basic' ? 'user:password' : 'your-token-or-key'} />
              </Field>
              {(authMode === 'apikey-header' || authMode === 'apikey-query') && (
                <Field label={t('form.httpAuthKeyName')}>
                  <input
                    className="field-input font-mono text-[10px]"
                    value={authKeyName}
                    onChange={(e) => updateInline({ auth: buildAuth(authMode, authCredential, e.target.value) })}
                    placeholder={authMode === 'apikey-header' ? 'X-Api-Key' : 'api_key'} />
                </Field>
              )}
            </>
          )}
          <Field label={t('form.httpRetryCount')}>
            <input
              type="number"
              className="field-input text-[10px] w-24"
              value={inline.retry.count}
              onChange={(e) => updateInline({ retry: { ...inline.retry, count: Math.max(0, Math.min(5, parseInt(e.target.value) || 0)) } })}
              min={0}
              max={5} />
            <p className="text-[8px] text-muted-foreground mt-0.5">{t('form.httpRetryHint')}</p>
          </Field>
          <Field label={t('form.httpResponseFormat')}>
            <select className="field-input text-[10px]" value={responseFormat}
              onChange={(e) => updateInline({ response: buildResponseParser(e.target.value as 'text' | 'json' | 'jsonPath', responseJsonPath) })}>
              <option value="text">Text (raw)</option>
              <option value="json">JSON (pretty print)</option>
              <option value="jsonPath">JSONPath (extract field)</option>
            </select>
          </Field>
          {responseFormat === 'jsonPath' && (
            <Field label={t('form.httpResponseJsonPath')}>
              <input
                className="field-input font-mono text-[10px]"
                value={responseJsonPath}
                onChange={(e) => updateInline({ response: { kind: 'jsonPath', path: e.target.value } })}
                placeholder="data.items[0].name" />
            </Field>
          )}
        </>
      )}
    </>
  )
}
