/**
 * PromptRefinerDialog — Prompt 優化預覽對話框。
 * 顯示 Before/After tab + 變更說明，使用者確認後套用。
 */
import { useState } from 'react'
import { X, Check, Sparkles } from 'lucide-react'
import { useTranslation } from 'react-i18next'

export interface PromptRefinerResult {
  original: string
  refined: string
  changes: string[]
}

interface Props {
  result: PromptRefinerResult
  onApply: (refined: string) => void
  onClose: () => void
}

export function PromptRefinerDialog({ result, onApply, onClose }: Props) {
  const { t } = useTranslation('common')
  const [tab, setTab] = useState<'before' | 'after'>('after')

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60" onClick={onClose}>
      <div
        className="w-[700px] max-h-[75vh] rounded-lg border border-border bg-card shadow-2xl flex flex-col"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div className="flex items-center justify-between border-b border-border px-4 py-2.5 shrink-0">
          <div className="flex items-center gap-2">
            <Sparkles size={14} className="text-amber-400" />
            <h2 className="text-sm font-semibold text-foreground">{t('promptRefiner.title')}</h2>
          </div>
          <button onClick={onClose} className="text-muted-foreground hover:text-foreground cursor-pointer">
            <X size={16} />
          </button>
        </div>

        {/* Changes */}
        {result.changes.length > 0 && (
          <div className="border-b border-border px-4 py-2.5 space-y-1">
            <p className="text-[10px] font-medium text-muted-foreground">{t('promptRefiner.changes')}</p>
            {result.changes.map((change, i) => (
              <div key={i} className="flex items-start gap-1.5">
                <Check size={10} className="text-green-500 mt-0.5 shrink-0" />
                <span className="text-[10px] text-foreground">{change}</span>
              </div>
            ))}
          </div>
        )}

        {/* Tabs */}
        <div className="flex border-b border-border px-4">
          <button
            onClick={() => setTab('before')}
            className={`px-3 py-1.5 text-[10px] font-medium border-b-2 transition-colors cursor-pointer ${
              tab === 'before'
                ? 'border-blue-500 text-foreground'
                : 'border-transparent text-muted-foreground hover:text-foreground'
            }`}
          >
            {t('promptRefiner.before')}
          </button>
          <button
            onClick={() => setTab('after')}
            className={`px-3 py-1.5 text-[10px] font-medium border-b-2 transition-colors cursor-pointer ${
              tab === 'after'
                ? 'border-blue-500 text-foreground'
                : 'border-transparent text-muted-foreground hover:text-foreground'
            }`}
          >
            {t('promptRefiner.after')}
          </button>
        </div>

        {/* Content */}
        <div className="flex-1 overflow-y-auto p-4 min-h-[200px] max-h-[400px]">
          <pre className="text-[11px] font-mono text-foreground whitespace-pre-wrap break-words leading-relaxed">
            {tab === 'before' ? result.original : result.refined}
          </pre>
        </div>

        {/* Footer */}
        <div className="flex justify-end gap-2 border-t border-border px-4 py-2.5 shrink-0">
          <button
            onClick={onClose}
            className="rounded-md border border-border px-3 py-1.5 text-xs text-muted-foreground hover:text-foreground transition-colors cursor-pointer"
          >
            {t('promptRefiner.discard')}
          </button>
          <button
            onClick={() => onApply(result.refined)}
            className="rounded-md bg-blue-600 px-4 py-1.5 text-xs font-semibold text-white hover:bg-blue-500 transition-colors cursor-pointer"
          >
            {t('promptRefiner.apply')}
          </button>
        </div>
      </div>
    </div>
  )
}
