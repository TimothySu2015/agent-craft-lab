/**
 * ConsolePanel — 畫布下方可收合的執行日誌面板。
 * 從 coagent-store 的 recentLogs 讀取（AG-UI STATE_SNAPSHOT 同步）。
 * 支援 Log / Trace 雙 tab 切換。
 */
import { useState, useRef, useEffect, useCallback } from 'react'
import { useTranslation } from 'react-i18next'
import { ChevronDown, ChevronUp, Trash2, Terminal } from 'lucide-react'
import { useCoAgentStore } from '@/stores/coagent-store'
import type { ConsoleLog } from '@/stores/agent-state'
import { StatsLine } from './StatsLine'
import { TraceWaterfall } from '@/components/shared/TraceWaterfall'
import { useTraceBuilder } from '@/hooks/useTraceBuilder'

const EMPTY_LOGS: ConsoleLog[] = []

const LEVEL_CONFIG: Record<string, { color: string; icon: string }> = {
  info: { color: 'text-blue-400', icon: '\u25CF' },
  success: { color: 'text-green-400', icon: '\u2713' },
  error: { color: 'text-red-400', icon: '\u2717' },
  warning: { color: 'text-amber-400', icon: '\u26A0' },
}

interface RagCitation {
  fileName: string
  chunkIndex: number
  score: number
  content: string
}

type TabId = 'log' | 'trace' | 'sources'

export function ConsolePanel() {
  const { t } = useTranslation('studio')
  const logs = useCoAgentStore((s) => s.state?.recentLogs ?? EMPTY_LOGS)
  const stats = useCoAgentStore((s) => s.state?.executionStats)
  const ragCitations = useCoAgentStore((s) => (s.state as any)?.ragCitations as RagCitation[] | undefined)
  const expandedQueries = useCoAgentStore((s) => (s.state as any)?.expandedQueries as string[] | undefined)
  const traceData = useTraceBuilder()
  const [expanded, setExpanded] = useState(false)
  const [cleared, setCleared] = useState(0)
  const [activeTab, setActiveTab] = useState<TabId>('log')
  const [contentHeight, setContentHeight] = useState(200)
  const scrollRef = useRef<HTMLDivElement>(null)
  const dragRef = useRef<{ startY: number; startH: number } | null>(null)

  const visibleLogs = logs.slice(cleared)

  // Auto-scroll to bottom
  useEffect(() => {
    if (expanded && scrollRef.current && activeTab === 'log') {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight
    }
  }, [visibleLogs.length, expanded, activeTab])

  // 有 trace 資料時自動切到 Trace tab
  useEffect(() => {
    if (traceData && traceData.spans.length > 0) {
      setActiveTab('trace')
    }
  }, [traceData?.spans.length])

  // 有 RAG citations 時自動切到 Sources tab
  useEffect(() => {
    if (ragCitations && ragCitations.length > 0) {
      setActiveTab('sources')
    }
  }, [ragCitations?.length])

  // Drag-to-resize
  const onDragStart = useCallback((e: React.MouseEvent) => {
    e.preventDefault()
    dragRef.current = { startY: e.clientY, startH: contentHeight }
    const onMove = (ev: MouseEvent) => {
      if (!dragRef.current) return
      const delta = dragRef.current.startY - ev.clientY
      setContentHeight(Math.max(80, Math.min(600, dragRef.current.startH + delta)))
    }
    const onUp = () => {
      dragRef.current = null
      document.removeEventListener('mousemove', onMove)
      document.removeEventListener('mouseup', onUp)
    }
    document.addEventListener('mousemove', onMove)
    document.addEventListener('mouseup', onUp)
  }, [contentHeight])

  return (
    <div className="border-t border-border bg-card shrink-0">
      {/* Drag handle */}
      {expanded && (
        <div
          onMouseDown={onDragStart}
          className="h-1 cursor-ns-resize hover:bg-blue-500/30 transition-colors"
        />
      )}
      {/* Header */}
      <button
        onClick={() => setExpanded(!expanded)}
        className="flex w-full items-center justify-between px-3 py-1.5 hover:bg-accent/30 transition-colors cursor-pointer"
      >
        <div className="flex items-center gap-1.5">
          <Terminal size={12} className="text-muted-foreground" />
          <span className="text-[10px] font-semibold text-muted-foreground uppercase tracking-wide">
            {t('console.title')}
          </span>
          {visibleLogs.length > 0 && (
            <span className="rounded-full bg-blue-500/20 px-1.5 text-[9px] text-blue-400 font-medium">
              {visibleLogs.length}
            </span>
          )}
          {stats && stats.durationMs > 0 && (
            <StatsLine
              durationMs={stats.durationMs}
              tokens={stats.totalTokens}
              cost={stats.estimatedCost}
              steps={stats.totalSteps}
              tools={stats.totalToolCalls}
              className="ml-2"
            />
          )}
        </div>
        <div className="flex items-center gap-1">
          {activeTab === 'log' && visibleLogs.length > 0 && (
            <span
              onClick={(e) => { e.stopPropagation(); setCleared(logs.length) }}
              className="text-muted-foreground hover:text-foreground p-0.5 cursor-pointer"
              title={t('console.clear')}
            >
              <Trash2 size={11} />
            </span>
          )}
          {expanded ? <ChevronDown size={13} className="text-muted-foreground" /> : <ChevronUp size={13} className="text-muted-foreground" />}
        </div>
      </button>

      {/* Tabs + Content */}
      {expanded && (
        <div className="border-t border-border/50">
          {/* Tab bar */}
          <div className="flex items-center gap-0 px-2 border-b border-border/30">
            <TabButton
              active={activeTab === 'log'}
              onClick={() => setActiveTab('log')}
              label={t('console.tab.log', 'Log')}
            />
            <TabButton
              active={activeTab === 'trace'}
              onClick={() => setActiveTab('trace')}
              label={t('console.tab.trace', 'Trace')}
              badge={traceData?.spans.length}
            />
            {ragCitations && ragCitations.length > 0 && (
              <TabButton
                active={activeTab === 'sources'}
                onClick={() => setActiveTab('sources')}
                label={t('console.tab.sources', 'Sources')}
                badge={ragCitations.length}
              />
            )}
          </div>

          {/* Tab content */}
          <div style={{ height: `${contentHeight}px` }} className="overflow-y-auto">
            {activeTab === 'log' ? (
              <div
                ref={scrollRef}
                className="h-full overflow-y-auto font-mono text-[10px] leading-relaxed"
              >
                {visibleLogs.length === 0 ? (
                  <p className="px-3 py-3 text-muted-foreground text-center">{t('console.empty')}</p>
                ) : (
                  visibleLogs.map((log, i) => <LogEntry key={i} log={log} />)
                )}
              </div>
            ) : activeTab === 'sources' && ragCitations ? (
              <CitationsPanel citations={ragCitations} expandedQueries={expandedQueries} />
            ) : (
              traceData ? (
                <TraceWaterfall data={traceData} maxHeight="100%" />
              ) : (
                <p className="px-3 py-3 text-muted-foreground text-center text-[10px]">
                  {t('trace.noData', 'No trace data')}
                </p>
              )
            )}
          </div>
        </div>
      )}
    </div>
  )
}

function TabButton({ active, onClick, label, badge }: {
  active: boolean; onClick: () => void; label: string; badge?: number
}) {
  return (
    <button
      onClick={(e) => { e.stopPropagation(); onClick() }}
      className={`px-2.5 py-1 text-[9px] font-medium border-b-2 transition-colors ${
        active
          ? 'border-blue-500 text-blue-400'
          : 'border-transparent text-muted-foreground hover:text-foreground'
      }`}
    >
      {label}
      {badge != null && badge > 0 && (
        <span className="ml-1 text-[8px] text-muted-foreground">({badge})</span>
      )}
    </button>
  )
}

function LogEntry({ log }: { log: ConsoleLog }) {
  const cfg = LEVEL_CONFIG[log.level] ?? LEVEL_CONFIG.info
  return (
    <div className="flex items-start gap-2 px-3 py-0.5 hover:bg-accent/20">
      <span className="text-muted-foreground shrink-0 w-[52px]">{log.ts}</span>
      <span className={`shrink-0 w-3 text-center ${cfg.color}`}>{cfg.icon}</span>
      <span className="text-foreground break-all">{log.message}</span>
    </div>
  )
}

function CitationsPanel({ citations, expandedQueries }: { citations: RagCitation[]; expandedQueries?: string[] }) {
  const [expandedIdx, setExpandedIdx] = useState<number | null>(null)
  return (
    <div className="h-full overflow-y-auto">
      {expandedQueries && expandedQueries.length > 0 && (
        <div className="px-3 py-2 border-b border-border/30 bg-accent/10">
          <div className="text-[9px] text-muted-foreground font-medium mb-0.5">Query Expansion:</div>
          {expandedQueries.map((q, i) => (
            <div key={i} className="text-[9px] text-blue-400/80">· {q}</div>
          ))}
        </div>
      )}
      {citations.map((c, i) => (
        <div
          key={i}
          className="px-3 py-2 border-b border-border/30 hover:bg-accent/20 cursor-pointer transition-colors"
          onClick={() => setExpandedIdx(expandedIdx === i ? null : i)}
        >
          <div className="flex items-center justify-between">
            <span className="text-[9px] text-muted-foreground">
              {c.fileName && <>{c.fileName} · </>}Section {c.chunkIndex + 1}
            </span>
            <span className={`text-[9px] font-mono ${c.score >= 0.02 ? 'text-green-400' : c.score >= 0.01 ? 'text-yellow-400' : 'text-muted-foreground'}`}>
              {c.score.toFixed(4)}
            </span>
          </div>
          <p className={`text-[10px] text-foreground mt-0.5 ${expandedIdx === i ? '' : 'line-clamp-2'}`}>
            {c.content}
          </p>
        </div>
      ))}
    </div>
  )
}
