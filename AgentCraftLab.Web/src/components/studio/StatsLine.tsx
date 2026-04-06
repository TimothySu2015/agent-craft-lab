/**
 * StatsLine — 共用的執行統計顯示元件。
 * AI Build 和 Execute Chat（ConsolePanel）共用。
 */
import { Clock, Coins, DollarSign, Layers, Wrench } from 'lucide-react'
import { formatDuration } from '@/lib/format'

export interface StatsLineProps {
  durationMs?: number
  tokens?: number
  cost?: string | null
  steps?: number
  tools?: number
  model?: string
  className?: string
}

export function StatsLine({ durationMs, tokens, cost, steps, tools, model, className }: StatsLineProps) {
  return (
    <div className={`flex items-center gap-2.5 text-[9px] text-muted-foreground ${className ?? ''}`}>
      {!!steps && steps > 0 && (
        <span className="flex items-center gap-0.5">
          <Layers size={9} /> {steps}
        </span>
      )}
      {!!tools && tools > 0 && (
        <span className="flex items-center gap-0.5">
          <Wrench size={9} /> {tools}
        </span>
      )}
      {!!tokens && tokens > 0 && (
        <span className="flex items-center gap-0.5">
          <Coins size={9} /> ~{tokens.toLocaleString()}
        </span>
      )}
      {cost && (
        <span className="flex items-center gap-0.5">
          <DollarSign size={9} /> {cost}
        </span>
      )}
      {!!durationMs && durationMs > 0 && (
        <span className="flex items-center gap-0.5">
          <Clock size={9} /> {formatDuration(durationMs)}
        </span>
      )}
      {model && (
        <span className="opacity-60">{model}</span>
      )}
    </div>
  )
}
