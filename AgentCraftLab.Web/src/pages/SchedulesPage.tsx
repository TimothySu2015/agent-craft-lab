/**
 * SchedulesPage — 排程管理頁面：建立/編輯定時排程 + 執行記錄查詢。
 * 對應後端 /api/schedules（Commercial 模式）。
 */
import { useState, useEffect, useCallback } from 'react'
import { useTranslation } from 'react-i18next'
import { Clock, Plus, Pause, Play, Edit3, Trash2, FileText, X } from 'lucide-react'
import { useConfirmDialog } from '@/components/shared/ConfirmDialog'
import { api, type ScheduleDocument, type ScheduleLogDocument, type WorkflowDocument } from '@/lib/api'
import { notify } from '@/lib/notify'

const CRON_PRESETS: { label: string; cron: string }[] = [
  { label: 'Every hour', cron: '0 * * * *' },
  { label: 'Daily 9am', cron: '0 9 * * *' },
  { label: 'Weekdays 9am', cron: '0 9 * * 1-5' },
  { label: 'Weekly Mon', cron: '0 9 * * 1' },
  { label: 'Every 30min', cron: '*/30 * * * *' },
]

const COMMON_TIMEZONES = [
  'UTC',
  'Asia/Taipei',
  'Asia/Tokyo',
  'Asia/Shanghai',
  'Asia/Singapore',
  'America/New_York',
  'America/Chicago',
  'America/Los_Angeles',
  'Europe/London',
  'Europe/Berlin',
  'Australia/Sydney',
]

function fmtDate(iso: string | null): string {
  if (!iso) return '-'
  const d = new Date(iso)
  return `${String(d.getMonth() + 1).padStart(2, '0')}/${String(d.getDate()).padStart(2, '0')} ${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
}

function fmtDateTime(iso: string): string {
  const d = new Date(iso)
  return `${String(d.getMonth() + 1).padStart(2, '0')}/${String(d.getDate()).padStart(2, '0')} ${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}:${String(d.getSeconds()).padStart(2, '0')}`
}

export function SchedulesPage() {
  const { t } = useTranslation()
  const { t: tn } = useTranslation('notifications')
  const { confirm, confirmDialog } = useConfirmDialog()

  const [schedules, setSchedules] = useState<ScheduleDocument[]>([])
  const [publishedWorkflows, setPublishedWorkflows] = useState<WorkflowDocument[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [unavailable, setUnavailable] = useState(false)

  // form state
  const [showForm, setShowForm] = useState(false)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [formWorkflowId, setFormWorkflowId] = useState('')
  const [formCron, setFormCron] = useState('')
  const [formTimeZone, setFormTimeZone] = useState('UTC')
  const [formDefaultInput, setFormDefaultInput] = useState('')
  const [saving, setSaving] = useState(false)

  // logs
  const [logsFor, setLogsFor] = useState<ScheduleDocument | null>(null)
  const [logs, setLogs] = useState<ScheduleLogDocument[]>([])
  const [logsLoading, setLogsLoading] = useState(false)

  const fetchData = useCallback(async () => {
    setLoading(true)
    try {
      const [scheds, wfs] = await Promise.all([
        api.schedules.list(),
        api.workflows.list(),
      ])
      setSchedules(scheds)
      setPublishedWorkflows(wfs.filter((w) => w.isPublished))
      setUnavailable(false)
    } catch (err: any) {
      // 開源模式下 /api/schedules 不存在，回傳 404
      if (err?.code === 'UNKNOWN' || err?.message?.includes('404') || err?.message?.includes('Not Found')) {
        setUnavailable(true)
      } else {
        console.error('Failed to load schedules:', err)
        notify.error(tn('loadFailed.schedules'))
      }
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { fetchData() }, [fetchData])

  const resetForm = () => {
    setEditingId(null)
    setFormWorkflowId('')
    setFormCron('')
    setFormTimeZone('UTC')
    setFormDefaultInput('')
    setShowForm(false)
    setError(null)
  }

  const handleEdit = (s: ScheduleDocument) => {
    setEditingId(s.id)
    setFormWorkflowId(s.workflowId)
    setFormCron(s.cronExpression)
    setFormTimeZone(s.timeZone)
    setFormDefaultInput(s.defaultInput)
    setShowForm(true)
    setError(null)
  }

  const handleSave = async () => {
    if (!formWorkflowId) { setError('Please select a workflow.'); return }
    if (!formCron.trim()) { setError('Please enter a cron expression.'); return }

    setSaving(true)
    setError(null)
    try {
      await api.schedules.create({
        id: editingId ?? undefined,
        workflowId: formWorkflowId,
        cronExpression: formCron.trim(),
        timeZone: formTimeZone,
        defaultInput: formDefaultInput,
      })
      resetForm()
      await fetchData()
    } catch (err: any) {
      setError(err?.error ?? err?.message ?? 'Failed to save schedule')
    } finally {
      setSaving(false)
    }
  }

  const handleToggle = async (s: ScheduleDocument) => {
    try {
      await api.schedules.toggle(s.id)
      await fetchData()
    } catch (err) {
      console.error('Failed to toggle schedule:', err)
      notify.error(tn('updateFailed.scheduleToggle'), { description: (err as any)?.message })
    }
  }

  const handleDelete = async (s: ScheduleDocument) => {
    if (!await confirm(t('schedules.confirmDelete', { name: s.workflowName }))) return
    try {
      await api.schedules.delete(s.id)
      if (logsFor?.id === s.id) setLogsFor(null)
      await fetchData()
    } catch (err) {
      console.error('Failed to delete schedule:', err)
      notify.error(tn('deleteFailed.schedule'), { description: (err as any)?.message })
    }
  }

  const handleViewLogs = async (s: ScheduleDocument) => {
    setLogsFor(s)
    setLogsLoading(true)
    try {
      const data = await api.schedules.logs(s.id)
      setLogs(data)
    } catch (err) {
      console.error('Failed to load logs:', err)
      notify.error(tn('loadFailed.logs'))
      setLogs([])
    } finally {
      setLogsLoading(false)
    }
  }

  if (unavailable) {
    return (
      <div className="flex flex-1 flex-col overflow-hidden">
        <div className="flex items-center border-b border-border bg-card px-5 shrink-0 h-[41px]">
          <Clock size={16} className="text-orange-400 mr-2" />
          <h1 className="text-sm font-semibold text-foreground">{t('nav.schedules')}</h1>
        </div>
        <div className="flex-1 flex items-center justify-center">
          <p className="text-xs text-muted-foreground">{t('schedules.commercialRequired')}</p>
        </div>
      </div>
    )
  }

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      {/* Top Bar */}
      <div className="flex items-center justify-between border-b border-border bg-card px-5 shrink-0 h-[41px]">
        <div className="flex items-center gap-2">
          <Clock size={16} className="text-orange-400" />
          <h1 className="text-sm font-semibold text-foreground">{t('nav.schedules')}</h1>
        </div>
        <button
          onClick={() => { resetForm(); setShowForm(true) }}
          className="flex items-center gap-1 rounded-md bg-blue-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-blue-500 transition-colors cursor-pointer"
        >
          <Plus size={13} /> {t('create')}
        </button>
      </div>

      <div className="flex-1 overflow-y-auto p-5 space-y-4">
        {/* Error */}
        {error && (
          <div className="flex items-center justify-between rounded-md border border-red-500/30 bg-red-500/10 px-3 py-2 text-xs text-red-400">
            <span>{error}</span>
            <button onClick={() => setError(null)} className="cursor-pointer"><X size={14} /></button>
          </div>
        )}

        {/* Create / Edit Form */}
        {showForm && (
          <div className="rounded-lg border border-border bg-card">
            <div className="border-b border-border px-4 py-2.5">
              <h2 className="text-xs font-semibold text-foreground">
                {editingId ? t('schedules.editSchedule') : t('schedules.newSchedule')}
              </h2>
            </div>
            <div className="p-4 space-y-3">
              <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-3">
                {/* Workflow */}
                <div>
                  <label className="block text-[10px] text-muted-foreground mb-1">Published Workflow</label>
                  <select
                    className="field-input"
                    value={formWorkflowId}
                    onChange={(e) => setFormWorkflowId(e.target.value)}
                  >
                    <option value="">-- Select --</option>
                    {publishedWorkflows.map((w) => (
                      <option key={w.id} value={w.id}>{w.name}</option>
                    ))}
                  </select>
                </div>

                {/* Cron */}
                <div>
                  <label className="block text-[10px] text-muted-foreground mb-1">Cron Expression</label>
                  <input
                    className="field-input font-mono text-[11px]"
                    value={formCron}
                    onChange={(e) => setFormCron(e.target.value)}
                    placeholder="0 9 * * 1-5"
                  />
                  <div className="flex flex-wrap gap-x-2 gap-y-0.5 mt-1">
                    {CRON_PRESETS.map((p) => (
                      <button
                        key={p.cron}
                        onClick={() => setFormCron(p.cron)}
                        className="text-[9px] text-blue-400 hover:text-blue-300 cursor-pointer"
                      >
                        {p.label}
                      </button>
                    ))}
                  </div>
                </div>

                {/* TimeZone */}
                <div>
                  <label className="block text-[10px] text-muted-foreground mb-1">Time Zone</label>
                  <select
                    className="field-input"
                    value={formTimeZone}
                    onChange={(e) => setFormTimeZone(e.target.value)}
                  >
                    {COMMON_TIMEZONES.map((tz) => (
                      <option key={tz} value={tz}>{tz}</option>
                    ))}
                  </select>
                </div>

                {/* Default Input */}
                <div>
                  <label className="block text-[10px] text-muted-foreground mb-1">Default Input</label>
                  <input
                    className="field-input"
                    value={formDefaultInput}
                    onChange={(e) => setFormDefaultInput(e.target.value)}
                    placeholder="(optional)"
                  />
                </div>
              </div>

              <div className="flex gap-2">
                <button
                  onClick={handleSave}
                  disabled={saving}
                  className="rounded-md bg-blue-600 px-4 py-1.5 text-xs font-semibold text-white hover:bg-blue-500 disabled:opacity-50 transition-colors cursor-pointer"
                >
                  {saving ? '...' : (editingId ? t('save') : t('create'))}
                </button>
                <button
                  onClick={resetForm}
                  className="rounded-md border border-border px-4 py-1.5 text-xs text-muted-foreground hover:text-foreground transition-colors cursor-pointer"
                >
                  {t('cancel')}
                </button>
              </div>
            </div>
          </div>
        )}

        {/* Schedule List */}
        {loading ? (
          <p className="text-xs text-muted-foreground text-center py-8">{t('loading')}</p>
        ) : schedules.length === 0 ? (
          <p className="text-xs text-muted-foreground text-center py-8">{t('schedules.empty')}</p>
        ) : (
          <div className="overflow-x-auto rounded-lg border border-border">
            <table className="w-full">
              <thead>
                <tr className="border-b border-border bg-card">
                  <th className="px-3 py-2 text-left text-[10px] font-medium uppercase text-muted-foreground">Status</th>
                  <th className="px-3 py-2 text-left text-[10px] font-medium uppercase text-muted-foreground">Workflow</th>
                  <th className="px-3 py-2 text-left text-[10px] font-medium uppercase text-muted-foreground">Cron</th>
                  <th className="px-3 py-2 text-left text-[10px] font-medium uppercase text-muted-foreground">Time Zone</th>
                  <th className="px-3 py-2 text-left text-[10px] font-medium uppercase text-muted-foreground">Next Run</th>
                  <th className="px-3 py-2 text-left text-[10px] font-medium uppercase text-muted-foreground">Last Run</th>
                  <th className="px-3 py-2 text-left text-[10px] font-medium uppercase text-muted-foreground">Actions</th>
                </tr>
              </thead>
              <tbody>
                {schedules.map((s) => (
                  <tr
                    key={s.id}
                    className={`border-b border-border hover:bg-secondary/50 transition-colors ${!s.enabled ? 'opacity-50' : ''}`}
                  >
                    <td className="px-3 py-2.5">
                      <span className={`inline-block rounded px-1.5 py-0.5 text-[10px] font-medium ${s.enabled ? 'bg-emerald-500/15 text-emerald-400' : 'bg-zinc-500/15 text-zinc-400'}`}>
                        {s.enabled ? 'Active' : 'Paused'}
                      </span>
                    </td>
                    <td className="px-3 py-2.5 text-xs font-medium text-foreground">{s.workflowName}</td>
                    <td className="px-3 py-2.5">
                      <code className="rounded bg-secondary px-1.5 py-0.5 text-[10px] text-foreground">{s.cronExpression}</code>
                    </td>
                    <td className="px-3 py-2.5 text-[10px] text-muted-foreground">{s.timeZone}</td>
                    <td className="px-3 py-2.5 text-[10px] text-muted-foreground">{fmtDate(s.nextRunAt)}</td>
                    <td className="px-3 py-2.5 text-[10px] text-muted-foreground">{s.lastRunAt ? fmtDate(s.lastRunAt) : 'Never'}</td>
                    <td className="px-3 py-2.5">
                      <div className="flex gap-1">
                        <button
                          onClick={() => handleToggle(s)}
                          title={s.enabled ? 'Pause' : 'Resume'}
                          className="rounded p-1 text-muted-foreground hover:text-foreground hover:bg-secondary transition-colors cursor-pointer"
                        >
                          {s.enabled ? <Pause size={13} /> : <Play size={13} />}
                        </button>
                        <button
                          onClick={() => handleEdit(s)}
                          title="Edit"
                          className="rounded p-1 text-muted-foreground hover:text-foreground hover:bg-secondary transition-colors cursor-pointer"
                        >
                          <Edit3 size={13} />
                        </button>
                        <button
                          onClick={() => handleDelete(s)}
                          title="Delete"
                          className="rounded p-1 text-muted-foreground hover:text-red-400 hover:bg-secondary transition-colors cursor-pointer"
                        >
                          <Trash2 size={13} />
                        </button>
                        <button
                          onClick={() => handleViewLogs(s)}
                          title="Execution Logs"
                          className={`rounded p-1 transition-colors cursor-pointer ${logsFor?.id === s.id ? 'text-blue-400 bg-blue-500/10' : 'text-muted-foreground hover:text-foreground hover:bg-secondary'}`}
                        >
                          <FileText size={13} />
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {/* Execution Logs */}
        {logsFor && (
          <div className="rounded-lg border border-border bg-card">
            <div className="flex items-center justify-between border-b border-border px-4 py-2.5">
              <span className="text-xs font-semibold text-foreground">
                {t('schedules.executionLogs')}: <span className="text-blue-400">{logsFor.workflowName}</span>
              </span>
              <button onClick={() => setLogsFor(null)} className="text-muted-foreground hover:text-foreground cursor-pointer">
                <X size={14} />
              </button>
            </div>
            <div>
              {logsLoading ? (
                <p className="px-4 py-4 text-xs text-muted-foreground">{t('loading')}</p>
              ) : logs.length === 0 ? (
                <p className="px-4 py-4 text-xs text-muted-foreground">{t('schedules.noLogs')}</p>
              ) : (
                <table className="w-full">
                  <thead>
                    <tr className="border-b border-border bg-card">
                      <th className="px-3 py-2 text-left text-[10px] font-medium uppercase text-muted-foreground">Time</th>
                      <th className="px-3 py-2 text-left text-[10px] font-medium uppercase text-muted-foreground">Status</th>
                      <th className="px-3 py-2 text-left text-[10px] font-medium uppercase text-muted-foreground">Duration</th>
                      <th className="px-3 py-2 text-left text-[10px] font-medium uppercase text-muted-foreground">Output / Error</th>
                    </tr>
                  </thead>
                  <tbody>
                    {logs.map((log) => (
                      <tr key={log.id} className="border-b border-border last:border-0">
                        <td className="px-3 py-2 text-[10px] text-muted-foreground">{fmtDateTime(log.createdAt)}</td>
                        <td className="px-3 py-2">
                          <span className={`inline-block rounded px-1.5 py-0.5 text-[10px] font-medium ${log.success ? 'bg-emerald-500/15 text-emerald-400' : 'bg-red-500/15 text-red-400'}`}>
                            {log.statusText}
                          </span>
                        </td>
                        <td className="px-3 py-2 text-[10px] text-muted-foreground">{log.elapsedMs}ms</td>
                        <td className="px-3 py-2 text-[10px] max-w-xs truncate">
                          <span className={log.success ? 'text-muted-foreground' : 'text-red-400'}>
                            {log.success ? (log.output ?? '-') : (log.error ?? 'Unknown error')}
                          </span>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>
          </div>
        )}
      </div>
      {confirmDialog}
    </div>
  )
}
