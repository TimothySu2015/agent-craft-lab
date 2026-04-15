import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { X } from 'lucide-react'
import { api } from '@/lib/api'
import { useWorkflowStore } from '@/stores/workflow-store'

interface Props {
  open: boolean;
  onClose: () => void;
  currentId: string | null;
  currentName: string;
  onSaved: (id: string, name: string) => void;
}

export function SaveDialog({ open, onClose, currentId, currentName, onSaved }: Props) {
  const { t } = useTranslation(['studio', 'common'])
  const nodes = useWorkflowStore((s) => s.nodes)
  const edges = useWorkflowStore((s) => s.edges)

  const [name, setName] = useState(currentName || t('studio:dialog.defaultName'))
  const [description, setDescription] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  if (!open) return null

  const realNodes = nodes.filter((n: any) => !n.type?.endsWith('-group'))
  const workflowJson = JSON.stringify({ nodes: realNodes, edges })

  const handleSave = async () => {
    if (!name.trim()) {
      setError(t('studio:dialog.nameRequired'))
      return
    }
    setSaving(true)
    setError('')
    try {
      if (currentId) {
        await api.workflows.update(currentId, { name, description, workflowJson })
        onSaved(currentId, name)
      } else {
        const doc = await api.workflows.create({ name, description, workflowJson })
        onSaved(doc.id, doc.name)
      }
      onClose()
    } catch (err: any) {
      setError(err?.message || err?.code || t('studio:dialog.saveFailed'))
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
      <div className="w-[400px] rounded-lg border border-border bg-card p-5 shadow-xl">
        <div className="flex items-center justify-between mb-4">
          <h2 className="text-sm font-semibold text-foreground">
            {currentId ? t('studio:dialog.updateWorkflow') : t('studio:dialog.saveWorkflow')}
          </h2>
          <button onClick={onClose} className="text-muted-foreground hover:text-foreground cursor-pointer">
            <X size={16} />
          </button>
        </div>

        <div className="mb-3">
          <label className="block text-[10px] font-medium uppercase text-muted-foreground mb-1">{t('studio:name')}</label>
          <input
            className="field-input text-sm"
            value={name}
            onChange={(e) => setName(e.target.value)}
            autoFocus
          />
        </div>

        <div className="mb-4">
          <label className="block text-[10px] font-medium uppercase text-muted-foreground mb-1">{t('studio:dialog.description')}</label>
          <textarea
            className="field-textarea"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            rows={2}
            placeholder={t('studio:dialog.optional')}
          />
        </div>

        {error && <p className="text-xs text-red-400 mb-3">{error}</p>}

        <div className="flex justify-end gap-2">
          <button onClick={onClose} className="rounded-md border border-border bg-secondary px-4 py-1.5 text-xs text-muted-foreground hover:bg-accent cursor-pointer">
            {t('cancel')}
          </button>
          <button
            onClick={handleSave}
            disabled={saving}
            className="rounded-md bg-primary px-4 py-1.5 text-xs font-medium text-primary-foreground hover:opacity-90 disabled:opacity-50 cursor-pointer"
          >
            {saving ? t('loading') : t('save')}
          </button>
        </div>
      </div>
    </div>
  )
}
