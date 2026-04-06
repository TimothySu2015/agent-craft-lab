/**
 * DebugActionPanel — Debug Mode 暫停時顯示的操作面板。
 * 類似 HumanInputPanel，從 coagent-store 讀取 pendingDebugAction，
 * 顯示 Continue / Rerun / Skip 按鈕，點擊後呼叫 POST /ag-ui/debug-action。
 */
import { Play, RotateCcw, SkipForward } from 'lucide-react'
import { useCoAgentStore } from '@/stores/coagent-store'
import { api } from '@/lib/api'

export function DebugActionPanel() {
  const pending = useCoAgentStore((s) => s.state?.pendingDebugAction)

  if (!pending) return null

  const submit = async (action: 'continue' | 'rerun' | 'skip') => {
    try {
      await api.debug.submitAction({ action })
    } catch {
      /* ignore — bridge may already be resolved */
    }
  }

  return (
    <div className="mx-3 mb-3 rounded-lg border border-orange-400/40 bg-orange-400/5 p-3">
      <div className="text-[10px] font-medium text-orange-400 mb-1.5">
        Debug — paused at {pending.nodeName}
      </div>
      {pending.output && (
        <div className="text-[9px] text-muted-foreground mb-2 max-h-[60px] overflow-y-auto whitespace-pre-wrap break-words">
          {pending.output}
        </div>
      )}
      <div className="flex gap-1.5">
        <button
          type="button"
          className="flex-1 flex items-center justify-center gap-1 rounded-md bg-green-500/15 border border-green-500/30 px-2 py-1 text-[10px] font-medium text-green-400 hover:bg-green-500/25 transition-colors"
          onClick={() => submit('continue')}
        >
          <Play size={10} />
          Continue
        </button>
        <button
          type="button"
          className="flex-1 flex items-center justify-center gap-1 rounded-md bg-orange-500/15 border border-orange-500/30 px-2 py-1 text-[10px] font-medium text-orange-400 hover:bg-orange-500/25 transition-colors"
          onClick={() => submit('rerun')}
        >
          <RotateCcw size={10} />
          Rerun
        </button>
        <button
          type="button"
          className="flex-1 flex items-center justify-center gap-1 rounded-md bg-muted border border-border px-2 py-1 text-[10px] font-medium text-muted-foreground hover:bg-accent transition-colors"
          onClick={() => submit('skip')}
        >
          <SkipForward size={10} />
          Skip
        </button>
      </div>
    </div>
  )
}
