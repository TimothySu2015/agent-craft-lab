/**
 * ExportDialog — 匯出模式選擇（4 種：JSON / Web API / Teams Bot / Console App）
 */
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { X, FileJson, Globe2, MessageSquare, Terminal, Download } from 'lucide-react'
import { useWorkflowStore } from '@/stores/workflow-store'
import { exportWorkflow } from '@/lib/workflow-io'
import { exportDeployPackage } from '@/lib/export-package'
import { cn } from '@/lib/utils'
import { notify } from '@/lib/notify'

const EXPORT_MODES = [
  { id: 'json', icon: FileJson, color: 'text-blue-400', titleKey: 'export.jsonTitle', descKey: 'export.jsonDesc' },
  { id: 'project', icon: Globe2, color: 'text-green-400', titleKey: 'export.projectTitle', descKey: 'export.projectDesc' },
  { id: 'teams', icon: MessageSquare, color: 'text-cyan-400', titleKey: 'export.teamsTitle', descKey: 'export.teamsDesc' },
  { id: 'console', icon: Terminal, color: 'text-yellow-400', titleKey: 'export.consoleTitle', descKey: 'export.consoleDesc' },
]

interface Props {
  open: boolean
  onClose: () => void
  workflowName: string
}

export function ExportDialog({ open, onClose, workflowName }: Props) {
  const { t } = useTranslation('studio')
  const { t: tn } = useTranslation('notifications')
  const [mode, setMode] = useState('project')
  const [exporting, setExporting] = useState(false)

  if (!open) return null

  const handleExport = async () => {
    setExporting(true)
    const { nodes, edges, workflowSettings } = useWorkflowStore.getState()
    const name = workflowName || 'MyWorkflow'

    try {
      switch (mode) {
        case 'json':
          exportWorkflow(name, nodes, edges, workflowSettings)
          break
        case 'project':
        case 'teams':
        case 'console':
          await exportDeployPackage(name, nodes, edges, mode as 'project' | 'teams' | 'console')
          break
      }
      onClose()
    } catch (err) {
      console.error('Export failed:', err)
      notify.error(tn('exportFailed.workflow'), { description: (err as any)?.message })
    } finally {
      setExporting(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" onClick={onClose}>
      <div className="w-[480px] rounded-lg border border-border bg-card shadow-xl" onClick={(e) => e.stopPropagation()}>
        <div className="flex items-center justify-between border-b border-border px-4 py-3">
          <h2 className="text-sm font-semibold text-foreground">{t('export.title')}</h2>
          <button onClick={onClose} className="text-muted-foreground hover:text-foreground cursor-pointer"><X size={16} /></button>
        </div>

        <div className="p-4 space-y-2">
          {EXPORT_MODES.map((em) => {
            const Icon = em.icon
            const selected = mode === em.id
            return (
              <button key={em.id} onClick={() => setMode(em.id)}
                className={cn('flex w-full items-start gap-3 rounded-md border px-3 py-2.5 text-left transition-colors cursor-pointer',
                  selected ? 'border-blue-500/50 bg-blue-500/5' : 'border-border hover:bg-accent/30')}>
                <input type="radio" checked={selected} readOnly className="mt-0.5 accent-blue-500" />
                <Icon size={18} className={cn('shrink-0 mt-0.5', em.color)} />
                <div>
                  <div className="text-xs font-semibold text-foreground">{t(em.titleKey)}</div>
                  <div className="text-[10px] text-muted-foreground">{t(em.descKey)}</div>
                </div>
              </button>
            )
          })}
        </div>

        <div className="flex justify-end gap-2 border-t border-border px-4 py-3">
          <button onClick={onClose} className="rounded-md border border-border bg-secondary px-3 py-1.5 text-xs text-muted-foreground cursor-pointer">
            {t('cancel', { ns: 'common' })}
          </button>
          <button onClick={handleExport} disabled={exporting}
            className="flex items-center gap-1 rounded-md bg-blue-600 px-4 py-1.5 text-xs font-semibold text-white hover:bg-blue-500 disabled:opacity-50 cursor-pointer">
            <Download size={13} />
            {exporting ? '...' : t('export.download')}
          </button>
        </div>
      </div>
    </div>
  )
}
