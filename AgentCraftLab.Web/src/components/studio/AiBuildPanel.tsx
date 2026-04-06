/**
 * AiBuildPanel — AI Build 模式：自然語言描述 → SSE 串流生成 workflow → 套用到畫布。
 * 呼叫 POST /api/flow-builder（SSE 串流）。
 */
import { useState, useRef, useEffect, useCallback } from 'react'
import { useTranslation } from 'react-i18next'
import { Send, Square, Sparkles } from 'lucide-react'
import { useWorkflowStore } from '@/stores/workflow-store'
import { useCredentialStore } from '@/stores/credential-store'
import { useDefaultCredential } from '@/hooks/useDefaultCredential'
import { toWorkflowPayloadJson } from '@/lib/workflow-payload'
import { expandFlowPlanParallel } from '@/lib/expand-parallel'
import { notify } from '@/lib/notify'
import { AssistantMessage, type Message, type MessageMetadata } from './AssistantMessage'

const MAX_HISTORY_PAIRS = 5

/** 修復 LLM 在 JSON string 中輸出的字面換行符 → 跳脫為 \\n */
function fixUnescapedNewlinesInJsonStrings(json: string): string {
  const result: string[] = []
  let inString = false
  let escaped = false
  for (let i = 0; i < json.length; i++) {
    const c = json[i]
    if (escaped) { result.push(c); escaped = false; continue }
    if (c === '\\' && inString) { result.push(c); escaped = true; continue }
    if (c === '"') { inString = !inString; result.push(c); continue }
    if (inString && (c === '\n' || c === '\r')) {
      if (c === '\r' && i + 1 < json.length && json[i + 1] === '\n') i++
      result.push('\\n')
      continue
    }
    result.push(c)
  }
  return result.join('')
}

// 移除 JSON string 外部的行註解和區塊註解（不動 string 內的 // 如 URL）
function removeJsonComments(json: string): string {
  const result: string[] = []
  let inString = false
  let escaped = false
  let i = 0
  while (i < json.length) {
    const c = json[i]
    if (escaped) { result.push(c); escaped = false; i++; continue }
    if (c === '\\' && inString) { result.push(c); escaped = true; i++; continue }
    if (c === '"') { inString = !inString; result.push(c); i++; continue }
    if (!inString) {
      // 行尾 // 註解
      if (c === '/' && i + 1 < json.length && json[i + 1] === '/') {
        while (i < json.length && json[i] !== '\n') i++
        continue
      }
      // 區塊 /* */ 註解
      if (c === '/' && i + 1 < json.length && json[i + 1] === '*') {
        i += 2
        while (i + 1 < json.length && !(json[i] === '*' && json[i + 1] === '/')) i++
        i += 2
        continue
      }
    }
    result.push(c)
    i++
  }
  return result.join('')
}

/** 修復 LLM 截斷造成的未閉合 JSON — 補上缺少的 ", ] , } */
function repairTruncatedJson(json: string): string {
  const trimmed = json.trimEnd()
  let inString = false
  let escaped = false
  const stack: string[] = []

  for (const c of trimmed) {
    if (escaped) { escaped = false; continue }
    if (c === '\\' && inString) { escaped = true; continue }
    if (c === '"') { inString = !inString; continue }
    if (inString) continue
    if (c === '{') stack.push('}')
    else if (c === '[') stack.push(']')
    else if ((c === '}' || c === ']') && stack.length > 0) stack.pop()
  }

  let result = trimmed
  if (inString) result += '"'
  // 移除尾部懸空的逗號或冒號
  result = result.replace(/[,:\s]+$/, '')
  // 補上未閉合的 brackets
  while (stack.length > 0) result += stack.pop()
  return result
}

export function AiBuildPanel() {
  const { t } = useTranslation('chat')
  const { t: tn } = useTranslation('notifications')
  const [messages, setMessages] = useState<Message[]>([])
  const [input, setInput] = useState('')
  const [streaming, setStreaming] = useState(false)
  const [applied, setApplied] = useState<string | null>(null)
  const [useLegacy, setUseLegacy] = useState(false)
  const abortRef = useRef<AbortController | null>(null)
  const scrollRef = useRef<HTMLDivElement>(null)

  const getCredential = useDefaultCredential()

  // Auto-scroll
  useEffect(() => {
    if (scrollRef.current) scrollRef.current.scrollTop = scrollRef.current.scrollHeight
  }, [messages])

  const handleSend = useCallback(async () => {
    if (!input.trim() || streaming) return
    const cred = getCredential()
    if (!cred) return

    const userMsg: Message = { role: 'user', text: input.trim() }
    const newMessages = [...messages, userMsg]
    setMessages(newMessages)
    setInput('')
    setStreaming(true)
    setApplied(null)

    // Build history (max 5 pairs)
    const history = newMessages
      .slice(-(MAX_HISTORY_PAIRS * 2))
      .filter((m) => m.role === 'user' || m.role === 'assistant')
      .slice(0, -1) // exclude current user message
      .map((m) => ({ role: m.role, content: m.text }))

    const { nodes, edges } = useWorkflowStore.getState()
    const settings = useWorkflowStore.getState().workflowSettings
    const currentPayload = toWorkflowPayloadJson(nodes, edges, settings)

    const controller = new AbortController()
    abortRef.current = controller

    let assistantText = ''
    let metadata: MessageMetadata | undefined

    try {
      const res = await fetch('/api/flow-builder', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        signal: controller.signal,
        body: JSON.stringify({
          message: userMsg.text,
          provider: cred.provider,
          model: cred.model || 'gpt-4o',
          currentPayload,
          history,
          mode: useLegacy ? 'legacy' : undefined,
        }),
      })

      if (!res.ok) throw new Error(res.statusText)
      if (!res.body) throw new Error('No response body')

      const reader = res.body.getReader()
      const decoder = new TextDecoder()

      while (true) {
        const { done, value } = await reader.read()
        if (done) break

        const chunk = decoder.decode(value, { stream: true })
        const lines = chunk.split('\n')

        for (const line of lines) {
          if (line.startsWith('data: ')) {
            const data = line.slice(6).trim()
            if (data === '[DONE]') break
            try {
              const parsed = JSON.parse(data)
              if (typeof parsed === 'object' && parsed?.type === '__metadata') {
                metadata = {
                  durationMs: parsed.durationMs ?? 0,
                  estimatedTokens: parsed.estimatedTokens ?? 0,
                  model: parsed.model ?? '',
                  estimatedCost: parsed.estimatedCost ?? '',
                }
              } else if (typeof parsed === 'string') {
                assistantText += parsed
                setMessages((prev) => {
                  const updated = [...prev]
                  const last = updated[updated.length - 1]
                  if (last?.role === 'assistant') {
                    last.text = assistantText
                  } else {
                    updated.push({ role: 'assistant', text: assistantText })
                  }
                  return [...updated]
                })
              }
            } catch {
              // skip unparseable chunks
            }
          }
        }
      }
    } catch (err) {
      if ((err as Error).name !== 'AbortError') {
        assistantText += `\n\n[Error: ${(err as Error).message}]`
      }
    } finally {
      // Extract ```json block
      const jsonMatch = assistantText.match(/```json\s*([\s\S]*?)```/)
      const workflowJson = jsonMatch?.[1]?.trim()

      setMessages((prev) => {
        const updated = [...prev]
        const last = updated[updated.length - 1]
        if (last?.role === 'assistant') {
          last.text = assistantText
          if (workflowJson) last.workflowJson = workflowJson
          if (metadata) last.metadata = metadata
        }
        return [...updated]
      })
      setStreaming(false)
      abortRef.current = null
    }
  }, [input, streaming, messages, getCredential, useLegacy])

  const handleApply = useCallback(async (json: string) => {
    try {
      // AI 可能在 JSON 中插入 // 或 /* */ 註解，先清除
      // 先修復字面換行符（必須在移除註解之前，否則 URL 中的 // 會被誤判為註解）
      let cleaned = fixUnescapedNewlinesInJsonStrings(json)
      // 移除 JSON string 外部的 // 和 /* */ 註解（不動 string 內的 //）
      cleaned = removeJsonComments(cleaned)
      // 清除尾隨逗號
      cleaned = cleaned.replace(/,\s*([}\]])/g, '$1')
      // 修復 LLM 截斷造成的未閉合 JSON
      cleaned = repairTruncatedJson(cleaned)

      // Debug: 記錄清理後的 JSON（即使 parse 失敗也能在 console 看到）
      console.debug('[AiBuild] cleaned JSON:', cleaned)

      const spec = JSON.parse(cleaned)
      if (!spec.nodes) return

      // Debug：記錄展開前的 spec
      const debugBefore = structuredClone(spec)

      // FlowPlan parallel 展開
      expandFlowPlanParallel(spec)

      // Debug：記錄展開後的 spec
      window.__craftlab_debug = {
        ...window.__craftlab_debug,
        lastAiBuildSpec: debugBefore,
        lastExpandedSpec: structuredClone(spec),
        lastApplyErrors: [],
        ts: new Date().toISOString(),
      }

      // Convert AI spec to React Flow nodes + edges
      const nodes = spec.nodes.map((n: any, i: number) => {
        const nodeType = n.type || n.nodeType || 'agent'
        const nodeName = n.name || `Node-${i + 1}`
        const { type: _t, name: _n, id: _id, ...flatFields } = n
        const extraData = n.data ? n.data : flatFields

        // Agent/Autonomous 節點缺 provider/model 時，自動帶入 credential store 的設定
        if ((nodeType === 'agent' || nodeType === 'autonomous') && !extraData.provider) {
          const creds = useCredentialStore.getState().credentials
          const configured = Object.entries(creds).find(([, v]) => v.apiKey)
          if (configured) {
            const [provider, entry] = configured
            extraData.provider = provider
            extraData.model = extraData.model || entry.model || 'gpt-4o'
          }
        }

        return {
          id: n.id || `${nodeType}-${i + 1}`,
          type: nodeType,
          position: { x: 300 + i * 250, y: 200 },
          data: { type: nodeType, name: nodeName, ...extraData },
        }
      })

      // Add start + end
      const startNode = { id: 'start-1', type: 'start', position: { x: 50, y: 200 }, data: { type: 'start', name: 'Start' } }
      const endNode = { id: 'end-1', type: 'end', position: { x: 300 + nodes.length * 250, y: 200 }, data: { type: 'end', name: 'End' } }

      const edges: any[] = []
      // Start → first node
      if (nodes.length > 0) edges.push({ id: 'e-start-0', source: 'start-1', target: nodes[0].id })
      // Connections from spec
      if (spec.connections) {
        for (const conn of spec.connections) {
          const fromNode = nodes[conn.from]
          const toNode = nodes[conn.to]
          if (fromNode && toNode) {
            edges.push({
              id: `e-${fromNode.id}-${toNode.id}`,
              source: fromNode.id,
              target: toNode.id,
              sourceHandle: conn.fromOutput || undefined,
            })
          }
        }
      } else {
        // Default: chain
        for (let i = 0; i < nodes.length - 1; i++) {
          edges.push({ id: `e-${i}-${i + 1}`, source: nodes[i].id, target: nodes[i + 1].id })
        }
      }
      // Terminal nodes → end
      const connectedSources = new Set(edges.map((e: any) => `${e.source}:${e.sourceHandle ?? 'output_1'}`))
      let hasTerminal = false
      for (const n of nodes) {
        if (n.type === 'parallel') {
          const branchCount = ((n.data as any).branches || '').split(',').filter(Boolean).length
          const donePort = `output_${branchCount + 1}`
          if (!connectedSources.has(`${n.id}:${donePort}`)) {
            edges.push({ id: `e-${n.id}-done-end`, source: n.id, target: 'end-1', sourceHandle: donePort })
            hasTerminal = true
          }
        }
      }
      if (!hasTerminal) {
        const sourcesWithAnyEdge = new Set(edges.map((e: any) => e.source))
        const terminals = nodes.filter((n) => !sourcesWithAnyEdge.has(n.id) && n.type !== 'start' && n.type !== 'end')
        if (terminals.length > 0) {
          for (const t of terminals) {
            edges.push({ id: `e-${t.id}-end`, source: t.id, target: 'end-1' })
          }
        } else if (nodes.length > 0) {
          edges.push({ id: 'e-last-end', source: nodes[nodes.length - 1].id, target: 'end-1' })
        }
      }

      useWorkflowStore.getState().setWorkflow([startNode, ...nodes, endNode], edges)
      await useWorkflowStore.getState().layout()
      setApplied(json)
    } catch (err) {
      console.error('Failed to apply workflow spec:', err)
      notify.error(tn('aiBuildApplyFailed'), { description: err instanceof Error ? err.message : String(err) })
    }
  }, [])

  const handleStop = useCallback(() => {
    abortRef.current?.abort()
  }, [])

  const cred = getCredential()

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      {/* Messages */}
      <div ref={scrollRef} className="flex-1 overflow-y-auto px-3 py-3 space-y-3">
        {messages.length === 0 && (
          <div className="text-center py-8">
            <Sparkles size={24} className="mx-auto mb-2 text-violet-400" />
            <p className="text-xs text-muted-foreground mb-3">{t('aiBuild.welcome')}</p>
            <div className="space-y-1.5">
              {['aiBuild.example1', 'aiBuild.example2', 'aiBuild.example3'].map((key) => (
                <button
                  key={key}
                  onClick={() => setInput(t(key))}
                  className="block w-full text-left rounded-md border border-border/50 px-3 py-1.5 text-[10px] text-muted-foreground hover:bg-accent/30 transition-colors cursor-pointer"
                >
                  {t(key)}
                </button>
              ))}
            </div>
          </div>
        )}

        {messages.map((msg, i) => (
          <div key={i} className={msg.role === 'user' ? 'flex justify-end' : ''}>
            {msg.role === 'user' ? (
              <div className="rounded-lg bg-blue-600/20 border border-blue-600/30 px-3 py-1.5 text-xs text-foreground max-w-[85%]">
                {msg.text}
              </div>
            ) : (
              <AssistantMessage msg={msg} applied={applied} onApply={handleApply} t={t} />
            )}
          </div>
        ))}

        {streaming && (
          <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
            <div className="flex gap-0.5">
              <span className="w-1 h-1 rounded-full bg-violet-400 animate-bounce" style={{ animationDelay: '0ms' }} />
              <span className="w-1 h-1 rounded-full bg-violet-400 animate-bounce" style={{ animationDelay: '150ms' }} />
              <span className="w-1 h-1 rounded-full bg-violet-400 animate-bounce" style={{ animationDelay: '300ms' }} />
            </div>
          </div>
        )}
      </div>

      {/* No credential warning */}
      {!cred && (
        <div className="px-3 py-1.5 text-[10px] text-amber-400 bg-amber-400/5 border-t border-amber-400/20">
          {t('aiBuild.noCredential')}
        </div>
      )}

      {/* Mode toggle */}
      <div className="flex items-center justify-end gap-1.5 px-3 pt-1.5 border-t border-border/50">
        <label className="flex items-center gap-1 cursor-pointer text-[9px] text-muted-foreground">
          <input type="checkbox" checked={useLegacy} onChange={(e) => setUseLegacy(e.target.checked)} className="accent-violet-500" />
          Legacy
        </label>
        <span className={`text-[9px] font-medium ${useLegacy ? 'text-muted-foreground' : 'text-violet-400'}`}>
          {useLegacy ? 'Basic' : 'Flow Planner'}
        </span>
      </div>

      {/* Input */}
      <div className="flex gap-1.5 px-3 pb-2 pt-1">
        <input
          value={input}
          onChange={(e) => setInput(e.target.value)}
          onKeyDown={(e) => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); handleSend() } }}
          placeholder={t('aiBuild.placeholder')}
          className="flex-1 rounded-md border border-border bg-background px-2.5 py-1.5 text-xs text-foreground placeholder:text-muted-foreground focus:outline-none focus:ring-1 focus:ring-ring"
          disabled={streaming || !cred}
        />
        {streaming ? (
          <button onClick={handleStop} className="rounded-md bg-red-600 p-1.5 text-white hover:bg-red-500 cursor-pointer">
            <Square size={14} />
          </button>
        ) : (
          <button
            onClick={handleSend}
            disabled={!input.trim() || !cred}
            className="rounded-md bg-violet-600 p-1.5 text-white hover:bg-violet-500 disabled:opacity-50 cursor-pointer"
          >
            <Send size={14} />
          </button>
        )}
      </div>
    </div>
  )
}
