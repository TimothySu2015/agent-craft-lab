import { useState, useEffect, useCallback } from 'react'
import { useTranslation } from 'react-i18next'
import { Rocket, Globe, Plug, Globe2, MessageSquare, Copy, Check, Power, PowerOff } from 'lucide-react'
import { api, type WorkflowDocument } from '@/lib/api'
import { cn } from '@/lib/utils'
import { notify } from '@/lib/notify'

const SERVICE_TYPES = [
  { id: 'a2a', label: 'A2A', icon: Globe, color: 'text-green-400', pathPrefix: '/a2a/' },
  { id: 'mcp', label: 'MCP', icon: Plug, color: 'text-yellow-400', pathPrefix: '/mcp/' },
  { id: 'api', label: 'API', icon: Globe2, color: 'text-blue-400', pathPrefix: '/api/' },
  { id: 'teams', label: 'Teams', icon: MessageSquare, color: 'text-cyan-400', pathPrefix: '/teams/' },
]

const INPUT_MODES = [
  { id: 'text/plain', label: 'Text', required: true },
  { id: 'image/png', label: 'PNG' },
  { id: 'image/jpeg', label: 'JPEG' },
  { id: 'image/webp', label: 'WebP' },
  { id: 'application/pdf', label: 'PDF' },
  { id: 'application/zip', label: 'ZIP' },
]

export function PublishedServicesPage() {
  const { t } = useTranslation(['studio', 'common'])
  const { t: tn } = useTranslation('notifications')
  const [workflows, setWorkflows] = useState<WorkflowDocument[]>([])
  const [loading, setLoading] = useState(true)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [copied, setCopied] = useState('')

  const fetchWorkflows = useCallback(() => {
    setLoading(true)
    api.workflows.list()
      .then(setWorkflows)
      .catch((err) => { console.error('Failed to load workflows:', err); notify.error(tn('loadFailed.workflows')) })
      .finally(() => setLoading(false))
  }, [])

  useEffect(() => { fetchWorkflows() }, [fetchWorkflows])

  const selected = workflows.find((w) => w.id === selectedId)

  const togglePublish = async (wf: WorkflowDocument) => {
    try {
      await api.workflows.publish(wf.id, !wf.isPublished)
      fetchWorkflows()
    } catch (err) { console.error('Publish toggle failed:', err); notify.error(tn('updateFailed.publishToggle'), { description: (err as any)?.message }) }
  }

  const toggleType = async (wf: WorkflowDocument, typeId: string) => {
    const types = (wf.type ?? '').split(',').map((s) => s.trim()).filter(Boolean)
    const next = types.includes(typeId) ? types.filter((t) => t !== typeId) : [...types, typeId]
    try {
      await api.workflows.update(wf.id, { name: wf.name, type: next.join(',') })
      fetchWorkflows()
    } catch (err) { console.error('Type toggle failed:', err); notify.error(tn('updateFailed.typeToggle'), { description: (err as any)?.message }) }
  }

  const handleCopy = async (text: string) => {
    await navigator.clipboard.writeText(text)
    setCopied(text)
    setTimeout(() => setCopied(''), 2000)
  }

  const baseUrl = window.location.origin

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      <div className="flex items-center justify-between border-b border-border bg-card px-5 shrink-0 h-[41px]">
        <div className="flex items-center gap-2">
          <Rocket size={16} className="text-orange-400" />
          <h1 className="text-sm font-semibold text-foreground">{t('common:nav.published')}</h1>
          <span className="rounded-full bg-green-500/10 px-2 py-0.5 text-[10px] text-green-400">
            {workflows.filter((w) => w.isPublished).length} {t('published.badge')}
          </span>
        </div>
      </div>

      <div className="flex flex-1 overflow-hidden">
        {/* Left: Workflow List */}
        <div className="w-[300px] shrink-0 border-r border-border overflow-y-auto">
          {loading && <p className="p-4 text-xs text-muted-foreground">{t('loading')}</p>}
          {!loading && workflows.length === 0 && (
            <p className="p-4 text-xs text-muted-foreground">{t('services.noPublished')}</p>
          )}
          {workflows.map((wf) => {
            const types = (wf.type ?? '').split(',').filter(Boolean)
            const isSelected = selectedId === wf.id
            return (
              <button key={wf.id} onClick={() => setSelectedId(wf.id)}
                className={cn('flex w-full items-start gap-2.5 px-4 py-3 text-left border-b border-border/50 transition-colors cursor-pointer',
                  isSelected ? 'bg-accent' : 'hover:bg-accent/30')}>
                <span className={cn('mt-0.5 w-2 h-2 rounded-full shrink-0', wf.isPublished ? 'bg-green-500' : 'bg-muted-foreground/30')} />
                <div className="min-w-0 flex-1">
                  <div className="text-xs font-medium text-foreground truncate">{wf.name}</div>
                  <div className="flex gap-1 mt-1 flex-wrap">
                    {types.map((tid) => {
                      const st = SERVICE_TYPES.find((s) => s.id === tid)
                      return st ? <span key={tid} className={cn('text-[8px] rounded px-1 py-0.5 bg-muted/50', st.color)}>{st.label}</span> : null
                    })}
                    {types.length === 0 && <span className="text-[8px] text-muted-foreground">{t('published.noTypes')}</span>}
                  </div>
                </div>
                <span className={cn('text-[9px] rounded-full px-1.5 py-0.5 shrink-0',
                  wf.isPublished ? 'bg-green-500/10 text-green-400' : 'bg-muted/30 text-muted-foreground')}>
                  {wf.isPublished ? t('published.live') : t('published.draft')}
                </span>
              </button>
            )
          })}
        </div>

        {/* Right: Detail Panel */}
        <div className="flex-1 overflow-y-auto p-5">
          {!selected ? (
            <div className="flex items-center justify-center h-full text-xs text-muted-foreground">
              {t('published.selectWorkflow')}
            </div>
          ) : (
            <div className="max-w-2xl space-y-5">
              {/* Header + Publish Toggle */}
              <div className="flex items-center justify-between">
                <div>
                  <h2 className="text-sm font-semibold text-foreground">{selected.name}</h2>
                  {selected.description && <p className="text-[10px] text-muted-foreground mt-0.5">{selected.description}</p>}
                </div>
                <button onClick={() => togglePublish(selected)}
                  className={cn('flex items-center gap-1.5 rounded-md px-3 py-1.5 text-xs font-semibold transition-colors cursor-pointer',
                    selected.isPublished
                      ? 'bg-red-600/10 text-red-400 border border-red-600/30 hover:bg-red-600/20'
                      : 'bg-green-600 text-white hover:bg-green-500')}>
                  {selected.isPublished ? <><PowerOff size={13} /> {t('published.unpublish')}</> : <><Power size={13} /> {t('published.publish')}</>}
                </button>
              </div>

              {/* Service Types */}
              <div className="rounded-lg border border-border p-4">
                <h3 className="text-xs font-semibold text-foreground mb-3">{t('published.serviceTypes')}</h3>
                <div className="grid grid-cols-2 gap-2">
                  {SERVICE_TYPES.map((st) => {
                    const active = (selected.type ?? '').split(',').includes(st.id)
                    const Icon = st.icon
                    return (
                      <label key={st.id} className={cn('flex items-center gap-2.5 rounded-md border px-3 py-2 cursor-pointer transition-colors',
                        active ? 'border-blue-500/40 bg-blue-500/5' : 'border-border hover:bg-accent/30')}>
                        <input type="checkbox" checked={active} onChange={() => toggleType(selected, st.id)} className="accent-blue-500" />
                        <Icon size={14} className={st.color} />
                        <span className="text-xs text-foreground">{st.label}</span>
                      </label>
                    )
                  })}
                </div>
              </div>

              {/* Endpoints */}
              {selected.isPublished && (selected.type ?? '').split(',').filter(Boolean).length > 0 && (
                <div className="rounded-lg border border-border p-4">
                  <h3 className="text-xs font-semibold text-foreground mb-3">{t('published.endpoints')}</h3>
                  <div className="space-y-2">
                    {(selected.type ?? '').split(',').filter(Boolean).map((typeId) => {
                      const st = SERVICE_TYPES.find((s) => s.id === typeId)
                      if (!st) return null
                      const url = typeId === 'api'
                        ? `POST ${baseUrl}/api/${selected.id}`
                        : typeId === 'teams'
                        ? `POST ${baseUrl}/teams/${selected.id}/api/messages`
                        : `${baseUrl}${st.pathPrefix}${selected.id}`
                      return (
                        <div key={typeId} className="flex items-center gap-2 rounded-md bg-background px-3 py-2">
                          <st.icon size={13} className={st.color} />
                          <code className="flex-1 text-[10px] font-mono text-foreground truncate">{url}</code>
                          <button onClick={() => handleCopy(url)} className="text-muted-foreground hover:text-foreground cursor-pointer shrink-0">
                            {copied === url ? <Check size={12} className="text-green-400" /> : <Copy size={12} />}
                          </button>
                        </div>
                      )
                    })}
                  </div>
                </div>
              )}

              {/* Input Modes */}
              <InputModesCard workflowId={selected.id} currentModes={(selected as any).acceptedInputModes} onChanged={fetchWorkflows} />
            </div>
          )}
        </div>
      </div>
    </div>
  )
}

function InputModesCard({ workflowId, currentModes, onChanged }: { workflowId: string; currentModes?: string; onChanged: () => void }) {
  const { t } = useTranslation('studio')
  const { t: tn } = useTranslation('notifications')
  const [modes, setModes] = useState<Set<string>>(() => {
    const initial = (currentModes ?? 'text/plain').split(',').map((s) => s.trim()).filter(Boolean)
    return new Set(initial.length > 0 ? initial : ['text/plain'])
  })

  const toggle = async (modeId: string) => {
    if (modeId === 'text/plain') return // text always required
    const next = new Set(modes)
    if (next.has(modeId)) next.delete(modeId)
    else next.add(modeId)
    setModes(next)
    try {
      await api.workflows.publish(workflowId, true, [...next])
      onChanged()
    } catch (err) { console.error('Input mode update failed:', err); notify.error(tn('updateFailed.inputMode'), { description: (err as any)?.message }) }
  }

  return (
    <div className="rounded-lg border border-border p-4">
      <h3 className="text-xs font-semibold text-foreground mb-3">{t('published.acceptedInput')}</h3>
      <div className="flex flex-wrap gap-3">
        {INPUT_MODES.map((mode) => {
          const checked = mode.required || modes.has(mode.id)
          return (
            <label key={mode.id} className="flex items-center gap-1.5 cursor-pointer">
              <input type="checkbox" checked={checked} disabled={mode.required} onChange={() => toggle(mode.id)} className="accent-blue-500" />
              <span className="text-xs text-foreground">{mode.label}</span>
              {mode.required && <span className="text-[8px] text-muted-foreground">(required)</span>}
            </label>
          )
        })}
      </div>
      <p className="text-[9px] text-muted-foreground mt-2">{t('published.teamsNote')}</p>
    </div>
  )
}
