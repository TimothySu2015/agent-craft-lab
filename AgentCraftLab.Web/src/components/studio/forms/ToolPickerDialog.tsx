/**
 * ToolPickerDialog — Resource Manager Modal。
 * 從 /api/tools 載入內建工具，以 checkbox 卡片選取，回寫到 node.tools。
 */
import { useState, useEffect, useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { X, Search, Check } from 'lucide-react'
import { api } from '@/lib/api'
import { notify } from '@/lib/notify'

interface ToolDef {
  id: string
  name: string
  description: string
  category: string
  icon: string
}

interface Props {
  open: boolean
  selected: string[]
  onClose: () => void
  onApply: (tools: string[]) => void
}

export function ToolPickerDialog({ open, selected, onClose, onApply }: Props) {
  const { t } = useTranslation('studio')
  const { t: tn } = useTranslation('notifications')
  const [tools, setTools] = useState<ToolDef[]>([])
  const [loading, setLoading] = useState(true)
  const [search, setSearch] = useState('')
  const [checked, setChecked] = useState<Set<string>>(new Set(selected))

  useEffect(() => {
    if (!open) return
    setChecked(new Set(selected))
    setLoading(true)
    api.tools.list()
      .then(setTools)
      .catch((err) => { console.error('Failed to load tools:', err); notify.error(tn('loadFailed.tools')) })
      .finally(() => setLoading(false))
  }, [open, selected])

  const categories = useMemo(() => [...new Set(tools.map((t) => t.category))], [tools])

  const filtered = useMemo(() => {
    if (!search) return tools
    const q = search.toLowerCase()
    return tools.filter((t) =>
      t.name.toLowerCase().includes(q) ||
      t.id.toLowerCase().includes(q) ||
      t.description.toLowerCase().includes(q)
    )
  }, [tools, search])

  const toggle = (id: string) => {
    setChecked((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  if (!open) return null

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" onClick={onClose}>
      <div
        className="w-[600px] max-h-[70vh] rounded-lg border border-border bg-card shadow-xl flex flex-col"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div className="flex items-center justify-between border-b border-border px-4 py-3 shrink-0">
          <h2 className="text-sm font-semibold text-foreground">{t('toolPicker.title')}</h2>
          <button onClick={onClose} className="text-muted-foreground hover:text-foreground cursor-pointer">
            <X size={16} />
          </button>
        </div>

        {/* Search */}
        <div className="px-4 pt-3 shrink-0">
          <div className="relative">
            <Search size={13} className="absolute left-2.5 top-1/2 -translate-y-1/2 text-muted-foreground" />
            <input
              className="field-input pl-8 text-xs"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder={t('toolPicker.search')}
            />
          </div>
        </div>

        {/* Tool grid */}
        <div className="flex-1 overflow-y-auto px-4 py-3">
          {loading ? (
            <p className="text-xs text-muted-foreground text-center py-8">{t('loading', { ns: 'common' })}</p>
          ) : (
            categories.map((cat) => {
              const catTools = filtered.filter((t) => t.category === cat)
              if (catTools.length === 0) return null
              return (
                <div key={cat} className="mb-4">
                  <h3 className="text-[10px] font-semibold text-muted-foreground uppercase tracking-wide mb-2">{cat}</h3>
                  <div className="grid grid-cols-2 gap-2">
                    {catTools.map((tool) => {
                      const isChecked = checked.has(tool.id)
                      return (
                        <button
                          key={tool.id}
                          onClick={() => toggle(tool.id)}
                          className={`flex items-start gap-2 rounded-md border p-2.5 text-left transition-colors cursor-pointer ${
                            isChecked
                              ? 'border-blue-500/50 bg-blue-500/5'
                              : 'border-border hover:bg-accent/30'
                          }`}
                        >
                          <div className={`mt-0.5 flex h-4 w-4 shrink-0 items-center justify-center rounded border ${
                            isChecked ? 'border-blue-500 bg-blue-500' : 'border-muted-foreground'
                          }`}>
                            {isChecked && <Check size={10} className="text-white" />}
                          </div>
                          <div className="min-w-0">
                            <div className="text-[11px] font-medium text-foreground">{tool.name}</div>
                            <div className="text-[9px] text-muted-foreground line-clamp-2">{tool.description}</div>
                          </div>
                        </button>
                      )
                    })}
                  </div>
                </div>
              )
            })
          )}
        </div>

        {/* Footer */}
        <div className="flex items-center justify-between border-t border-border px-4 py-2.5 shrink-0">
          <span className="text-[10px] text-muted-foreground">
            {checked.size} {t('toolPicker.selected')}
          </span>
          <div className="flex gap-2">
            <button
              onClick={onClose}
              className="rounded-md border border-border bg-secondary px-3 py-1.5 text-xs text-muted-foreground hover:text-foreground cursor-pointer"
            >
              {t('cancel', { ns: 'common' })}
            </button>
            <button
              onClick={() => { onApply([...checked]); onClose() }}
              className="rounded-md bg-blue-600 px-4 py-1.5 text-xs font-semibold text-white hover:bg-blue-500 cursor-pointer"
            >
              {t('toolPicker.apply')}
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}
