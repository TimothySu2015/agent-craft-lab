import type { NodeProps } from '@xyflow/react'
import { NodeShell } from './shared/NodeShell'
import { NODE_REGISTRY } from './registry'
import type { AutonomousNodeData } from '@/types/workflow'

const config = NODE_REGISTRY.autonomous

export function AutonomousNode({ data, selected }: NodeProps & { data: AutonomousNodeData }) {
  return (
    <NodeShell {...config} title={data.name} subtitle={`${data.provider} / ${data.model}`} selected={selected}>
      {data.instructions && <p className="truncate">{data.instructions}</p>}
    </NodeShell>
  )
}
