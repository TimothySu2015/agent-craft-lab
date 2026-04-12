import type { NodeProps } from '@xyflow/react'
import { NodeShell } from './shared/NodeShell'
import { NODE_REGISTRY } from './registry'
import type { AutonomousNodeData } from '@/types/workflow'

const config = NODE_REGISTRY.autonomous

export function AutonomousNode({ data, selected }: NodeProps & { data: AutonomousNodeData }) {
  return (
    <NodeShell {...config} title={data.name} subtitle={data.model?.provider && data.model?.model ? `${data.model.provider} / ${data.model.model}` : undefined} selected={selected}>
      {data.instructions && <p className="truncate">{data.instructions}</p>}
    </NodeShell>
  )
}
