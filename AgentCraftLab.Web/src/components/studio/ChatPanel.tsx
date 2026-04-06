import { useState, useMemo, useCallback } from 'react'
import { useTranslation } from 'react-i18next'
import { CopilotKit } from '@copilotkit/react-core'
import { CopilotChat } from '@copilotkit/react-ui'
import '@copilotkit/react-ui/styles.css'
import './copilot-chat.css'
import { Sparkles, Play, PanelRightClose, RotateCcw, PlayCircle, Bug } from 'lucide-react'
import { ChatAssistantMessage } from './ChatAssistantMessage'
import { CopilotReadable } from './CopilotIntegration'
import { AgentStateProvider } from './AgentStateProvider'
import { HumanInputPanel } from './HumanInputPanel'
import { DebugActionPanel } from './DebugActionPanel'
import { AiBuildPanel } from './AiBuildPanel'
import { StableChatInput, chatInputFileRef, type PendingFile } from './ChatInput'
import { useWorkflowStore } from '@/stores/workflow-store'
import { useCredentialStore } from '@/stores/credential-store'
import { useCoAgentStore } from '@/stores/coagent-store'
import { toWorkflowPayloadJson } from '@/lib/workflow-payload'

type ChatTab = 'execute' | 'build'

interface ChatPanelProps {
  onCollapse?: () => void
}

export function ChatPanel({ onCollapse }: ChatPanelProps) {
  const { t, i18n } = useTranslation('chat')
  const [tab, setTab] = useState<ChatTab>('execute')
  const nodes = useWorkflowStore((s) => s.nodes)
  const edges = useWorkflowStore((s) => s.edges)
  const settings = useWorkflowStore((s) => s.workflowSettings)
  const chatSessionId = useWorkflowStore((s) => s.chatSessionId)
  const resetChatSession = useWorkflowStore((s) => s.resetChatSession)
  const actionsEnabled = useCredentialStore((s) => s.copilotActionsEnabled)
  const setActionsEnabled = useCredentialStore((s) => s.setCopilotActionsEnabled)
  const resetCoAgentState = useCoAgentStore((s) => s.reset)
  const lastExecutionId = useCoAgentStore((s) => s.state?.executionId)
  const [resumeExecutionId, setResumeExecutionId] = useState<string | null>(null)
  const [debugMode, setDebugMode] = useState(false)

  // 延遲重建 CopilotKit：只在切回 Execute tab 時才套用新 sessionId，避免 hidden 狀態下重建
  const [activeSessionId, setActiveSessionId] = useState(chatSessionId)
  const handleTabChange = useCallback((newTab: ChatTab) => {
    setTab(newTab)
    if (newTab === 'execute' && chatSessionId !== activeSessionId) {
      setActiveSessionId(chatSessionId)
    }
  }, [chatSessionId, activeSessionId])

  const [pendingFile, setPendingFile] = useState<PendingFile | null>(null)

  const handleClearChat = useCallback(() => {
    // 保存 executionId 供 Resume 使用
    if (lastExecutionId) setResumeExecutionId(lastExecutionId)
    resetChatSession()
    resetCoAgentState()
    setPendingFile(null)
    setActiveSessionId(useWorkflowStore.getState().chatSessionId)
  }, [resetChatSession, resetCoAgentState, lastExecutionId])

  const handleFileReady = useCallback((file: PendingFile) => setPendingFile(file), [])
  const handleFileRemove = useCallback(() => setPendingFile(null), [])

  // 同步 ref 供 StableChatInput 讀取（identity 穩定，不觸發 CopilotChat 重建）
  chatInputFileRef.current = { pendingFile, onFileReady: handleFileReady, onFileRemove: handleFileRemove }

  const workflowJson = useMemo(() => toWorkflowPayloadJson(nodes, edges, settings), [nodes, edges, settings])

  return (
    <aside className="flex-1 min-w-0 flex flex-col border-l border-border bg-card overflow-hidden">
      {/* Tab bar */}
      <div className="flex items-center border-b border-border shrink-0">
        {onCollapse && (
          <button onClick={onCollapse} className="px-2 py-2 text-muted-foreground hover:text-foreground cursor-pointer" title="Collapse">
            <PanelRightClose size={14} />
          </button>
        )}
        <button
          onClick={() => handleTabChange('execute')}
          className={`flex items-center gap-1 flex-1 px-3 py-2 text-[11px] font-medium transition-colors cursor-pointer ${
            tab === 'execute'
              ? 'text-foreground border-b-2 border-blue-500'
              : 'text-muted-foreground hover:text-foreground'
          }`}
        >
          <Play size={11} />
          {t('tabExecute')}
        </button>
        <button
          onClick={() => handleTabChange('build')}
          className={`flex items-center gap-1 flex-1 px-3 py-2 text-[11px] font-medium transition-colors cursor-pointer ${
            tab === 'build'
              ? 'text-foreground border-b-2 border-violet-500'
              : 'text-muted-foreground hover:text-foreground'
          }`}
        >
          <Sparkles size={11} />
          {t('tabBuild')}
        </button>
        {tab === 'execute' && (
          <div className="flex items-center gap-1 mr-2">
            {/* TODO: Resume 按鈕暫時隱藏 — 等 Checkpoint Resume 前端完整串接後啟用 */}
            <button
              onClick={() => setDebugMode(!debugMode)}
              title="Debug Mode"
              className={`p-1 rounded transition-colors cursor-pointer ${
                debugMode
                  ? 'text-orange-400 bg-orange-500/15'
                  : 'text-muted-foreground hover:text-foreground hover:bg-muted/50'
              }`}
            >
              <Bug size={12} />
            </button>
            <button
              onClick={handleClearChat}
              title={t('clearChat')}
              className="p-1 rounded text-muted-foreground hover:text-foreground hover:bg-muted/50 transition-colors cursor-pointer"
            >
              <RotateCcw size={12} />
            </button>
            <button
              onClick={() => setActionsEnabled(!actionsEnabled)}
              title={t('copilotActionsTooltip')}
              className={`rounded-full px-1.5 py-0.5 text-[9px] transition-colors cursor-pointer ${
                actionsEnabled
                  ? 'bg-violet-500/20 text-violet-400 border border-violet-500/30'
                  : 'bg-muted/30 text-muted-foreground border border-border/50 hover:bg-muted/50'
              }`}
            >
              AI
            </button>
          </div>
        )}
      </div>

      {/* Execute Tab — hidden 保持 mount，保留對話紀錄 */}
      <div className={`flex-1 flex flex-col min-h-0 overflow-hidden ${tab !== 'execute' ? 'hidden' : ''}`}>
        <div className="flex-1 min-h-0 overflow-hidden">
        <CopilotKit
          key={activeSessionId}
          runtimeUrl="/copilotkit"
          agent="craftlab"
          showDevConsole={false}
          properties={{
            locale: i18n.language,
            workflowJson,
            ...(pendingFile && { fileId: pendingFile.fileId }),
            ...(resumeExecutionId && { resumeExecutionId }),
            ...(debugMode && { debugMode: 'true' }),
          }}
        >
          <CopilotReadable />
          <AgentStateProvider />
          <CopilotChat
            labels={{
              title: t('title'),
              initial: t('welcome'),
              placeholder: t('placeholder'),
              stopGenerating: t('stop'),
              regenerateResponse: t('regenerate'),
            }}
            className="copilot-chat-studio"
            AssistantMessage={ChatAssistantMessage}
            Input={StableChatInput}
            onSubmitMessage={() => {
              // 訊息送出後清除附件
              setPendingFile(null)
            }}
          />
        </CopilotKit>
        </div>
        {/* Debug/Human panels — 固定在輸入框上方，不被 CopilotChat 擠掉 */}
        <div className="shrink-0">
          <DebugActionPanel />
          <HumanInputPanel />
        </div>
      </div>

      {/* Build Tab — 用 hidden 保持 mount，切回時保留對話 */}
      <div className={`flex-1 flex flex-col overflow-hidden ${tab !== 'build' ? 'hidden' : ''}`}>
        <AiBuildPanel />
      </div>

    </aside>
  )
}
