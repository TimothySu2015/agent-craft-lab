/**
 * ConfirmDialog — 共用確認對話框，取代 browser confirm()。
 * 用法：const { confirm, confirmDialog } = useConfirmDialog()
 */
import { useState, useCallback, useRef } from 'react'

interface ConfirmDialogProps {
  open: boolean
  message: string
  confirmLabel?: string
  cancelLabel?: string
  danger?: boolean
  onConfirm: () => void
  onCancel: () => void
}

export function ConfirmDialog({ open, message, confirmLabel = 'OK', cancelLabel = 'Cancel', danger = true, onConfirm, onCancel }: ConfirmDialogProps) {
  if (!open) return null

  return (
    <div className="fixed inset-0 z-[100] flex items-center justify-center bg-black/40" onClick={onCancel}>
      <div className="rounded-lg border border-border bg-card shadow-2xl p-4 min-w-[280px] max-w-[400px]" onClick={(e) => e.stopPropagation()}>
        <p className="text-sm text-foreground mb-4 whitespace-pre-wrap">{message}</p>
        <div className="flex justify-end gap-2">
          <button
            className="rounded-md border border-border bg-secondary px-3 py-1.5 text-xs text-muted-foreground cursor-pointer hover:bg-accent"
            onClick={onCancel}
          >
            {cancelLabel}
          </button>
          <button
            className={`rounded-md px-3 py-1.5 text-xs font-semibold text-white cursor-pointer ${
              danger ? 'bg-red-600 hover:bg-red-500' : 'bg-blue-600 hover:bg-blue-500'
            }`}
            onClick={onConfirm}
            autoFocus
          >
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  )
}

/** Hook：回傳 confirm() async 函式 + ConfirmDialog 元件 */
export function useConfirmDialog() {
  const [state, setState] = useState<{ message: string; confirmLabel?: string; cancelLabel?: string; danger?: boolean } | null>(null)
  const resolveRef = useRef<((value: boolean) => void) | null>(null)

  const confirm = useCallback((message: string, opts?: { confirmLabel?: string; cancelLabel?: string; danger?: boolean }): Promise<boolean> => {
    return new Promise((resolve) => {
      resolveRef.current = resolve
      setState({ message, ...opts })
    })
  }, [])

  const handleConfirm = useCallback(() => {
    resolveRef.current?.(true)
    resolveRef.current = null
    setState(null)
  }, [])

  const handleCancel = useCallback(() => {
    resolveRef.current?.(false)
    resolveRef.current = null
    setState(null)
  }, [])

  const dialog = (
    <ConfirmDialog
      open={state !== null}
      message={state?.message ?? ''}
      confirmLabel={state?.confirmLabel}
      cancelLabel={state?.cancelLabel}
      danger={state?.danger}
      onConfirm={handleConfirm}
      onCancel={handleCancel}
    />
  )

  return { confirm, confirmDialog: dialog }
}
