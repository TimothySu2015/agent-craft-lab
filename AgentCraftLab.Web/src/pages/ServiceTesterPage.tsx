/**
 * ServiceTesterPage — 服務測試：支援 Published Workflow + 外部端點，
 * 5 種協定（Google A2A / Microsoft A2A / MCP / API / Teams），Chat 式多輪對話。
 */
import { useState, useEffect, useCallback, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { FlaskConical, Play, Copy, Check, Plus, Trash2, Globe, Bot, RotateCcw } from 'lucide-react'
import { api, type WorkflowDocument } from '@/lib/api'

type Protocol = 'auto' | 'google' | 'microsoft' | 'mcp' | 'api' | 'teams'

interface ExternalEndpoint {
  name: string
  url: string
  format: Protocol
}

interface ChatMsg {
  isUser: boolean
  text: string
  elapsedMs?: number
  status?: string
}

interface AgentCard {
  name: string
  description?: string
  version?: string
}

const PROTOCOL_KEYS: { value: Protocol; i18nKey: string }[] = [
  { value: 'auto', i18nKey: 'tester.autoDetect' },
  { value: 'google', i18nKey: 'tester.googleA2A' },
  { value: 'microsoft', i18nKey: 'tester.microsoftA2A' },
  { value: 'mcp', i18nKey: 'tester.mcp' },
  { value: 'api', i18nKey: 'tester.api' },
  { value: 'teams', i18nKey: 'tester.teams' },
]

function extractName(url: string): string {
  try {
    const segments = new URL(url).pathname.split('/').filter(Boolean)
    return segments.length > 0 ? segments[segments.length - 1] : new URL(url).host
  } catch {
    return url
  }
}

export function ServiceTesterPage() {
  const { t } = useTranslation()

  // endpoint list
  const [publishedWorkflows, setPublishedWorkflows] = useState<WorkflowDocument[]>([])
  const [externals, setExternals] = useState<ExternalEndpoint[]>([])
  const [customUrl, setCustomUrl] = useState('')
  const [customFormat, setCustomFormat] = useState<Protocol>('auto')

  // selection
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [selectedName, setSelectedName] = useState('')
  const [selectedUrl, setSelectedUrl] = useState('')
  const [isLocal, setIsLocal] = useState(false)
  const [workflowKey, setWorkflowKey] = useState('')
  const [testFormat, setTestFormat] = useState<Protocol>('google')
  const [agentCard, setAgentCard] = useState<AgentCard | null>(null)
  const [discovering, setDiscovering] = useState(false)

  // chat
  const [messages, setMessages] = useState<ChatMsg[]>([])
  const [input, setInput] = useState('')
  const [sending, setSending] = useState(false)
  const [copied, setCopied] = useState<number | null>(null)
  const chatRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    api.workflows.list()
      .then((wfs) => setPublishedWorkflows(wfs.filter((w) => w.isPublished)))
      .catch(() => {})
  }, [])

  // auto-scroll chat
  useEffect(() => {
    chatRef.current?.scrollTo(0, chatRef.current.scrollHeight)
  }, [messages, sending])

  const baseUrl = window.location.origin

  const resolveUrl = useCallback((format: Protocol): string => {
    if (isLocal && workflowKey) {
      if (format === 'teams') return `${baseUrl}/teams/${workflowKey}/api/messages`
      const prefix = format === 'mcp' ? 'mcp' : format === 'api' ? 'api' : 'a2a'
      return `${baseUrl}/${prefix}/${workflowKey}`
    }
    return selectedUrl
  }, [isLocal, workflowKey, selectedUrl, baseUrl])

  const fetchAgentCard = useCallback(async (url: string, format: Protocol) => {
    if (format === 'api' || format === 'teams' || format === 'mcp') return
    setDiscovering(true)
    setAgentCard(null)
    try {
      const res = await api.a2a.discover(url, format === 'auto' ? 'auto' : format)
      if (res.healthy && res.agent) {
        setAgentCard(res.agent as AgentCard)
      }
    } catch { /* ignore */ }
    finally { setDiscovering(false) }
  }, [])

  const selectEndpoint = useCallback((ep: { id: string; name: string; url: string; key: string; format: Protocol; local: boolean }) => {
    setSelectedId(ep.id)
    setSelectedName(ep.name)
    setSelectedUrl(ep.url)
    setWorkflowKey(ep.key)
    setIsLocal(ep.local)
    setTestFormat(ep.format)
    setAgentCard(null)
    setMessages([])
    const resolvedUrl = ep.local ? `${baseUrl}/a2a/${ep.key}` : ep.url
    fetchAgentCard(resolvedUrl, ep.format)
  }, [baseUrl, fetchAgentCard])

  const addCustom = () => {
    const url = customUrl.trim()
    if (!url) return
    if (externals.some((e) => e.url === url)) return
    setExternals([...externals, { name: extractName(url), url, format: customFormat }])
    setCustomUrl('')
  }

  const removeExternal = (url: string) => {
    setExternals(externals.filter((e) => e.url !== url))
    if (selectedId === `ext:${url}`) {
      setSelectedId(null)
      setMessages([])
    }
  }

  const handleSend = async () => {
    if (!input.trim() || sending) return
    const userMsg = input.trim()
    setInput('')
    setMessages((prev) => [...prev, { isUser: true, text: userMsg }])
    setSending(true)

    const start = Date.now()
    const url = resolveUrl(testFormat)

    try {
      let result: { success: boolean; response?: string; error?: string }

      if (testFormat === 'api') {
        result = await api.httpTools.test({ url, method: 'POST', body: JSON.stringify({ message: userMsg }), input: userMsg })
      } else {
        // A2A (google/microsoft/auto) + MCP/Teams fallback to a2a.test
        result = await api.a2a.test(url, userMsg, testFormat)
      }

      const elapsed = Date.now() - start
      const text = result.success
        ? tryFormatJson(result.response ?? '')
        : `Error: ${result.error ?? 'Unknown error'}`

      setMessages((prev) => [...prev, {
        isUser: false,
        text,
        elapsedMs: elapsed,
        status: result.success ? 'completed' : 'failed',
      }])
    } catch (err: any) {
      setMessages((prev) => [...prev, {
        isUser: false,
        text: `Error: ${err?.message ?? err}`,
        elapsedMs: Date.now() - start,
        status: 'failed',
      }])
    } finally {
      setSending(false)
    }
  }

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault()
      handleSend()
    }
  }

  const handleCopy = async (idx: number, text: string) => {
    await navigator.clipboard.writeText(text)
    setCopied(idx)
    setTimeout(() => setCopied(null), 2000)
  }

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      {/* Top Bar */}
      <div className="flex items-center border-b border-border bg-card px-5 shrink-0 h-[41px]">
        <FlaskConical size={16} className="text-violet-400 mr-2" />
        <h1 className="text-sm font-semibold text-foreground">{t('nav.tester')}</h1>
      </div>

      <div className="flex flex-1 overflow-hidden">
        {/* ─── Left: Endpoint List ─── */}
        <div className="w-[300px] shrink-0 border-r border-border flex flex-col overflow-hidden">
          {/* Add Custom */}
          <div className="border-b border-border p-3 space-y-2">
            <div className="flex gap-1.5">
              <input
                className="field-input text-xs flex-1"
                value={customUrl}
                onChange={(e) => setCustomUrl(e.target.value)}
                placeholder="https://host/a2a/key"
                onKeyDown={(e) => e.key === 'Enter' && addCustom()}
              />
              <button
                onClick={addCustom}
                disabled={!customUrl.trim()}
                className="rounded-md bg-violet-600 px-2 py-1 text-[10px] font-semibold text-white hover:bg-violet-500 disabled:opacity-50 transition-colors cursor-pointer shrink-0"
              >
                <Plus size={12} />
              </button>
            </div>
            <select
              className="field-input text-[10px]"
              value={customFormat}
              onChange={(e) => setCustomFormat(e.target.value as Protocol)}
            >
              {PROTOCOL_KEYS.map((p) => (
                <option key={p.value} value={p.value}>{t(p.i18nKey)}</option>
              ))}
            </select>
          </div>

          {/* Endpoint List */}
          <div className="flex-1 overflow-y-auto">
            {/* Published (Local) */}
            {publishedWorkflows.length > 0 && (
              <>
                <div className="px-3 pt-3 pb-1 text-[9px] font-semibold uppercase tracking-wider text-muted-foreground">{t('tester.publishedLocal')}</div>
                {publishedWorkflows.map((wf) => {
                  const id = `local:${wf.id}`
                  const active = selectedId === id
                  return (
                    <button
                      key={id}
                      onClick={() => selectEndpoint({ id, name: wf.name, url: '', key: wf.id, format: 'google', local: true })}
                      className={`w-full text-left px-3 py-2 border-b border-border/30 transition-colors cursor-pointer ${active ? 'bg-secondary' : 'hover:bg-secondary/50'}`}
                    >
                      <div className="flex items-center gap-2">
                        <Bot size={14} className="text-violet-400 shrink-0" />
                        <div className="flex-1 min-w-0">
                          <div className="text-xs font-medium text-foreground truncate">{wf.name}</div>
                          <div className="text-[9px] text-muted-foreground truncate">{wf.id}</div>
                        </div>
                      </div>
                    </button>
                  )
                })}
              </>
            )}

            {/* External */}
            {externals.length > 0 && (
              <>
                <div className="px-3 pt-3 pb-1 text-[9px] font-semibold uppercase tracking-wider text-muted-foreground">{t('tester.external')}</div>
                {externals.map((ep) => {
                  const id = `ext:${ep.url}`
                  const active = selectedId === id
                  return (
                    <button
                      key={id}
                      onClick={() => selectEndpoint({ id, name: ep.name, url: ep.url, key: '', format: ep.format, local: false })}
                      className={`w-full text-left px-3 py-2 border-b border-border/30 transition-colors cursor-pointer group ${active ? 'bg-secondary' : 'hover:bg-secondary/50'}`}
                    >
                      <div className="flex items-center gap-2">
                        <Globe size={14} className="text-blue-400 shrink-0" />
                        <div className="flex-1 min-w-0">
                          <div className="text-xs font-medium text-foreground truncate">{ep.name}</div>
                          <div className="text-[9px] text-muted-foreground truncate">{ep.url}</div>
                        </div>
                        <span className="rounded bg-blue-500/15 px-1 py-0.5 text-[8px] text-blue-400 shrink-0">{ep.format}</span>
                        <button
                          onClick={(e) => { e.stopPropagation(); removeExternal(ep.url) }}
                          className="opacity-0 group-hover:opacity-100 text-muted-foreground hover:text-red-400 cursor-pointer shrink-0"
                        >
                          <Trash2 size={11} />
                        </button>
                      </div>
                    </button>
                  )
                })}
              </>
            )}

            {publishedWorkflows.length === 0 && externals.length === 0 && (
              <div className="px-3 py-8 text-center text-xs text-muted-foreground">
                {t('tester.noEndpoints')}
              </div>
            )}
          </div>
        </div>

        {/* ─── Right: Chat Panel ─── */}
        <div className="flex-1 flex flex-col overflow-hidden">
          {selectedId ? (
            <>
              {/* Header */}
              <div className="flex items-center justify-between border-b border-border bg-card px-4 py-2.5 shrink-0">
                <div className="min-w-0 flex-1">
                  <div className="text-xs font-semibold text-foreground truncate">{selectedName}</div>
                  <div className="text-[9px] text-muted-foreground truncate">{resolveUrl(testFormat)}</div>
                  {discovering && <div className="text-[9px] text-muted-foreground/50">Discovering...</div>}
                  {agentCard && (
                    <div className="text-[9px] text-muted-foreground">
                      {agentCard.name}{agentCard.version ? ` v${agentCard.version}` : ''}{agentCard.description ? ` — ${agentCard.description}` : ''}
                    </div>
                  )}
                </div>
                <div className="flex items-center gap-2 shrink-0 ml-3">
                  <select
                    className="field-input text-[10px] w-auto"
                    value={testFormat}
                    onChange={(e) => {
                      const f = e.target.value as Protocol
                      setTestFormat(f)
                      if (f !== 'api' && f !== 'teams' && f !== 'mcp') {
                        fetchAgentCard(resolveUrl(f), f)
                      }
                    }}
                  >
                    {PROTOCOL_KEYS.filter((p) => p.value !== 'auto').map((p) => (
                      <option key={p.value} value={p.value}>{t(p.i18nKey)}</option>
                    ))}
                  </select>
                </div>
              </div>

              {/* Messages */}
              <div ref={chatRef} className="flex-1 overflow-y-auto p-4 space-y-3">
                {messages.length === 0 && !sending && (
                  <div className="text-center py-12 text-xs text-muted-foreground/50">
                    {t('tester.sendMessage')}
                  </div>
                )}
                {messages.map((msg, i) => (
                  <div key={i} className={`flex ${msg.isUser ? 'justify-end' : 'justify-start'}`}>
                    <div className={`max-w-[80%] rounded-lg px-3 py-2 ${msg.isUser
                      ? 'bg-violet-600/20 text-foreground'
                      : 'bg-card border border-border text-foreground'}`}
                    >
                      {!msg.isUser && (
                        <div className="flex items-center justify-between mb-1">
                          <span className="text-[9px] font-semibold text-violet-400">{selectedName}</span>
                          <button
                            onClick={() => handleCopy(i, msg.text)}
                            className="text-muted-foreground hover:text-foreground cursor-pointer ml-2"
                          >
                            {copied === i ? <Check size={10} className="text-green-400" /> : <Copy size={10} />}
                          </button>
                        </div>
                      )}
                      <pre className="text-[11px] font-mono whitespace-pre-wrap break-all m-0">{msg.text}</pre>
                      {msg.elapsedMs !== undefined && (
                        <div className="flex items-center gap-2 mt-1 text-[9px] text-muted-foreground justify-end">
                          <span>{msg.elapsedMs}ms</span>
                          {msg.status && (
                            <span className={`rounded px-1 py-0.5 text-[8px] font-medium ${msg.status === 'completed' ? 'bg-emerald-500/15 text-emerald-400' : 'bg-red-500/15 text-red-400'}`}>
                              {msg.status}
                            </span>
                          )}
                        </div>
                      )}
                    </div>
                  </div>
                ))}
                {sending && (
                  <div className="flex justify-start">
                    <div className="rounded-lg bg-card border border-border px-3 py-2 flex items-center gap-2">
                      <RotateCcw size={12} className="animate-spin text-violet-400" />
                      <span className="text-[10px] text-muted-foreground">{t('tester.waiting')}</span>
                    </div>
                  </div>
                )}
              </div>

              {/* Input */}
              <div className="border-t border-border bg-card px-4 py-3 shrink-0">
                <div className="flex gap-2 items-end">
                  <textarea
                    className="field-textarea text-xs flex-1 resize-none"
                    value={input}
                    onChange={(e) => setInput(e.target.value)}
                    onKeyDown={handleKeyDown}
                    rows={1}
                    placeholder={t('tester.messagePlaceholder')}
                    style={{ minHeight: 36, maxHeight: 80 }}
                  />
                  <button
                    onClick={handleSend}
                    disabled={sending || !input.trim()}
                    className="rounded-md bg-violet-600 px-3 py-2 text-xs font-semibold text-white hover:bg-violet-500 disabled:opacity-50 transition-colors cursor-pointer shrink-0"
                  >
                    {sending ? <RotateCcw size={13} className="animate-spin" /> : <Play size={13} />}
                  </button>
                </div>
              </div>
            </>
          ) : (
            <div className="flex-1 flex items-center justify-center text-xs text-muted-foreground">
              {t('tester.selectEndpoint')}
            </div>
          )}
        </div>
      </div>
    </div>
  )
}

function tryFormatJson(text: string): string {
  try {
    return JSON.stringify(JSON.parse(text), null, 2)
  } catch {
    return text
  }
}
