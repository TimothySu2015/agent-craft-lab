import { useCallback, useRef, useEffect, useState } from 'react'
import {
  ReactFlow,
  MiniMap,
  Controls,
  Background,
  BackgroundVariant,
  useReactFlow,
  ReactFlowProvider,
  type NodeMouseHandler,
  type EdgeMouseHandler,
  type IsValidConnection,
} from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { useTranslation } from 'react-i18next'
import { nodeTypes } from './nodes'
import { useConfirmDialog } from '@/components/shared/ConfirmDialog'
import { useWorkflowStore } from '@/stores/workflow-store'
import { NODE_REGISTRY } from './nodes/registry'
import type { NodeType } from '@/types/workflow'

interface ContextMenu {
  x: number
  y: number
  flowX: number
  flowY: number
  nodeId?: string
}

function CanvasInner() {
  const { t } = useTranslation('studio')
  const reactFlowWrapper = useRef<HTMLDivElement>(null)
  const { screenToFlowPosition, fitView } = useReactFlow()
  const [contextMenu, setContextMenu] = useState<ContextMenu | null>(null)
  const { confirm, confirmDialog } = useConfirmDialog()

  const nodes = useWorkflowStore((s) => s.nodes)
  const edges = useWorkflowStore((s) => s.edges)
  const onNodesChange = useWorkflowStore((s) => s.onNodesChange)
  const onEdgesChange = useWorkflowStore((s) => s.onEdgesChange)
  const onConnect = useWorkflowStore((s) => s.onConnect)
  const addNode = useWorkflowStore((s) => s.addNode)
  const setSelectedNode = useWorkflowStore((s) => s.setSelectedNode)
  const removeSelected = useWorkflowStore((s) => s.removeSelected)
  const duplicateSelected = useWorkflowStore((s) => s.duplicateSelected)
  const undo = useWorkflowStore((s) => s.undo)
  const redo = useWorkflowStore((s) => s.redo)
  const layout = useWorkflowStore((s) => s.layout)
  const selectedNodeId = useWorkflowStore((s) => s.selectedNodeId)

  const onDragOver = useCallback((event: React.DragEvent) => {
    event.preventDefault()
    event.dataTransfer.dropEffect = 'move'
  }, [])

  const onDrop = useCallback((event: React.DragEvent) => {
    event.preventDefault()
    const type = event.dataTransfer.getData('application/reactflow') as NodeType
    if (!type) return

    const position = screenToFlowPosition({ x: event.clientX, y: event.clientY })
    addNode(type, position)
  }, [screenToFlowPosition, addNode])

  const onNodeClick: NodeMouseHandler = useCallback((_event, node) => {
    setSelectedNode(node.id)
    setContextMenu(null)
  }, [setSelectedNode])

  const onEdgeClick: EdgeMouseHandler = useCallback((_event, edge) => {
    // 選取 edge（React Flow 內部處理 selected 狀態）
    onEdgesChange([{ id: edge.id, type: 'select', selected: true }])
    setSelectedNode(null)
    setContextMenu(null)
  }, [onEdgesChange, setSelectedNode])

  const onPaneClick = useCallback(() => {
    setSelectedNode(null)
    setContextMenu(null)
  }, [setSelectedNode])

  // 右鍵選單
  const onContextMenu = useCallback((event: React.MouseEvent) => {
    event.preventDefault()
    const flowPos = screenToFlowPosition({ x: event.clientX, y: event.clientY })
    setContextMenu({
      x: event.clientX - (reactFlowWrapper.current?.getBoundingClientRect().left ?? 0),
      y: event.clientY - (reactFlowWrapper.current?.getBoundingClientRect().top ?? 0),
      flowX: flowPos.x,
      flowY: flowPos.y,
    })
  }, [screenToFlowPosition])

  const onNodeContextMenu = useCallback((event: React.MouseEvent, node: { id: string }) => {
    event.preventDefault()
    setSelectedNode(node.id)
    const flowPos = screenToFlowPosition({ x: event.clientX, y: event.clientY })
    setContextMenu({
      x: event.clientX - (reactFlowWrapper.current?.getBoundingClientRect().left ?? 0),
      y: event.clientY - (reactFlowWrapper.current?.getBoundingClientRect().top ?? 0),
      flowX: flowPos.x,
      flowY: flowPos.y,
      nodeId: node.id,
    })
  }, [screenToFlowPosition, setSelectedNode])

  const isValidConnection: IsValidConnection = useCallback((connection) => {
    const sourceNode = nodes.find((n) => n.id === connection.source)
    const targetNode = nodes.find((n) => n.id === connection.target)
    if (!sourceNode || !targetNode) return false
    if (connection.source === connection.target) return false
    if (targetNode.type === 'start') return false
    if (sourceNode.type === 'end') return false
    const attachOnlyTypes = ['tool', 'rag']
    if (attachOnlyTypes.includes(targetNode.type ?? '') && sourceNode.type !== 'agent') return false
    if (attachOnlyTypes.includes(sourceNode.type ?? '') && targetNode.type !== 'agent') return false
    return true
  }, [nodes])

  const layoutVersion = useWorkflowStore((s) => s.layoutVersion)
  useEffect(() => {
    if (layoutVersion > 0) {
      setTimeout(() => fitView({ padding: 0.2, duration: 200 }), 50)
    }
  }, [layoutVersion, fitView])

  const onKeyDown = useCallback((event: React.KeyboardEvent) => {
    if (event.key === 'Delete' || event.key === 'Backspace') {
      // 線（edge）：直接刪除，不確認。節點：需確認。
      const hasSelectedEdge = edges.some((e) => e.selected)
      const hasSelectedNode = nodes.some((n) => n.selected)
      if (hasSelectedEdge && !hasSelectedNode) {
        onEdgesChange(edges.filter((e) => e.selected).map((e) => ({ id: e.id, type: 'remove' as const })))
      } else if (hasSelectedNode) {
        confirm(t('ctx.confirmDelete'), { confirmLabel: t('ctx.delete'), cancelLabel: t('cancel', { ns: 'common' }) }).then((ok) => { if (ok) removeSelected() })
      }
    }
    if (event.ctrlKey && event.key === 'z') { event.preventDefault(); undo() }
    if (event.ctrlKey && event.key === 'y') { event.preventDefault(); redo() }
    if (event.ctrlKey && event.key === 'd') { event.preventDefault(); duplicateSelected() }
    if (event.key === 'Escape') setContextMenu(null)
  }, [nodes, edges, onEdgesChange, removeSelected, duplicateSelected, undo, redo])

  // 常用的 addable 節點類型
  const quickAddTypes: NodeType[] = ['agent', 'condition', 'human', 'code', 'parallel', 'iteration']

  return (
    <div ref={reactFlowWrapper} className="h-full w-full bg-background relative" onKeyDown={onKeyDown} tabIndex={0}>
      <ReactFlow
        nodes={nodes}
        edges={edges}
        nodeTypes={nodeTypes}
        onNodesChange={onNodesChange}
        onEdgesChange={onEdgesChange}
        onConnect={onConnect}
        onNodeClick={onNodeClick}
        onEdgeClick={onEdgeClick}
        onPaneClick={onPaneClick}
        onDragOver={onDragOver}
        onDrop={onDrop}
        onContextMenu={onContextMenu}
        onNodeContextMenu={onNodeContextMenu}
        isValidConnection={isValidConnection}
        colorMode="dark"
        fitView
        snapToGrid
        snapGrid={[20, 20]}
        proOptions={{ hideAttribution: true }}
        edgesFocusable
        edgesReconnectable
        deleteKeyCode={null}
        defaultEdgeOptions={{ type: 'smoothstep', interactionWidth: 20, focusable: true, deletable: true, style: { stroke: 'var(--border)', strokeWidth: 2 } }}
      >
        <Background variant={BackgroundVariant.Dots} gap={20} size={1} color="var(--accent)" />
        <MiniMap
          style={{ background: 'var(--card)', borderRadius: '8px' }}
          maskColor="rgba(15,23,42,0.7)"
        />
        <Controls
          style={{ background: 'var(--card)', border: '1px solid var(--border)', borderRadius: '8px' }}
        />
      </ReactFlow>

      {/* Context Menu */}
      {contextMenu && (
        <div
          className="absolute z-50 min-w-[160px] rounded-lg border border-border bg-card shadow-xl py-1 text-xs"
          style={{ left: contextMenu.x, top: contextMenu.y }}
        >
          {contextMenu.nodeId ? (
            <>
              <CtxItem label={t('ctx.duplicate')} shortcut="Ctrl+D" onClick={() => { duplicateSelected(); setContextMenu(null) }} />
              <CtxItem label={t('ctx.delete')} shortcut="Del" onClick={() => { setContextMenu(null); confirm(t('ctx.confirmDelete'), { confirmLabel: t('ctx.delete'), cancelLabel: t('cancel', { ns: 'common' }) }).then((ok) => { if (ok) removeSelected() }) }} danger />
              <CtxDivider />
              <CtxItem label={`${t('ctx.layout')} →`} onClick={() => { layout('LR'); setContextMenu(null) }} />
              <CtxItem label={`${t('ctx.layout')} ↓`} onClick={() => { layout('TB'); setContextMenu(null) }} />
            </>
          ) : (
            <>
              <div className="px-2.5 py-1 text-[9px] text-muted-foreground uppercase tracking-wide">{t('ctx.addNode')}</div>
              {quickAddTypes.map((type) => (
                <CtxItem
                  key={type}
                  label={t(`node.${type === 'a2a-agent' ? 'a2a' : type}`)}
                  onClick={() => { addNode(type, { x: contextMenu.flowX, y: contextMenu.flowY }); setContextMenu(null) }}
                />
              ))}
              <CtxDivider />
              <CtxItem label={`${t('ctx.layout')} →`} onClick={() => { layout('LR'); setContextMenu(null) }} />
              <CtxItem label={`${t('ctx.layout')} ↓`} onClick={() => { layout('TB'); setContextMenu(null) }} />
              <CtxItem label={t('ctx.undo')} shortcut="Ctrl+Z" onClick={() => { undo(); setContextMenu(null) }} />
              <CtxItem label={t('ctx.redo')} shortcut="Ctrl+Y" onClick={() => { redo(); setContextMenu(null) }} />
            </>
          )}
        </div>
      )}

      {confirmDialog}
    </div>
  )
}

function CtxItem({ label, shortcut, onClick, danger }: { label: string; shortcut?: string; onClick: () => void; danger?: boolean }) {
  return (
    <button
      onClick={onClick}
      className={`flex w-full items-center justify-between px-2.5 py-1.5 text-left hover:bg-accent transition-colors cursor-pointer ${
        danger ? 'text-red-400 hover:bg-red-500/10' : 'text-foreground'
      }`}
    >
      <span>{label}</span>
      {shortcut && <span className="text-[9px] text-muted-foreground ml-4">{shortcut}</span>}
    </button>
  )
}

function CtxDivider() {
  return <div className="my-1 border-t border-border" />
}

export function Canvas() {
  return (
    <ReactFlowProvider>
      <CanvasInner />
    </ReactFlowProvider>
  )
}
