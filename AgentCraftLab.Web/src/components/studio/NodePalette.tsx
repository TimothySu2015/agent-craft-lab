import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  Bot, Database, GitBranch, RefreshCw, Route,
  Globe, User, Code, Repeat, Columns3, Globe2, Brain,
  PanelLeftClose, PanelLeftOpen,
} from 'lucide-react'
import { cn } from '@/lib/utils'

const nodeGroups = [
  {
    labelKey: 'palette.nodes',
    items: [
      { type: 'agent', labelKey: 'node.agent', icon: Bot, color: 'text-blue-400' },
      { type: 'rag', labelKey: 'node.rag', icon: Database, color: 'text-violet-400' },
      { type: 'condition', labelKey: 'node.condition', icon: GitBranch, color: 'text-amber-400' },
      { type: 'loop', labelKey: 'node.loop', icon: RefreshCw, color: 'text-amber-400' },
      { type: 'router', labelKey: 'node.router', icon: Route, color: 'text-amber-400' },
      { type: 'human', labelKey: 'node.human', icon: User, color: 'text-pink-400' },
      { type: 'code', labelKey: 'node.code', icon: Code, color: 'text-teal-400' },
      { type: 'iteration', labelKey: 'node.iteration', icon: Repeat, color: 'text-teal-400' },
      { type: 'parallel', labelKey: 'node.parallel', icon: Columns3, color: 'text-cyan-400' },
      // { type: 'autonomous', labelKey: 'node.autonomous', icon: Brain, color: 'text-green-400' }, // TODO: re-enable when autonomous node is ready for public use
    ],
  },
  {
    labelKey: 'palette.integrations',
    items: [
      { type: 'a2a-agent', labelKey: 'node.a2a', icon: Globe, color: 'text-purple-400' },
      { type: 'http-request', labelKey: 'node.http', icon: Globe2, color: 'text-orange-400' },
    ],
  },
]

export function NodePalette() {
  const { t } = useTranslation('studio')
  const [collapsed, setCollapsed] = useState(false)

  const onDragStart = (event: React.DragEvent, nodeType: string) => {
    event.dataTransfer.setData('application/reactflow', nodeType)
    event.dataTransfer.effectAllowed = 'move'
  }

  return (
    <aside className={cn(
      'shrink-0 overflow-y-auto border-r border-border bg-card transition-all duration-200 flex flex-col',
      collapsed ? 'w-11' : 'w-[180px]',
    )}>
      {/* Toggle */}
      <div className={cn('flex items-center shrink-0 border-b border-border/50', collapsed ? 'justify-center py-2' : 'justify-between px-2.5 py-1.5')}>
        {!collapsed && <span className="text-[10px] font-semibold uppercase tracking-wider text-muted-foreground">{t('palette.nodes')}</span>}
        <button onClick={() => setCollapsed(!collapsed)} className="text-muted-foreground hover:text-foreground cursor-pointer">
          {collapsed ? <PanelLeftOpen size={14} /> : <PanelLeftClose size={14} />}
        </button>
      </div>

      {/* Items */}
      <div className={cn('flex-1 overflow-y-auto', collapsed ? 'px-1 py-1.5' : 'p-2.5')}>
        {nodeGroups.map((group, gi) => (
          <div key={gi} className={cn(gi > 0 && (collapsed ? 'mt-1 pt-1 border-t border-border/50' : 'mt-3'))}>
            {!collapsed && gi > 0 && (
              <h3 className="mb-1.5 text-[10px] font-semibold uppercase tracking-wider text-muted-foreground">
                {t(group.labelKey)}
              </h3>
            )}
            {group.items.map((item) => (
              <div
                key={item.type}
                draggable
                onDragStart={(e) => onDragStart(e, item.type)}
                title={collapsed ? t(item.labelKey) : undefined}
                className={cn(
                  'flex items-center rounded-md mb-0.5 cursor-grab hover:bg-secondary hover:text-foreground transition-colors',
                  collapsed
                    ? 'justify-center px-0 py-1.5'
                    : 'gap-2 px-2 py-1.5 text-[11px] text-muted-foreground',
                )}
              >
                <item.icon size={collapsed ? 18 : 16} className={cn('shrink-0', item.color)} />
                {!collapsed && t(item.labelKey)}
              </div>
            ))}
          </div>
        ))}
      </div>
    </aside>
  )
}
