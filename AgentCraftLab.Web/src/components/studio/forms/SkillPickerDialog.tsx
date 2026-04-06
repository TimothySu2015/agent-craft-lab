/**
 * SkillPickerDialog — 從 /api/skills 載入內建 + 自訂 Skills，checkbox 選取。
 */
import { useState, useEffect, useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { X, Search, Check, Sparkles, Zap } from 'lucide-react'
import { api } from '@/lib/api'
import { notify } from '@/lib/notify'

interface SkillDef {
  id: string
  name: string
  description: string
  category: string
  isBuiltin?: boolean
}

interface Props {
  open: boolean
  selected: string[]
  onClose: () => void
  onApply: (skills: string[]) => void
}

export function SkillPickerDialog({ open, selected, onClose, onApply }: Props) {
  const { t } = useTranslation('studio')
  const { t: tn } = useTranslation('notifications')
  const [builtin, setBuiltin] = useState<SkillDef[]>([])
  const [custom, setCustom] = useState<SkillDef[]>([])
  const [loading, setLoading] = useState(true)
  const [search, setSearch] = useState('')
  const [checked, setChecked] = useState<Set<string>>(new Set(selected))

  useEffect(() => {
    if (!open) return
    setChecked(new Set(selected))
    setLoading(true)
    api.skills.list()
      .then((data) => {
        setBuiltin((data.builtin ?? []).map((s: any) => ({ ...s, isBuiltin: true })))
        setCustom(data.custom ?? [])
      })
      .catch((err) => { console.error('Failed to load skills:', err); notify.error(tn('loadFailed.skills')) })
      .finally(() => setLoading(false))
  }, [open, selected])

  const allSkills = useMemo(() => [...custom, ...builtin], [custom, builtin])

  const filtered = useMemo(() => {
    if (!search) return allSkills
    const q = search.toLowerCase()
    return allSkills.filter((s) =>
      s.name.toLowerCase().includes(q) ||
      s.id.toLowerCase().includes(q) ||
      s.description?.toLowerCase().includes(q)
    )
  }, [allSkills, search])

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
        className="w-[500px] max-h-[70vh] rounded-lg border border-border bg-card shadow-xl flex flex-col"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between border-b border-border px-4 py-3 shrink-0">
          <h2 className="text-sm font-semibold text-foreground">{t('skillPicker.title')}</h2>
          <button onClick={onClose} className="text-muted-foreground hover:text-foreground cursor-pointer"><X size={16} /></button>
        </div>

        <div className="px-4 pt-3 shrink-0">
          <div className="relative">
            <Search size={13} className="absolute left-2.5 top-1/2 -translate-y-1/2 text-muted-foreground" />
            <input className="field-input pl-8 text-xs" value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t('skillPicker.search')} />
          </div>
        </div>

        <div className="flex-1 overflow-y-auto px-4 py-3">
          {loading ? (
            <p className="text-xs text-muted-foreground text-center py-8">{t('loading', { ns: 'common' })}</p>
          ) : filtered.length === 0 ? (
            <p className="text-xs text-muted-foreground text-center py-8">{t('skillPicker.empty')}</p>
          ) : (
            <div className="grid grid-cols-1 gap-2">
              {filtered.map((skill) => {
                const isChecked = checked.has(skill.id)
                const Icon = skill.isBuiltin ? Zap : Sparkles
                const accent = skill.isBuiltin ? 'blue' : 'violet'
                return (
                  <button
                    key={skill.id}
                    onClick={() => toggle(skill.id)}
                    className={`flex items-start gap-2.5 rounded-md border p-2.5 text-left transition-colors cursor-pointer ${
                      isChecked
                        ? `border-${accent}-500/50 bg-${accent}-500/5`
                        : 'border-border hover:bg-accent/30'
                    }`}
                  >
                    <div className={`mt-0.5 flex h-4 w-4 shrink-0 items-center justify-center rounded border ${
                      isChecked ? `border-${accent}-500 bg-${accent}-500` : 'border-muted-foreground'
                    }`}>
                      {isChecked && <Check size={10} className="text-white" />}
                    </div>
                    <Icon size={13} className={`shrink-0 mt-0.5 text-${accent}-400`} />
                    <div className="min-w-0">
                      <div className="text-[11px] font-medium text-foreground">{skill.name}</div>
                      {skill.description && <div className="text-[9px] text-muted-foreground line-clamp-1">{skill.description}</div>}
                      {skill.category && <span className={`inline-block mt-0.5 rounded bg-${accent}-500/10 px-1 py-0.5 text-[8px] text-${accent}-400`}>{skill.category}</span>}
                    </div>
                  </button>
                )
              })}
            </div>
          )}
        </div>

        <div className="flex items-center justify-between border-t border-border px-4 py-2.5 shrink-0">
          <span className="text-[10px] text-muted-foreground">{checked.size} {t('skillPicker.selected')}</span>
          <div className="flex gap-2">
            <button onClick={onClose} className="rounded-md border border-border bg-secondary px-3 py-1.5 text-xs text-muted-foreground cursor-pointer">{t('cancel', { ns: 'common' })}</button>
            <button onClick={() => { onApply([...checked]); onClose() }} className="rounded-md bg-violet-600 px-4 py-1.5 text-xs font-semibold text-white hover:bg-violet-500 cursor-pointer">{t('skillPicker.apply')}</button>
          </div>
        </div>
      </div>
    </div>
  )
}
