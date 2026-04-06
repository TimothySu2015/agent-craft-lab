/**
 * AssistantMessage — AI Build 回覆訊息元件。
 * 拆分為「思考」（可收合）、「JSON viewer」（可收合）、「結果」、「套用按鈕」、「統計」。
 */
import { useState, useMemo } from 'react'
import { Sparkles, CheckCircle2, ChevronRight, Brain, Code2 } from 'lucide-react'
import JsonView from 'react18-json-view'
import 'react18-json-view/src/style.css'
import { StatsLine } from './StatsLine'

export interface MessageMetadata {
  durationMs: number
  estimatedTokens: number
  model: string
  estimatedCost: string
}

export interface Message {
  role: 'user' | 'assistant'
  text: string
  workflowJson?: string
  metadata?: MessageMetadata
}

interface AssistantMessageProps {
  msg: Message
  applied: string | null
  onApply: (json: string) => void
  t: (key: string) => string
}

export function AssistantMessage({ msg, applied, onApply, t }: AssistantMessageProps) {
  const [showThinking, setShowThinking] = useState(false)
  const [showJson, setShowJson] = useState(false)

  // 拆分：```json 之前 = thinking，之後 = result
  const jsonBlockStart = msg.text.indexOf('```json')
  const jsonBlockEnd = msg.text.indexOf('```', jsonBlockStart + 7)
  const hasJsonBlock = jsonBlockStart !== -1 && jsonBlockEnd !== -1

  const thinking = hasJsonBlock ? msg.text.slice(0, jsonBlockStart).trim() : ''
  const rawResult = hasJsonBlock
    ? msg.text.slice(jsonBlockEnd + 3).trim()
    : msg.text.replace(/```json[\s\S]*?```/g, '').trim()
  const result = rawResult.replace(/^json\s*/i, '').trim().startsWith('{') ? '' : rawResult

  const parsedJson = useMemo(() => {
    if (!msg.workflowJson) return null
    try {
      const cleaned = msg.workflowJson
        .replace(/\/\/.*$/gm, '')
        .replace(/\/\*[\s\S]*?\*\//g, '')
        .replace(/,\s*([}\]])/g, '$1')
      return JSON.parse(cleaned)
    } catch { return null }
  }, [msg.workflowJson])

  return (
    <div className="text-xs text-foreground space-y-2">
      {/* Thinking — 可收合區塊 */}
      {thinking && (
        <div>
          <button
            onClick={() => setShowThinking((v) => !v)}
            className="flex items-center gap-1 text-[10px] text-muted-foreground hover:text-foreground transition-colors cursor-pointer"
          >
            <ChevronRight size={12} className={`transition-transform ${showThinking ? 'rotate-90' : ''}`} />
            <Brain size={12} />
            <span>{t('aiBuild.thinking')}</span>
          </button>
          {showThinking && (
            <pre className="mt-1.5 whitespace-pre-wrap font-sans leading-relaxed text-[10px] text-muted-foreground border-l-2 border-muted-foreground/20 pl-2.5 ml-1">
              {thinking}
            </pre>
          )}
        </div>
      )}

      {/* JSON — 可收合 JSON viewer */}
      {parsedJson && (
        <div>
          <button
            onClick={() => setShowJson((v) => !v)}
            className="flex items-center gap-1 text-[10px] text-muted-foreground hover:text-foreground transition-colors cursor-pointer"
          >
            <ChevronRight size={12} className={`transition-transform ${showJson ? 'rotate-90' : ''}`} />
            <Code2 size={12} />
            <span>Workflow JSON</span>
            <span className="text-[9px] opacity-50">
              ({parsedJson.nodes?.length ?? 0} nodes)
            </span>
          </button>
          {showJson && (
            <div className="mt-1.5 rounded-md border border-border/50 bg-accent/30 p-2 overflow-auto max-h-[300px] ml-1">
              <JsonView
                src={parsedJson}
                theme="a11y"
                dark
                style={{ fontSize: '10px', background: 'transparent' }}
              />
            </div>
          )}
        </div>
      )}

      {/* Result — 主要內容 */}
      {result && (
        <pre className="whitespace-pre-wrap font-sans leading-relaxed">{result}</pre>
      )}

      {/* 無思考區時顯示完整文字（串流中） */}
      {!thinking && !result && !hasJsonBlock && (
        <pre className="whitespace-pre-wrap font-sans leading-relaxed text-muted-foreground">{msg.text}</pre>
      )}

      {/* Apply 按鈕 */}
      {msg.workflowJson && (
        <button
          onClick={() => onApply(msg.workflowJson!)}
          disabled={applied === msg.workflowJson}
          className={`flex items-center gap-1 rounded-md px-3 py-1.5 text-[10px] font-semibold transition-colors cursor-pointer ${
            applied === msg.workflowJson
              ? 'bg-green-600/20 text-green-400 border border-green-600/30'
              : 'bg-violet-600 text-white hover:bg-violet-500'
          }`}
        >
          {applied === msg.workflowJson ? (
            <><CheckCircle2 size={12} /> {t('aiBuild.applied')}</>
          ) : (
            <><Sparkles size={12} /> {t('aiBuild.applyToCanvas')}</>
          )}
        </button>
      )}

      {msg.metadata && (
        <StatsLine
          durationMs={msg.metadata.durationMs}
          tokens={msg.metadata.estimatedTokens}
          cost={msg.metadata.estimatedCost}
          model={msg.metadata.model}
          className="pt-0.5"
        />
      )}
    </div>
  )
}
