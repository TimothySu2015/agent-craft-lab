/**
 * useTraceBuilder — 從 coagent-store 的 traceSpans 組裝 TraceData。
 * traceSpans 透過 STATE_SNAPSHOT 從後端搭載，和 recentLogs 同一個消費模式。
 */
import { useMemo } from 'react'
import { useCoAgentStore } from '@/stores/coagent-store'
import type { TraceSpan, TraceData } from '@/stores/agent-state'

/** 把扁平 span 列表依 parentId 組裝成樹狀結構 */
function buildTree(spans: TraceSpan[]): TraceSpan[] {
  const map = new Map<string, TraceSpan & { children?: TraceSpan[] }>()
  const roots: TraceSpan[] = []

  for (const span of spans) {
    map.set(span.id, { ...span, children: [] })
  }

  for (const span of map.values()) {
    if (span.parentId && map.has(span.parentId)) {
      map.get(span.parentId)!.children!.push(span)
    } else {
      roots.push(span)
    }
  }

  // 排序：按 startMs
  const sortChildren = (nodes: TraceSpan[]) => {
    nodes.sort((a, b) => a.startMs - b.startMs)
    for (const n of nodes) {
      if ((n as any).children?.length) sortChildren((n as any).children)
    }
  }
  sortChildren(roots)

  return roots
}

export function useTraceBuilder(): TraceData | null {
  const traceSpans = useCoAgentStore((s) => s.state?.traceSpans)

  return useMemo(() => {
    if (!traceSpans || traceSpans.length === 0) return null

    const totalMs = Math.max(...traceSpans.map(s => s.endMs)) - Math.min(...traceSpans.map(s => s.startMs))
    const totalTokens = traceSpans
      .filter(s => s.tokens != null)
      .reduce((sum, s) => sum + (s.tokens ?? 0), 0)
    const hasError = traceSpans.some(s => s.status === 'error')
    const hasRunning = traceSpans.some(s => s.status === 'running')

    return {
      traceId: '',
      totalMs,
      totalTokens,
      totalCost: '',
      status: hasRunning ? 'running' : hasError ? 'error' : 'completed',
      spans: buildTree(traceSpans),
    }
  }, [traceSpans])
}
