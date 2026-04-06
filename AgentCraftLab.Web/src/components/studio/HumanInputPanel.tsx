/**
 * HumanInputPanel — Human-in-the-Loop 輸入面板。
 * 從 coagent-store 讀取 pendingHumanInput（由 AG-UI STATE_SNAPSHOT 同步），
 * 提交到 POST /ag-ui/human-input。
 */
import { useState, useCallback } from 'react'
import { useTranslation } from 'react-i18next'
import { UserCheck } from 'lucide-react'
import { useCoAgentStore } from '@/stores/coagent-store'
import { api } from '@/lib/api'

export function HumanInputPanel() {
  const { t } = useTranslation('chat')
  const pending = useCoAgentStore((s) => s.state?.pendingHumanInput)
  const [textValue, setTextValue] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState('')

  const handleSubmit = useCallback(async (response: string) => {
    if (!pending || submitting) return
    setSubmitting(true)
    setError('')
    try {
      // 空 threadId/runId → 後端 SubmitAnyPending 自動匹配唯一等待中的 bridge
      // 開源單人模式下只有一個 session，不會有 concurrent 問題
      // 商業多人模式需改為從 AG-UI state 取得真實 threadId/runId
      await api.humanInput.submit({ threadId: '', runId: '', response })
      setTextValue('')
    } catch (err) {
      setError(String(err && typeof err === 'object' && 'message' in err ? err.message : 'Submit failed'))
    } finally {
      setSubmitting(false)
    }
  }, [pending, submitting])

  if (!pending) return null

  return (
    <div className="border-t border-border bg-card px-3 py-3 shrink-0 animate-in slide-in-from-bottom-2">
      <div className="flex items-center gap-1.5 mb-2">
        <UserCheck size={14} className="text-amber-400" />
        <span className="text-xs font-semibold text-amber-400">{t('humanInput.title')}</span>
      </div>

      <p className="text-xs text-muted-foreground mb-2 whitespace-pre-wrap">{pending.prompt}</p>
      {error && <p className="text-[10px] text-red-400 mb-1">{error}</p>}

      {pending.inputType === 'approval' && (
        <div className="flex gap-2">
          <button
            onClick={() => handleSubmit('approve')}
            disabled={submitting}
            className="flex-1 rounded-md bg-green-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-green-500 disabled:opacity-50 transition-colors cursor-pointer"
          >
            {t('humanInput.approve')}
          </button>
          <button
            onClick={() => handleSubmit('reject')}
            disabled={submitting}
            className="flex-1 rounded-md bg-red-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-red-500 disabled:opacity-50 transition-colors cursor-pointer"
          >
            {t('humanInput.reject')}
          </button>
        </div>
      )}

      {pending.inputType === 'choice' && pending.choices && (
        <div className="flex flex-wrap gap-1.5">
          {pending.choices.split(',').map((choice) => (
            <button
              key={choice.trim()}
              onClick={() => handleSubmit(choice.trim())}
              disabled={submitting}
              className="rounded-md border border-border bg-secondary px-3 py-1.5 text-xs text-foreground hover:bg-accent disabled:opacity-50 transition-colors cursor-pointer"
            >
              {choice.trim()}
            </button>
          ))}
        </div>
      )}

      {pending.inputType === 'text' && (
        <div className="flex gap-1.5">
          <input
            type="text"
            value={textValue}
            onChange={(e) => setTextValue(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && textValue.trim()) handleSubmit(textValue.trim())
            }}
            placeholder={t('humanInput.placeholder')}
            className="flex-1 rounded-md border border-border bg-background px-2.5 py-1.5 text-xs text-foreground placeholder:text-muted-foreground focus:outline-none focus:ring-1 focus:ring-ring"
            autoFocus
          />
          <button
            onClick={() => textValue.trim() && handleSubmit(textValue.trim())}
            disabled={submitting || !textValue.trim()}
            className="rounded-md bg-blue-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-blue-500 disabled:opacity-50 transition-colors cursor-pointer"
          >
            {t('humanInput.submit')}
          </button>
        </div>
      )}
    </div>
  )
}
