import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { X, Search, Trash2, Bookmark } from 'lucide-react'
import * as Icons from 'lucide-react'
import { BUILTIN_TEMPLATES, TEMPLATE_CATEGORIES, type TemplateInfo } from '@/lib/templates'
import { useCustomTemplatesStore, type CustomTemplate } from '@/stores/custom-templates-store'

interface Props {
  open: boolean;
  onClose: () => void;
  onSelect: (template: TemplateInfo) => void;
  onSelectCustom?: (template: CustomTemplate) => void;
}

export function TemplatesDialog({ open, onClose, onSelect, onSelectCustom }: Props) {
  const { t } = useTranslation('studio')
  const [search, setSearch] = useState('')
  const [category, setCategory] = useState('All')
  const customTemplates = useCustomTemplatesStore((s) => s.templates)
  const removeCustom = useCustomTemplatesStore((s) => s.removeTemplate)

  if (!open) return null

  const filtered = BUILTIN_TEMPLATES.filter((tpl) => {
    if (category !== 'All' && category !== 'Custom' && tpl.category !== category) return false
    if (category === 'Custom') return false
    if (search) {
      const q = search.toLowerCase()
      return tpl.name.toLowerCase().includes(q) ||
        tpl.shortDescription.toLowerCase().includes(q) ||
        tpl.tags.some((tag) => tag.includes(q))
    }
    return true
  })

  const filteredCustom = customTemplates.filter((tpl) => {
    if (category !== 'All' && category !== 'Custom') return false
    if (search) {
      return tpl.name.toLowerCase().includes(search.toLowerCase())
    }
    return true
  })

  const allCategories = ['Custom', ...TEMPLATE_CATEGORIES]

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
      <div className="w-[700px] max-h-[80vh] rounded-lg border border-border bg-card p-5 shadow-xl flex flex-col">
        {/* Header */}
        <div className="flex items-center justify-between mb-4 shrink-0">
          <h2 className="text-sm font-semibold text-foreground">{t('templates')}</h2>
          <button onClick={onClose} className="text-muted-foreground hover:text-foreground cursor-pointer">
            <X size={16} />
          </button>
        </div>

        {/* Search + Category filter */}
        <div className="flex gap-2 mb-4 shrink-0">
          <div className="relative flex-1">
            <Search size={14} className="absolute left-2.5 top-1/2 -translate-y-1/2 text-muted-foreground" />
            <input
              className="field-input pl-8 text-xs"
              placeholder="Search templates..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
          </div>
          <select
            className="field-input w-auto text-xs"
            value={category}
            onChange={(e) => setCategory(e.target.value)}
          >
            <option value="All">All</option>
            {allCategories.map((c) => <option key={c} value={c}>{c}</option>)}
          </select>
        </div>

        {/* Template grid */}
        <div className="flex-1 overflow-y-auto">
          {/* Custom templates */}
          {filteredCustom.length > 0 && (
            <>
              <div className="flex items-center gap-1.5 mb-2">
                <Bookmark size={12} className="text-violet-400" />
                <span className="text-[10px] font-semibold text-violet-400 uppercase tracking-wide">{t('dialog.customTemplates')}</span>
              </div>
              <div className="grid grid-cols-2 gap-3 mb-4">
                {filteredCustom.map((tpl) => (
                  <div
                    key={tpl.id}
                    className="flex flex-col items-start rounded-lg border border-violet-500/30 p-3 text-left hover:bg-violet-500/5 transition-colors group"
                  >
                    <button
                      onClick={() => { onSelectCustom?.(tpl); onClose() }}
                      className="flex-1 w-full text-left cursor-pointer"
                    >
                      <div className="flex items-center gap-2 mb-1">
                        <Bookmark size={13} className="text-violet-400 shrink-0" />
                        <span className="text-xs font-semibold text-foreground">{tpl.name}</span>
                      </div>
                      {tpl.description && (
                        <p className="text-[10px] text-muted-foreground line-clamp-2 mb-1">{tpl.description}</p>
                      )}
                      <span className="text-[8px] text-muted-foreground">{new Date(tpl.createdAt).toLocaleDateString()}</span>
                    </button>
                    <button
                      onClick={() => removeCustom(tpl.id)}
                      className="self-end mt-1 text-muted-foreground hover:text-red-400 opacity-0 group-hover:opacity-100 transition-opacity cursor-pointer"
                      title="Delete"
                    >
                      <Trash2 size={12} />
                    </button>
                  </div>
                ))}
              </div>
            </>
          )}

          {/* Built-in templates */}
          <div className="grid grid-cols-2 gap-3">
            {filtered.map((tpl) => {
              const IconComp = (Icons as Record<string, Icons.LucideIcon>)[tpl.icon] ?? Icons.Sparkles
              return (
                <button
                  key={tpl.id}
                  onClick={() => { onSelect(tpl); onClose() }}
                  className="flex flex-col items-start rounded-lg border border-border p-3 text-left hover:border-primary/40 hover:bg-secondary/30 transition-colors cursor-pointer"
                >
                  <div className="flex items-center gap-2 mb-1.5">
                    <IconComp size={15} className="text-primary shrink-0" />
                    <span className="text-xs font-semibold text-foreground">{tpl.name}</span>
                  </div>
                  <p className="text-[10px] text-muted-foreground line-clamp-2 mb-2">{tpl.shortDescription}</p>
                  <div className="flex flex-wrap gap-1">
                    {tpl.tags.slice(0, 3).map((tag) => (
                      <span key={tag} className="rounded bg-secondary px-1.5 py-0.5 text-[8px] text-muted-foreground">{tag}</span>
                    ))}
                    <span className="rounded bg-primary/10 px-1.5 py-0.5 text-[8px] text-primary">{tpl.category}</span>
                  </div>
                </button>
              )
            })}
          </div>
          {filtered.length === 0 && filteredCustom.length === 0 && (
            <p className="text-xs text-muted-foreground text-center py-8">No templates match your search.</p>
          )}
        </div>
      </div>
    </div>
  )
}
