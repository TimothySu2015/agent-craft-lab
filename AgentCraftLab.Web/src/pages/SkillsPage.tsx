/**
 * Skill Manager 頁面 — 列出內建 + 自訂 Skills，支援 CRUD + 內建 Skill 詳情檢視。
 */
import { useState, useEffect, useCallback } from 'react'
import { useTranslation } from 'react-i18next'
import { Plus, Trash2, Edit3, Sparkles, X, Download, Upload } from 'lucide-react'
import { useConfirmDialog } from '@/components/shared/ConfirmDialog'
import { api } from '@/lib/api'
import { notify } from '@/lib/notify'

interface BuiltinSkill {
  id: string; name: string; description: string; instructions: string; category: string; icon: string; tools: string[]; isBuiltin: true
}
interface CustomSkill {
  id: string; name: string; description: string; category: string; icon: string; instructions: string; tools: string[]
}

type AnySkill = (BuiltinSkill | CustomSkill) & { isBuiltin?: boolean }

export function SkillsPage() {
  const { t } = useTranslation('studio')
  const { t: tn } = useTranslation('notifications')
  const { confirm, confirmDialog } = useConfirmDialog()
  const [builtin, setBuiltin] = useState<BuiltinSkill[]>([])
  const [custom, setCustom] = useState<CustomSkill[]>([])
  const [loading, setLoading] = useState(true)
  const [showForm, setShowForm] = useState(false)
  const [editing, setEditing] = useState<CustomSkill | null>(null)
  const [viewing, setViewing] = useState<AnySkill | null>(null)

  const fetchSkills = useCallback(() => {
    setLoading(true)
    api.skills.list()
      .then((data) => { setBuiltin(data.builtin ?? []); setCustom(data.custom ?? []) })
      .catch((err) => console.error('Failed to load skills:', err))
      .finally(() => setLoading(false))
  }, [])

  useEffect(() => { fetchSkills() }, [fetchSkills])

  const handleDelete = async (id: string) => {
    if (!await confirm(t('skills.confirmDelete'))) return
    try {
      await api.skills.delete(id)
    } catch (err) { console.error('Failed to delete skill:', err) }
    fetchSkills()
  }

  const handleExport = async (id: string, name: string) => {
    try {
      const md = await api.skills.exportMd(id)
      const blob = new Blob([md], { type: 'text/markdown' })
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = `${name}.skill.md`
      a.click()
      URL.revokeObjectURL(url)
    } catch (err) { console.error('Failed to export skill:', err); notify.error(tn('exportFailed.skill')) }
  }

  const handleImport = () => {
    const input = document.createElement('input')
    input.type = 'file'
    input.accept = '.md,.skill.md'
    input.onchange = async () => {
      const file = input.files?.[0]
      if (!file) return
      try {
        await api.skills.importMd(file)
        fetchSkills()
      } catch (err) { console.error('Failed to import skill:', err); notify.error(tn('importFailed.skill'), { description: (err as any)?.message }) }
    }
    input.click()
  }

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      <div className="flex items-center justify-between border-b border-border bg-card px-5 shrink-0 h-[41px]">
        <h1 className="text-sm font-semibold text-foreground">{t('skills.title')}</h1>
        <div className="flex gap-2">
          <button
            onClick={handleImport}
            className="flex items-center gap-1 rounded-md border border-border px-3 py-1.5 text-xs text-muted-foreground hover:text-foreground transition-colors cursor-pointer"
          >
            <Upload size={13} /> {t('skills.import')}
          </button>
          <button
            onClick={() => { setEditing(null); setShowForm(true) }}
            className="flex items-center gap-1 rounded-md bg-blue-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-blue-500 transition-colors cursor-pointer"
          >
            <Plus size={13} /> {t('skills.create')}
          </button>
        </div>
      </div>

      <div className="flex flex-1 overflow-hidden">
        {/* ─── Left: Skill List ─── */}
        <div className={`${viewing ? 'w-[300px]' : 'flex-1'} shrink-0 overflow-y-auto p-5 transition-all`}>
          {loading ? (
            <p className="text-xs text-muted-foreground text-center py-8">{t('loading', { ns: 'common' })}</p>
          ) : (
            <>
              {/* Custom Skills */}
              {custom.length > 0 && (
                <div className="mb-6">
                  <h2 className="text-xs font-semibold text-foreground mb-3">{t('skills.custom')}</h2>
                  <div className={`grid gap-3 ${viewing ? 'grid-cols-1' : 'grid-cols-1 md:grid-cols-2 lg:grid-cols-3'}`}>
                    {custom.map((s) => (
                      <div
                        key={s.id}
                        onClick={() => setViewing(s)}
                        className={`rounded-lg border p-3 group transition-colors cursor-pointer ${viewing?.id === s.id ? 'border-violet-500/50 bg-violet-500/5' : 'border-border hover:border-blue-500/30'}`}
                      >
                        <div className="flex items-start justify-between mb-1">
                          <div className="flex items-center gap-1.5">
                            <Sparkles size={13} className="text-violet-400" />
                            <span className="text-xs font-semibold text-foreground">{s.name}</span>
                          </div>
                          <div className="flex gap-1 opacity-0 group-hover:opacity-100 transition-opacity">
                            <button onClick={(e) => { e.stopPropagation(); handleExport(s.id, s.name) }} className="text-muted-foreground hover:text-foreground cursor-pointer" title="Export SKILL.md"><Download size={12} /></button>
                            <button onClick={(e) => { e.stopPropagation(); setEditing(s); setShowForm(true) }} className="text-muted-foreground hover:text-foreground cursor-pointer"><Edit3 size={12} /></button>
                            <button onClick={(e) => { e.stopPropagation(); handleDelete(s.id) }} className="text-muted-foreground hover:text-red-400 cursor-pointer"><Trash2 size={12} /></button>
                          </div>
                        </div>
                        <p className="text-[10px] text-muted-foreground line-clamp-2 mb-1">{s.description}</p>
                        {s.category && <span className="rounded bg-violet-500/10 px-1.5 py-0.5 text-[8px] text-violet-400">{s.category}</span>}
                      </div>
                    ))}
                  </div>
                </div>
              )}

              {/* Built-in Skills */}
              <h2 className="text-xs font-semibold text-foreground mb-3">{t('skills.builtin')}</h2>
              <div className={`grid gap-3 ${viewing ? 'grid-cols-1' : 'grid-cols-1 md:grid-cols-2 lg:grid-cols-3'}`}>
                {builtin.map((s) => (
                  <div
                    key={s.id}
                    onClick={() => setViewing(s)}
                    className={`rounded-lg border p-3 group transition-colors cursor-pointer ${viewing?.id === s.id ? 'border-blue-500/50 bg-blue-500/5' : 'border-border/50 hover:border-blue-500/30'}`}
                  >
                    <div className="flex items-start justify-between mb-1">
                      <div className="flex items-center gap-1.5">
                        <Sparkles size={13} className="text-blue-400" />
                        <span className="text-xs font-semibold text-foreground">{s.name}</span>
                      </div>
                      <button onClick={(e) => { e.stopPropagation(); handleExport(s.id, s.name) }} className="text-muted-foreground hover:text-foreground cursor-pointer opacity-0 group-hover:opacity-100 transition-opacity" title="Export SKILL.md"><Download size={12} /></button>
                    </div>
                    <p className="text-[10px] text-muted-foreground line-clamp-2 mb-1">{s.description}</p>
                    <span className="rounded bg-blue-500/10 px-1.5 py-0.5 text-[8px] text-blue-400">{s.category}</span>
                  </div>
                ))}
              </div>
            </>
          )}
        </div>

        {/* ─── Right: Detail Panel ─── */}
        {viewing && (
          <div className="flex-1 border-l border-border flex flex-col overflow-hidden">
            <div className="flex items-center justify-between border-b border-border bg-card px-4 py-2.5 shrink-0">
              <div className="flex items-center gap-2">
                <Sparkles size={14} className={viewing.isBuiltin ? 'text-blue-400' : 'text-violet-400'} />
                <span className="text-xs font-semibold text-foreground">{viewing.name}</span>
                <span className={`rounded px-1.5 py-0.5 text-[8px] ${viewing.isBuiltin ? 'bg-blue-500/10 text-blue-400' : 'bg-violet-500/10 text-violet-400'}`}>
                  {viewing.isBuiltin ? 'Built-in' : 'Custom'}
                </span>
              </div>
              <button onClick={() => setViewing(null)} className="text-muted-foreground hover:text-foreground cursor-pointer">
                <X size={14} />
              </button>
            </div>

            <div className="flex-1 overflow-y-auto p-4 space-y-4">
              {/* Description */}
              {viewing.description && (
                <div>
                  <h3 className="text-[10px] font-semibold text-muted-foreground uppercase mb-1">Description</h3>
                  <p className="text-xs text-foreground">{viewing.description}</p>
                </div>
              )}

              {/* Category */}
              <div>
                <h3 className="text-[10px] font-semibold text-muted-foreground uppercase mb-1">Category</h3>
                <p className="text-xs text-foreground">{viewing.category || '—'}</p>
              </div>

              {/* Instructions */}
              {viewing.instructions && (
                <div>
                  <h3 className="text-[10px] font-semibold text-muted-foreground uppercase mb-1">Instructions</h3>
                  <pre className="rounded-md border border-border bg-background p-3 text-[11px] font-mono text-foreground whitespace-pre-wrap break-words max-h-[400px] overflow-y-auto">
                    {viewing.instructions}
                  </pre>
                </div>
              )}

              {/* Tools */}
              {viewing.tools && viewing.tools.length > 0 && (
                <div>
                  <h3 className="text-[10px] font-semibold text-muted-foreground uppercase mb-1">Tools</h3>
                  <div className="flex flex-wrap gap-1">
                    {viewing.tools.map((tool) => (
                      <span key={tool} className="rounded bg-blue-500/10 border border-blue-500/20 px-1.5 py-0.5 text-[9px] text-blue-400 font-mono">{tool}</span>
                    ))}
                  </div>
                </div>
              )}
            </div>
          </div>
        )}
      </div>

      {showForm && (
        <SkillForm
          editing={editing}
          onClose={() => setShowForm(false)}
          onSaved={() => { setShowForm(false); fetchSkills() }}
        />
      )}
      {confirmDialog}
    </div>
  )
}

function SkillForm({ editing, onClose, onSaved }: { editing: CustomSkill | null; onClose: () => void; onSaved: () => void }) {
  const { t } = useTranslation('studio')
  const { t: tn } = useTranslation('notifications')
  const [name, setName] = useState(editing?.name ?? '')
  const [description, setDescription] = useState(editing?.description ?? '')
  const [category, setCategory] = useState(editing?.category ?? '')
  const [instructions, setInstructions] = useState(editing?.instructions ?? '')
  const [tools, setTools] = useState(Array.isArray(editing?.tools) ? editing.tools.join(', ') : (editing?.tools ?? ''))
  const [saving, setSaving] = useState(false)

  const handleSubmit = async () => {
    if (!name.trim()) return
    setSaving(true)
    try {
      const data = { name, description, category, instructions, tools: tools.split(',').map((t) => t.trim()).filter(Boolean) }
      if (editing) await api.skills.update(editing.id, data)
      else await api.skills.create(data)
      onSaved()
    } catch (err) {
      console.error('Failed to save skill:', err); notify.error(tn('saveFailed.skill'), { description: (err as any)?.message })
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" onClick={onClose}>
      <div className="w-[400px] rounded-lg border border-border bg-card shadow-xl" onClick={(e) => e.stopPropagation()}>
        <div className="flex items-center justify-between border-b border-border px-4 py-3">
          <h2 className="text-sm font-semibold text-foreground">
            {editing ? t('skills.edit') : t('skills.create')}
          </h2>
          <button onClick={onClose} className="text-muted-foreground hover:text-foreground cursor-pointer"><X size={16} /></button>
        </div>
        <div className="p-4 space-y-3">
          <div>
            <label className="block text-[10px] text-muted-foreground mb-1">{t('name')}</label>
            <input className="field-input" value={name} onChange={(e) => setName(e.target.value)} placeholder="My Skill" />
          </div>
          <div>
            <label className="block text-[10px] text-muted-foreground mb-1">{t('skills.category')}</label>
            <input className="field-input" value={category} onChange={(e) => setCategory(e.target.value)} placeholder="General" />
          </div>
          <div>
            <label className="block text-[10px] text-muted-foreground mb-1">{t('dialog.description')}</label>
            <input className="field-input" value={description} onChange={(e) => setDescription(e.target.value)} />
          </div>
          <div>
            <label className="block text-[10px] text-muted-foreground mb-1">{t('skills.instructions')}</label>
            <textarea className="field-textarea" rows={4} value={instructions} onChange={(e) => setInstructions(e.target.value)} placeholder="System prompt for this skill..." />
          </div>
          <div>
            <label className="block text-[10px] text-muted-foreground mb-1">{t('skills.tools')}</label>
            <input className="field-input font-mono text-[10px]" value={tools} onChange={(e) => setTools(e.target.value)} placeholder="web_search, read_url" />
          </div>
          <button
            onClick={handleSubmit}
            disabled={saving || !name.trim()}
            className="w-full rounded-md bg-blue-600 px-3 py-2 text-xs font-semibold text-white hover:bg-blue-500 disabled:opacity-50 transition-colors cursor-pointer"
          >
            {saving ? '...' : (editing ? t('save', { ns: 'common' }) : t('skills.create'))}
          </button>
        </div>
      </div>
    </div>
  )
}
