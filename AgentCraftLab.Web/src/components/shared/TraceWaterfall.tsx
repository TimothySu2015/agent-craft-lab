/**
 * TraceWaterfall — Jaeger/Aspire 風格的瀑布圖，共用元件。
 * 即時模式（Studio ConsolePanel）和歷史模式（RequestLogsPage）共用。
 */
import { useState, useMemo, useCallback } from 'react'
import { useTranslation } from 'react-i18next'
import { ChevronRight, ChevronDown, Copy, Check, X } from 'lucide-react'
import { NODE_COLORS } from '@/components/studio/nodes/registry'
import type { TraceSpan, TraceData } from '@/stores/agent-state'

/** 按索引輪替的顏色序列（每層 span 不同色） */
const SPAN_COLOR_CYCLE = ['blue', 'teal', 'amber', 'purple', 'green', 'orange', 'cyan', 'pink', 'violet', 'yellow', 'red'] as const

function getSpanColorByIndex(index: number) {
  const colorName = SPAN_COLOR_CYCLE[index % SPAN_COLOR_CYCLE.length]
  return NODE_COLORS[colorName] ?? NODE_COLORS.blue
}

interface TraceWaterfallProps {
  data: TraceData
  maxHeight?: string
}

export function TraceWaterfall({ data, maxHeight = '320px' }: TraceWaterfallProps) {
  const { t } = useTranslation('studio')
  const [selectedSpan, setSelectedSpan] = useState<TraceSpan | null>(null)
  const [selectedToolCall, setSelectedToolCall] = useState<{ name: string; result?: string; durationMs?: number } | null>(null)

  if (!data || data.spans.length === 0) {
    return (
      <p className="px-3 py-3 text-muted-foreground text-center text-[10px]">
        {t('trace.noData', 'No trace data')}
      </p>
    )
  }

  return (
    <>
      <div className="text-[10px] font-mono overflow-x-hidden" style={{ maxHeight, overflowY: 'auto' }}>
        {/* Timeline header */}
        <TimelineHeader totalMs={data.totalMs} />
        {/* Span rows */}
        {data.spans.map((span, i) => (
          <SpanRow key={span.id} span={span} totalMs={data.totalMs} depth={0} colorIndex={i}
            onSelect={setSelectedSpan} onSelectToolCall={setSelectedToolCall} />
        ))}
      </div>
      {selectedSpan && <SpanDetailModal span={selectedSpan} onClose={() => setSelectedSpan(null)} />}
      {selectedToolCall && <ToolCallModal tc={selectedToolCall} onClose={() => setSelectedToolCall(null)} />}
    </>
  )
}

function TimelineHeader({ totalMs }: { totalMs: number }) {
  const ticks = useMemo(() => {
    if (totalMs <= 0) return []
    const interval = totalMs < 10_000 ? 1000
      : totalMs < 60_000 ? 5000
      : totalMs < 300_000 ? 30_000
      : 60_000
    const result: number[] = []
    for (let t = 0; t <= totalMs; t += interval) result.push(t)
    return result
  }, [totalMs])

  if (ticks.length === 0) return null

  return (
    <div className="flex items-center border-b border-border/30 px-2 py-0.5 text-muted-foreground text-[8px]">
      <div className="w-[140px] shrink-0" />
      <div className="flex-1 relative h-3">
        {ticks.map(t => (
          <span
            key={t}
            className="absolute text-[7px]"
            style={{ left: `${(t / totalMs) * 100}%` }}
          >
            {formatMs(t)}
          </span>
        ))}
      </div>
    </div>
  )
}

function SpanRow({ span, totalMs, depth, colorIndex, onSelect, onSelectToolCall }: {
  span: TraceSpan; totalMs: number; depth: number; colorIndex: number
  onSelect: (s: TraceSpan) => void
  onSelectToolCall: (tc: { name: string; result?: string; durationMs?: number }) => void
}) {
  const [expanded, setExpanded] = useState(false)
  const children = (span as any).children as TraceSpan[] | undefined
  const hasChildren = children && children.length > 0
  const hasToolCalls = span.toolCalls && span.toolCalls.length > 0
  const isExpandable = hasChildren || hasToolCalls
  const color = getSpanColorByIndex(colorIndex)
  const isError = span.status === 'error'
  const isCancelled = span.status === 'cancelled'

  const barLeft = totalMs > 0 ? Math.max(0, (span.startMs / totalMs) * 100) : 0
  const barWidth = totalMs > 0 ? Math.max(0.5, ((span.endMs - span.startMs) / totalMs) * 100) : 0
  const duration = span.endMs - span.startMs

  return (
    <>
      {/* Main row */}
      <div
        className={`flex items-center hover:bg-accent/20 cursor-pointer group ${isError ? 'bg-red-500/5' : isCancelled ? 'bg-gray-500/5 opacity-60' : ''}`}
        onClick={() => onSelect(span)}
      >
        {/* Name column */}
        <div
          className="w-[140px] shrink-0 flex items-center gap-0.5 px-1 py-[2px] truncate"
          style={{ paddingLeft: `${depth * 12 + 4}px` }}
        >
          {isExpandable ? (
            <button
              onClick={(e) => { e.stopPropagation(); setExpanded(!expanded) }}
              className="shrink-0 w-3 h-3 flex items-center justify-center text-muted-foreground hover:text-foreground"
            >
              {expanded ? <ChevronDown size={9} /> : <ChevronRight size={9} />}
            </button>
          ) : (
            <span className="w-3 shrink-0" />
          )}
          <span className={`w-1.5 h-1.5 rounded-full shrink-0 ${color.iconBg}`} />
          <span className={`truncate ${isError ? 'text-red-400' : isCancelled ? 'text-gray-400 line-through' : 'text-foreground'}`}>
            {span.name}
          </span>
          {hasToolCalls && (
            <span className="text-muted-foreground/50 text-[8px] ml-0.5">
              ({span.toolCalls!.length})
            </span>
          )}
        </div>

        {/* Bar column */}
        <div className="flex-1 relative h-[14px] mx-1 overflow-hidden">
          <div
            className={`absolute top-[3px] h-[8px] rounded-sm ${isError ? 'bg-red-500/40 border border-red-500/60 border-dashed' : isCancelled ? 'bg-gray-500/30 border border-gray-500/50 border-dashed' : color.iconBg}`}
            style={{ left: `${barLeft}%`, width: `${barWidth}%`, minWidth: '2px' }}
          />
          {/* Duration label */}
          <span
            className="absolute top-0 text-[8px] text-muted-foreground whitespace-nowrap"
            style={{ left: `${Math.min(barLeft + barWidth + 0.5, 85)}%` }}
          >
            {formatMs(duration)}
            {span.tokens ? ` · ${span.tokens.toLocaleString()}tk` : ''}
            {isCancelled && ' · cancelled'}
          </span>
        </div>
      </div>

      {/* Tool call sub-rows */}
      {expanded && hasToolCalls && span.toolCalls!.map((tc, i) => (
        <div
          key={`tc-${i}`}
          className="flex items-center hover:bg-accent/10 cursor-pointer"
          onClick={() => onSelectToolCall(tc)}
        >
          {/* Name column — tool call 用較寬的空間 */}
          <div
            className="w-[200px] shrink-0 flex items-center gap-0.5 px-1 py-[2px] text-[9px]"
            style={{ paddingLeft: `${(depth + 1) * 12 + 16}px` }}
          >
            <span className="shrink-0 text-yellow-400">&#x1F527;</span>
            <span className="text-muted-foreground truncate">{tc.name}</span>
          </div>
          {/* Bar column */}
          <div className="flex-1 relative h-[14px] mx-1 overflow-hidden">
            {tc.durationMs != null && tc.durationMs > 0 && (
              <div
                className="absolute top-[3px] h-[8px] rounded-sm bg-yellow-500/20"
                style={{
                  left: `${(span.startMs / totalMs) * 100}%`,
                  width: `${Math.max(0.5, (tc.durationMs / totalMs) * 100)}%`,
                  minWidth: '2px',
                }}
              />
            )}
            <span
              className="absolute top-0 text-[8px] text-muted-foreground whitespace-nowrap"
              style={{ left: `${Math.min(((span.startMs + (tc.durationMs ?? 0)) / totalMs) * 100 + 0.5, 85)}%` }}
            >
              {tc.durationMs != null && tc.durationMs > 0 ? formatMs(tc.durationMs) : ''}
              {tc.result ? ` ${tc.result.slice(0, 60)}${tc.result.length > 60 ? '...' : ''}` : ''}
            </span>
          </div>
        </div>
      ))}

      {/* Children */}
      {expanded && hasChildren && children.map((child, ci) => (
        <SpanRow key={child.id} span={child} totalMs={totalMs} depth={depth + 1} colorIndex={colorIndex + ci + 1}
          onSelect={onSelect} onSelectToolCall={onSelectToolCall} />
      ))}
    </>
  )
}

function SpanDetailModal({ span, onClose }: { span: TraceSpan; onClose: () => void }) {
  const [copied, setCopied] = useState<string | null>(null)
  const duration = span.endMs - span.startMs

  const copy = useCallback((text: string, key: string) => {
    navigator.clipboard.writeText(text)
    setCopied(key)
    setTimeout(() => setCopied(null), 1500)
  }, [])

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" onClick={onClose}>
      <div
        className="bg-card border border-border rounded-lg shadow-xl w-[90vw] max-w-[700px] max-h-[80vh] flex flex-col"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div className="flex items-center justify-between px-4 py-3 border-b border-border">
          <div className="flex items-center gap-2">
            <span className={`w-2 h-2 rounded-full ${getSpanColorByIndex(0).iconBg}`} />
            <span className="font-semibold text-sm text-foreground">{span.name}</span>
            <span className="text-xs text-muted-foreground">{formatMs(duration)}</span>
          </div>
          <button onClick={onClose} className="text-muted-foreground hover:text-foreground">
            <X size={16} />
          </button>
        </div>

        {/* Meta */}
        <div className="flex flex-wrap gap-x-5 gap-y-1 px-4 py-2 border-b border-border/50 text-xs text-muted-foreground">
          <span>Type: <span className="text-foreground">{span.type}</span></span>
          <span>Source: <span className="text-foreground">{span.source}</span></span>
          {span.model && <span>Model: <span className="text-foreground">{span.model}</span></span>}
          {span.inputTokens != null && <span>Input Tokens: <span className="text-foreground">{span.inputTokens.toLocaleString()}</span></span>}
          {span.outputTokens != null && <span>Output Tokens: <span className="text-foreground">{span.outputTokens.toLocaleString()}</span></span>}
          {span.tokens != null && <span>Total: <span className="text-foreground">{span.tokens.toLocaleString()}</span></span>}
        </div>

        {/* Content */}
        <div className="flex-1 overflow-y-auto px-4 py-3 space-y-3 text-xs">
          {span.error && (
            <ContentBlock label="Error" text={span.error} color="text-red-400" copied={copied === 'error'} onCopy={() => copy(span.error!, 'error')} />
          )}
          {span.toolCalls && span.toolCalls.length > 0 && (
            <div>
              <span className="font-medium text-muted-foreground">Tool Calls ({span.toolCalls.length})</span>
              <div className="mt-1 space-y-2">
                {span.toolCalls.map((tc, i) => (
                  <div key={i} className="bg-accent/20 rounded p-2">
                    <div className="flex items-center gap-2 mb-1">
                      <span className="text-yellow-400">&#x1F527;</span>
                      <span className="font-medium text-foreground">{tc.name}</span>
                      {tc.durationMs != null && tc.durationMs > 0 && (
                        <span className="text-blue-400 text-[10px]">{formatMs(tc.durationMs)}</span>
                      )}
                      {tc.args && <span className="text-muted-foreground">({tc.args})</span>}
                      <button
                        onClick={() => copy(tc.result ?? '', `tc-${i}`)}
                        className="text-muted-foreground hover:text-foreground ml-auto"
                      >
                        {copied === `tc-${i}` ? <Check size={12} className="text-green-400" /> : <Copy size={12} />}
                      </button>
                    </div>
                    {tc.result && (
                      <pre className="text-foreground whitespace-pre-wrap break-all max-h-[200px] overflow-y-auto text-[11px] leading-relaxed">
                        {tc.result}
                      </pre>
                    )}
                  </div>
                ))}
              </div>
            </div>
          )}
          {span.input && (
            <ContentBlock label="Input" text={span.input} copied={copied === 'input'} onCopy={() => copy(span.input!, 'input')} />
          )}
          {span.result && (
            <ContentBlock label="Output" text={span.result} copied={copied === 'result'} onCopy={() => copy(span.result!, 'result')} />
          )}
          {!span.error && !span.input && !span.result && (
            <p className="text-muted-foreground text-center py-4">No content available</p>
          )}
        </div>
      </div>
    </div>
  )
}

function ContentBlock({ label, text, color, copied, onCopy }: {
  label: string; text: string; color?: string; copied: boolean; onCopy: () => void
}) {
  return (
    <div>
      <div className="flex items-center gap-2 mb-1">
        <span className={`font-medium ${color ?? 'text-muted-foreground'}`}>{label}</span>
        <button onClick={onCopy} className="text-muted-foreground hover:text-foreground">
          {copied ? <Check size={12} className="text-green-400" /> : <Copy size={12} />}
        </button>
      </div>
      <pre className={`${color ?? 'text-foreground'} whitespace-pre-wrap break-all bg-accent/20 rounded p-3 max-h-[300px] overflow-y-auto text-[11px] leading-relaxed`}>
        {text}
      </pre>
    </div>
  )
}

function ToolCallModal({ tc, onClose }: {
  tc: { name: string; result?: string; durationMs?: number }; onClose: () => void
}) {
  const [copied, setCopied] = useState(false)

  const copy = () => {
    navigator.clipboard.writeText(tc.result ?? '')
    setCopied(true)
    setTimeout(() => setCopied(false), 1500)
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" onClick={onClose}>
      <div
        className="bg-card border border-border rounded-lg shadow-xl w-[90vw] max-w-[700px] max-h-[80vh] flex flex-col"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between px-4 py-3 border-b border-border">
          <div className="flex items-center gap-2">
            <span className="text-yellow-400">&#x1F527;</span>
            <span className="font-semibold text-sm text-foreground">{tc.name}</span>
            {tc.durationMs != null && tc.durationMs > 0 && (
              <span className="text-xs text-blue-400">{formatMs(tc.durationMs)}</span>
            )}
          </div>
          <button onClick={onClose} className="text-muted-foreground hover:text-foreground">
            <X size={16} />
          </button>
        </div>
        <div className="flex-1 overflow-y-auto px-4 py-3">
          {tc.result ? (
            <div>
              <div className="flex items-center gap-2 mb-2">
                <span className="font-medium text-xs text-muted-foreground">Result</span>
                <button onClick={copy} className="text-muted-foreground hover:text-foreground">
                  {copied ? <Check size={12} className="text-green-400" /> : <Copy size={12} />}
                </button>
              </div>
              <pre className="text-foreground whitespace-pre-wrap break-all bg-accent/20 rounded p-3 max-h-[400px] overflow-y-auto text-[11px] leading-relaxed">
                {tc.result}
              </pre>
            </div>
          ) : (
            <p className="text-muted-foreground text-center py-4 text-xs">No result content</p>
          )}
        </div>
      </div>
    </div>
  )
}

function formatMs(ms: number): string {
  if (ms < 1) return '<1ms'
  if (ms < 1000) return `${Math.round(ms)}ms`
  if (ms < 60_000) return `${(ms / 1000).toFixed(1)}s`
  return `${(ms / 60_000).toFixed(1)}m`
}
