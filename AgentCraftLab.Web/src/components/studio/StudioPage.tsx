import { useState, useCallback, useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { Save, FolderOpen, Download, Upload, LayoutGrid, Settings, BookmarkPlus, Code, PanelRightOpen, PanelRightClose, Layers } from 'lucide-react'
import { Canvas } from './Canvas'
import { NodePalette } from './NodePalette'
import { ChatPanel } from './ChatPanel'
import { PropertiesPanel } from './PropertiesPanel'
import { SaveDialog } from './SaveDialog'
import { LoadDialog } from './LoadDialog'
import { TemplatesDialog } from './TemplatesDialog'
import { WorkflowSettingsDialog } from './WorkflowSettingsDialog'
import { CodeDialog } from './CodeDialog'
import { ExportDialog } from './ExportDialog'
import { ConsolePanel } from './ConsolePanel'
import { getTemplateWorkflow, type TemplateInfo } from '@/lib/templates'
import { useWorkflowStore } from '@/stores/workflow-store'
import { useCustomTemplatesStore } from '@/stores/custom-templates-store'
import { importWorkflow } from '@/lib/workflow-io'
import { notify } from '@/lib/notify'

export function StudioPage() {
  const { t } = useTranslation('studio')
  const { t: tn } = useTranslation('notifications')

  const [showSave, setShowSave] = useState(false)
  const [showLoad, setShowLoad] = useState(false)
  const [showTemplates, setShowTemplates] = useState(false)
  const [showSettings, setShowSettings] = useState(false)
  const [showCode, setShowCode] = useState(false)
  const [showExport, setShowExport] = useState(false)
  const [chatOpen, setChatOpen] = useState(true)
  const [chatWidth, setChatWidth] = useState(360)
  const chatDragRef = useRef<{ startX: number; startW: number } | null>(null)
  const [currentId, setCurrentId] = useState<string | null>(null)
  const [currentName, setCurrentName] = useState('')

  // 啟動時從後端同步自訂範本
  useEffect(() => { useCustomTemplatesStore.getState().loadFromBackend() }, [])

  const handleChatDragStart = useCallback((e: React.MouseEvent) => {
    e.preventDefault()
    chatDragRef.current = { startX: e.clientX, startW: chatWidth }
    const onMove = (ev: MouseEvent) => {
      if (!chatDragRef.current) return
      const delta = chatDragRef.current.startX - ev.clientX
      const newW = Math.min(800, Math.max(280, chatDragRef.current.startW + delta))
      setChatWidth(newW)
    }
    const onUp = () => {
      chatDragRef.current = null
      document.removeEventListener('mousemove', onMove)
      document.removeEventListener('mouseup', onUp)
      document.body.style.cursor = ''
      document.body.style.userSelect = ''
    }
    document.addEventListener('mousemove', onMove)
    document.addEventListener('mouseup', onUp)
    document.body.style.cursor = 'col-resize'
    document.body.style.userSelect = 'none'
  }, [chatWidth])

  const handleSaved = useCallback((id: string, name: string) => {
    setCurrentId(id)
    setCurrentName(name)
  }, [])

  const handleLoaded = useCallback((id: string, name: string) => {
    setCurrentId(id)
    setCurrentName(name)
  }, [])

  // Ctrl+S quick save
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (e.ctrlKey && e.key === 's') {
        e.preventDefault()
        setShowSave(true)
      }
    }
    window.addEventListener('keydown', handler)
    return () => window.removeEventListener('keydown', handler)
  }, [])

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      {/* Top Bar */}
      <div className="flex items-center justify-between border-b border-border bg-card px-5 shrink-0 h-[41px]">
        <div className="flex items-center gap-2">
          <h1 className="text-sm font-semibold text-foreground">{t('title')}</h1>
          {currentName && (
            <span className="text-xs text-muted-foreground">/ {currentName}</span>
          )}
        </div>
        <div className="flex items-center gap-1">
          {/* ─── 檔案操作 ─── */}
          <TbBtn onClick={() => setShowLoad(true)} icon={FolderOpen} label={t('load', { ns: 'common' })} />
          <TbBtn onClick={() => setShowSave(true)} icon={Save} label={t('save', { ns: 'common' })} />
          <label className="flex items-center gap-1 rounded-md border border-border bg-secondary px-2.5 h-7 text-[11px] text-muted-foreground hover:bg-accent hover:text-foreground transition-colors cursor-pointer">
            <Upload size={12} />
            {t('import')}
            <input
              type="file"
              accept=".json"
              className="hidden"
              onChange={async (e) => {
                const file = e.target.files?.[0]
                if (!file) return
                try {
                  const data = await importWorkflow(file)
                  useWorkflowStore.getState().setWorkflow(data.nodes, data.edges)
                  if (data.settings) useWorkflowStore.getState().updateSettings(data.settings)
                  setCurrentId(null)
                  setCurrentName(data.name)
                } catch (err) { console.error('Import failed:', err); notify.error(tn('importFailed.workflow'), { description: err instanceof Error ? err.message : String(err) }) }
                e.target.value = ''
              }}
            />
          </label>

          <span className="w-px h-5 bg-border mx-0.5" />

          {/* ─── 畫布工具 ─── */}
          <TbBtn onClick={() => useWorkflowStore.getState().layout()} icon={LayoutGrid} title="Auto Layout" />
          <TbBtn onClick={() => setShowSettings(true)} icon={Settings} title={t('settings.title')} />
          <TbBtn onClick={() => setShowTemplates(true)} icon={Layers} label={t('templates')} />
          <TbBtn onClick={() => {
            const name = window.prompt(t('dialog.saveAsTemplate'))
            if (!name?.trim()) return
            const { nodes, edges } = useWorkflowStore.getState()
            useCustomTemplatesStore.getState().addTemplate(name.trim(), '', nodes, edges)
          }} icon={BookmarkPlus} title={t('dialog.saveAsTemplate')} />

          <span className="w-px h-5 bg-border mx-0.5" />

          {/* ─── 產出 ─── */}
          <TbBtn onClick={() => setShowCode(true)} icon={Code} title={t('code.title')} />
          <TbBtn onClick={() => setShowExport(true)} icon={Download} label={t('exportJson')} />

        </div>
      </div>

      {/* Three-column layout */}
      <div className="flex flex-1 overflow-hidden">
        <NodePalette />
        <div className="relative flex-1 flex flex-col h-full">
          <div className="relative flex-1">
            <Canvas />
            <PropertiesPanel />
          </div>
          <ConsolePanel />
        </div>
        {/* Chat panel — 用 hidden 保持 mount，收折不清空對話 */}
        <div className={`flex shrink-0 ${chatOpen ? '' : 'hidden'}`} style={{ width: chatWidth }}>
          <div
            onMouseDown={handleChatDragStart}
            className="w-1 shrink-0 cursor-col-resize hover:bg-blue-500/30 active:bg-blue-500/50 transition-colors"
          />
          <ChatPanel onCollapse={() => setChatOpen(false)} />
        </div>
        {!chatOpen && (
          <button
            onClick={() => setChatOpen(true)}
            className="shrink-0 flex flex-col items-center justify-center w-10 border-l border-border bg-card hover:bg-accent/30 transition-colors cursor-pointer"
            title="Open chat"
          >
            <PanelRightOpen size={16} className="text-muted-foreground" />
          </button>
        )}
      </div>

      {/* Dialogs */}
      <SaveDialog
        open={showSave}
        onClose={() => setShowSave(false)}
        currentId={currentId}
        currentName={currentName}
        onSaved={handleSaved}
      />
      <LoadDialog
        open={showLoad}
        onClose={() => setShowLoad(false)}
        onLoaded={handleLoaded}
      />
      <CodeDialog
        open={showCode}
        onClose={() => setShowCode(false)}
      />
      <ExportDialog
        open={showExport}
        onClose={() => setShowExport(false)}
        workflowName={currentName || 'MyWorkflow'}
      />
      <WorkflowSettingsDialog
        open={showSettings}
        onClose={() => setShowSettings(false)}
      />
      <TemplatesDialog
        open={showTemplates}
        onClose={() => setShowTemplates(false)}
        onSelect={(template: TemplateInfo) => {
          const workflow = getTemplateWorkflow(template.id)
          if (workflow) {
            useWorkflowStore.getState().setWorkflow(workflow.nodes, workflow.edges)
            setCurrentId(null)
            setCurrentName(template.name)
          }
        }}
        onSelectCustom={(tpl) => {
          useWorkflowStore.getState().setWorkflow(tpl.nodes, tpl.edges)
          setCurrentId(null)
          setCurrentName(tpl.name)
        }}
      />
    </div>
  )
}

/** 工具列按鈕 — 統一高度，icon-only 正方形 */
function TbBtn({ onClick, icon: Icon, label, title }: {
  onClick: () => void; icon: React.ComponentType<{ size?: number }>; label?: string; title?: string
}) {
  return (
    <button onClick={onClick} title={title ?? label}
      className={`flex items-center justify-center gap-1 rounded-md border border-border bg-secondary text-[11px] text-muted-foreground hover:bg-accent hover:text-foreground transition-colors cursor-pointer h-7 ${
        label ? 'px-2.5' : 'w-7'
      }`}>
      <Icon size={13} />
      {label}
    </button>
  )
}
