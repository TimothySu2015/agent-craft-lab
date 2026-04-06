/**
 * GroupNodeShell — 控制流程群組節點的共用外殼。
 * 渲染一個半透明容器框，子節點透過 React Flow parentId 渲染在內部。
 * 不渲染 Handle（Handle 留在原本的控制節點上）。
 * 支援拖拉邊框調整大小（NodeResizer）。
 */
import { NodeResizer } from '@xyflow/react'
import type { LucideIcon } from 'lucide-react'
import { cn } from '@/lib/utils'

interface GroupNodeShellProps {
  label: string
  icon: LucideIcon
  borderClass: string
  bgClass: string
  lineColor?: string
  children?: React.ReactNode
}

export function GroupNodeShell({ label, icon: Icon, borderClass, bgClass, lineColor = '#6b7280', children }: GroupNodeShellProps) {
  return (
    <>
      <NodeResizer
        isVisible
        minWidth={200}
        minHeight={120}
        lineStyle={{ borderColor: lineColor, borderWidth: 1, opacity: 0.3 }}
        handleStyle={{
          width: 8,
          height: 8,
          borderRadius: 2,
          backgroundColor: lineColor,
          opacity: 0.5,
          borderColor: lineColor,
        }}
      />
      <div
        className={cn(
          'rounded-xl p-3 w-full h-full',
          'border-2',
          borderClass,
          bgClass,
        )}
      >
        <div className="flex items-center gap-1.5 mb-2">
          <Icon size={12} className="text-muted-foreground" />
          <span className="text-[10px] font-semibold text-muted-foreground uppercase tracking-wide">
            {label}
          </span>
        </div>
        {children}
      </div>
    </>
  )
}
