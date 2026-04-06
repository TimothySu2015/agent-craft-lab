/**
 * NodeShell — 所有 15 種節點共用的外殼元件。
 * 負責：邊框顏色、icon、input/output handles、選中狀態。
 * 各節點只需提供 body 內容。
 * Handle 位置跟隨 layoutDirection（LR: Left/Right, TB: Top/Bottom）。
 */
import { Handle, Position, useNodeId, useUpdateNodeInternals } from '@xyflow/react'
import type { LucideIcon } from 'lucide-react'
import { useEffect } from 'react'
import { cn } from '@/lib/utils'
import { NODE_COLORS } from '../registry'
import { useCoAgentStore } from '@/stores/coagent-store'
import { useWorkflowStore } from '@/stores/workflow-store'
import type { ReactNode } from 'react'

interface NodeShellProps {
  color: string;
  icon: LucideIcon;
  title: string;
  subtitle?: string;
  inputs: number;
  outputs: number;
  selected?: boolean;
  children?: ReactNode;
}

export function NodeShell({ color, icon: Icon, title, subtitle, inputs, outputs, selected, children }: NodeShellProps) {
  const colors = NODE_COLORS[color] ?? NODE_COLORS.blue
  const execStatus = useCoAgentStore((s) => s.state?.nodeStates?.[title])
  const dir = useWorkflowStore((s) => s.layoutDirection)
  const isTB = dir === 'TB'
  const inputPos = isTB ? Position.Top : Position.Left
  const outputPos = isTB ? Position.Bottom : Position.Right

  // 告知 React Flow 重新計算 Handle 位置
  const nid = useNodeId()
  const updateNodeInternals = useUpdateNodeInternals()
  useEffect(() => {
    if (nid) updateNodeInternals(nid)
  }, [dir, nid, updateNodeInternals])

  return (
    <div
      className={cn(
        'min-w-[170px] max-w-[280px] rounded-lg border-[1.5px] bg-card shadow-lg transition-all',
        colors.border,
        selected && 'ring-2 ring-blue-500/40 border-blue-500',
        execStatus === 'executing' && 'ring-2 ring-amber-400/60 border-amber-400',
        execStatus === 'completed' && 'ring-2 ring-green-400/50 border-green-400',
        execStatus === 'cancelled' && 'ring-2 ring-gray-400/40 border-gray-400 opacity-50',
        execStatus === 'debug-paused' && 'ring-2 ring-orange-400/60 border-orange-400',
      )}
    >
      {/* Input handle */}
      {inputs > 0 && (
        <Handle
          type="target"
          position={inputPos}
          className="!w-2.5 !h-2.5 !border-2 !border-blue-500 !bg-background"
        />
      )}

      {/* Header */}
      <div className="flex items-center gap-2 px-2.5 pt-2 pb-1">
        <div className={cn('flex h-6 w-6 items-center justify-center rounded relative', colors.iconBg)}>
          <Icon size={13} className={colors.iconText} />
          {execStatus === 'executing' && (
            <span className="absolute -top-0.5 -right-0.5 h-2 w-2 rounded-full bg-amber-400 animate-pulse" />
          )}
          {execStatus === 'completed' && (
            <span className="absolute -top-0.5 -right-0.5 h-2 w-2 rounded-full bg-green-400" />
          )}
          {execStatus === 'cancelled' && (
            <span className="absolute -top-0.5 -right-0.5 h-2 w-2 rounded-full bg-gray-400" />
          )}
          {execStatus === 'debug-paused' && (
            <span className="absolute -top-0.5 -right-0.5 h-2 w-2 rounded-full bg-orange-400 animate-pulse" />
          )}
        </div>
        <div className="min-w-0">
          <div className="text-[11px] font-semibold text-foreground truncate">{title}</div>
          {subtitle && <div className="text-[9px] text-muted-foreground truncate">{subtitle}</div>}
        </div>
      </div>

      {/* Body (provided by each node) */}
      {children && (
        <div className="px-2.5 pb-2 text-[9px] text-muted-foreground overflow-hidden">
          {children}
        </div>
      )}

      {/* Output handles */}
      {outputs === 1 && (
        <Handle
          type="source"
          position={outputPos}
          className="!w-2.5 !h-2.5 !border-2 !border-green-500 !bg-background"
        />
      )}
      {outputs >= 2 && Array.from({ length: outputs }, (_, i) => {
        const pct = ((i + 1) / (outputs + 1)) * 100
        const isLast = i === outputs - 1
        const borderColor = isLast ? '!border-red-500' : '!border-green-500'
        return (
          <Handle
            key={`output_${i + 1}_${dir}`}
            type="source"
            position={outputPos}
            id={`output_${i + 1}`}
            className={`!w-2.5 !h-2.5 !border-2 ${borderColor} !bg-background`}
            style={isTB
              ? { left: `${pct}%` }
              : { top: `${pct}%` }
            }
          />
        )
      })}
    </div>
  )
}
