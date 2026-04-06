import { useTranslation } from 'react-i18next'
import { X } from 'lucide-react'
import { useWorkflowStore } from '@/stores/workflow-store'
import { NODE_REGISTRY, NODE_COLORS } from './nodes/registry'
import { AgentForm } from './forms/AgentForm'
import { ConditionForm } from './forms/ConditionForm'
import { HumanForm } from './forms/HumanForm'
import { CodeForm } from './forms/CodeForm'
import { A2AForm } from './forms/A2AForm'
import { AutonomousForm } from './forms/AutonomousForm'
import { SimpleForm } from './forms/SimpleForm'
import type { NodeData } from '@/types/workflow'

export function PropertiesPanel() {
  const { t } = useTranslation('studio')
  const selectedNodeId = useWorkflowStore((s) => s.selectedNodeId)
  const nodes = useWorkflowStore((s) => s.nodes)
  const updateNodeData = useWorkflowStore((s) => s.updateNodeData)
  const setSelectedNode = useWorkflowStore((s) => s.setSelectedNode)

  const selectedNode = nodes.find((n) => n.id === selectedNodeId)
  if (!selectedNode || !selectedNodeId) return null

  const data = selectedNode.data as NodeData
  const config = NODE_REGISTRY[data.type]
  if (!config) return null

  const colors = NODE_COLORS[config.color] ?? NODE_COLORS.blue

  const onUpdate = (partial: Partial<NodeData>) => {
    updateNodeData(selectedNodeId, partial)
  }

  return (
    <div className="absolute top-2 right-2 z-20 w-[260px] max-h-[calc(100%-16px)] overflow-y-auto rounded-lg border border-border bg-card p-3 shadow-xl">
      {/* Header */}
      <div className="flex items-center justify-between mb-3">
        <div className="flex items-center gap-2">
          <div className={`flex h-5 w-5 items-center justify-center rounded ${colors.iconBg}`}>
            <config.icon size={11} className={colors.iconText} />
          </div>
          <span className="text-xs font-semibold text-foreground">{data.name}</span>
        </div>
        <button
          onClick={() => setSelectedNode(null)}
          className="text-muted-foreground hover:text-foreground cursor-pointer"
        >
          <X size={14} />
        </button>
      </div>

      {/* Name field (all nodes) */}
      <Field label={t('properties', { defaultValue: 'Name' })}>
        <input
          className="field-input"
          value={data.name}
          onChange={(e) => onUpdate({ name: e.target.value })}
        />
      </Field>

      {/* Type-specific form */}
      {data.type === 'agent' && <AgentForm data={data} onUpdate={onUpdate} />}
      {data.type === 'condition' && <ConditionForm data={data} onUpdate={onUpdate} />}
      {data.type === 'loop' && <ConditionForm data={data} onUpdate={onUpdate} />}
      {data.type === 'human' && <HumanForm data={data} onUpdate={onUpdate} />}
      {data.type === 'code' && <CodeForm data={data} onUpdate={onUpdate} />}
      {data.type === 'a2a-agent' && <A2AForm data={data} onUpdate={onUpdate} />}
      {data.type === 'autonomous' && <AutonomousForm data={data} onUpdate={onUpdate} />}
      {(data.type === 'router' || data.type === 'iteration' || data.type === 'parallel' ||
        data.type === 'rag' || data.type === 'tool' || data.type === 'http-request') && (
        <SimpleForm data={data} onUpdate={onUpdate} />
      )}
    </div>
  )
}

/** Reusable field wrapper */
export function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="mb-2">
      <label className="block text-[9px] font-medium uppercase tracking-wider text-muted-foreground mb-0.5">
        {label}
      </label>
      {children}
    </div>
  )
}
