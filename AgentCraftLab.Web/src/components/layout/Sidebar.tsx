import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useLocation, Link } from 'react-router-dom'
import {
  Sparkles, BookOpen, Rocket, Factory,
  Key, FlaskConical, BarChart3, Zap, Settings, Clock,
  PanelLeftClose, PanelLeftOpen,
} from 'lucide-react'
import { cn } from '@/lib/utils'

const navSections = [
  {
    label: '',
    items: [
      { path: '/', icon: Sparkles, labelKey: 'nav.studio' },
    ],
  },
  {
    labelKey: 'settings',
    items: [
      { path: '/settings', icon: Settings, labelKey: 'nav.settings' },
    ],
  },
  {
    labelKey: 'nav.knowledgeBase',
    items: [
      { path: '/knowledge-bases', icon: BookOpen, labelKey: 'nav.knowledgeBase' },
      { path: '/doc-refinery', icon: Factory, labelKey: 'nav.docRefinery' },
      { path: '/skills', icon: Zap, labelKey: 'nav.skills' },
    ],
  },
  {
    labelKey: 'nav.services',
    items: [
      { path: '/published-services', icon: Rocket, labelKey: 'nav.published' },
      { path: '/api-keys', icon: Key, labelKey: 'nav.apiKeys' },
      { path: '/service-tester', icon: FlaskConical, labelKey: 'nav.tester' },
      { path: '/request-logs', icon: BarChart3, labelKey: 'nav.logs' },
      { path: '/schedules', icon: Clock, labelKey: 'nav.schedules' },
    ],
  },
]

export function Sidebar() {
  const { t } = useTranslation('common')
  const location = useLocation()
  const [collapsed, setCollapsed] = useState(false)

  return (
    <aside className={cn(
      'flex flex-col border-r border-border bg-sidebar shrink-0 transition-all duration-200',
      collapsed ? 'w-12' : 'w-[220px]',
    )}>
      {/* Brand */}
      <div className="flex items-center justify-between border-b border-border px-3 h-[41px]">
        {!collapsed && <span className="text-sm font-bold text-foreground truncate">AgentCraftLab</span>}
        <button
          onClick={() => setCollapsed(!collapsed)}
          className="text-muted-foreground hover:text-foreground cursor-pointer shrink-0"
          title={collapsed ? 'Expand' : 'Collapse'}
        >
          {collapsed ? <PanelLeftOpen size={16} /> : <PanelLeftClose size={16} />}
        </button>
      </div>

      {/* Nav */}
      <nav className="flex-1 overflow-y-auto px-1.5 py-2">
        {navSections.map((section, si) => (
          <div key={si} className="mb-1">
            {!collapsed && (section.labelKey || section.label) && (
              <div className="px-2 pb-1 pt-3 text-[10px] font-semibold uppercase tracking-wider text-muted-foreground">
                {section.labelKey ? t(section.labelKey) : section.label}
              </div>
            )}
            {collapsed && si > 0 && <div className="my-1.5 border-t border-border/50" />}
            {section.items.map((item) => {
              const isActive = location.pathname === item.path
              return (
                <Link
                  key={item.path}
                  to={item.path}
                  title={collapsed ? t(item.labelKey) : undefined}
                  className={cn(
                    'flex items-center rounded-md transition-colors',
                    'hover:bg-accent hover:text-foreground',
                    isActive && 'bg-sidebar-accent text-foreground font-medium',
                    collapsed
                      ? 'justify-center px-0 py-2 my-0.5'
                      : 'gap-2.5 px-2.5 py-1.5 text-[13px] text-muted-foreground',
                  )}
                >
                  <item.icon
                    size={collapsed ? 20 : 18}
                    className={cn(
                      'shrink-0',
                      isActive ? 'text-sidebar-primary' : 'text-muted-foreground',
                    )}
                  />
                  {!collapsed && t(item.labelKey)}
                </Link>
              )
            })}
          </div>
        ))}
      </nav>
    </aside>
  )
}
