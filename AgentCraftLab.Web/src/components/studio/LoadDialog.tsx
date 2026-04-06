import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { X, Trash2 } from 'lucide-react'
import { api, type WorkflowDocument } from '@/lib/api'
import { useWorkflowStore } from '@/stores/workflow-store'
import type { Node, Edge } from '@xyflow/react'
import type { NodeData } from '@/types/workflow'

interface Props {
  open: boolean;
  onClose: () => void;
  onLoaded: (id: string, name: string) => void;
}

export function LoadDialog({ open, onClose, onLoaded }: Props) {
  const { t } = useTranslation(['studio', 'common'])
  const setWorkflow = useWorkflowStore((s) => s.setWorkflow)

  const [workflows, setWorkflows] = useState<WorkflowDocument[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    if (!open) return
    setLoading(true)
    setError('')
    api.workflows.list()
      .then(setWorkflows)
      .catch((err) => setError(err?.message || 'Failed to load'))
      .finally(() => setLoading(false))
  }, [open])

  if (!open) return null

  const handleLoad = async (doc: WorkflowDocument) => {
    try {
      const parsed = JSON.parse(doc.workflowJson)
      const nodes: Node<NodeData>[] = parsed.nodes ?? []
      const edges: Edge[] = parsed.edges ?? []
      setWorkflow(nodes, edges)
      onLoaded(doc.id, doc.name)
      onClose()
    } catch {
      setError(t('studio:dialog.invalidJson'))
    }
  }

  const handleDelete = async (id: string) => {
    try {
      await api.workflows.delete(id)
      setWorkflows((prev) => prev.filter((w) => w.id !== id))
    } catch (err: any) {
      setError(err?.message || t('studio:dialog.deleteFailed'))
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
      <div className="w-[500px] max-h-[70vh] rounded-lg border border-border bg-card p-5 shadow-xl flex flex-col">
        <div className="flex items-center justify-between mb-4 shrink-0">
          <h2 className="text-sm font-semibold text-foreground">{t('studio:dialog.loadWorkflow')}</h2>
          <button onClick={onClose} className="text-muted-foreground hover:text-foreground cursor-pointer">
            <X size={16} />
          </button>
        </div>

        {error && <p className="text-xs text-red-400 mb-3">{error}</p>}

        <div className="flex-1 overflow-y-auto">
          {loading && <p className="text-xs text-muted-foreground">{t('loading')}</p>}

          {!loading && workflows.length === 0 && (
            <p className="text-xs text-muted-foreground text-center py-8">
              {t('studio:dialog.noSavedWorkflows')}
            </p>
          )}

          {workflows.map((doc) => (
            <div
              key={doc.id}
              className="flex items-center justify-between rounded-md border border-border p-3 mb-2 hover:bg-secondary/50 transition-colors"
            >
              <button
                onClick={() => handleLoad(doc)}
                className="flex-1 text-left cursor-pointer"
              >
                <div className="text-xs font-medium text-foreground">{doc.name}</div>
                {doc.description && <div className="text-[10px] text-muted-foreground mt-0.5">{doc.description}</div>}
                <div className="text-[9px] text-muted-foreground mt-1">
                  {new Date(doc.updatedAt).toLocaleDateString()} {new Date(doc.updatedAt).toLocaleTimeString()}
                </div>
              </button>
              <button
                onClick={() => handleDelete(doc.id)}
                className="ml-2 p-1.5 text-muted-foreground hover:text-red-400 cursor-pointer"
                title={t('delete')}
              >
                <Trash2 size={14} />
              </button>
            </div>
          ))}
        </div>

        <div className="flex justify-end mt-3 shrink-0">
          <button onClick={onClose} className="rounded-md border border-border bg-secondary px-4 py-1.5 text-xs text-muted-foreground hover:bg-accent cursor-pointer">
            {t('cancel')}
          </button>
        </div>
      </div>
    </div>
  )
}
