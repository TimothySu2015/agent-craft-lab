import type { NodeProps } from '@xyflow/react'
import { NodeShell } from './shared/NodeShell'
import { NODE_REGISTRY } from './registry'
import type { ConditionNodeData } from '@/types/workflow'

const config = NODE_REGISTRY.condition

export function ConditionNode({ data, selected }: NodeProps & { data: ConditionNodeData }) {
  return (
    <NodeShell {...config} title={data.name} subtitle={data.condition?.kind} selected={selected}>
      {data.condition?.value && <p className="truncate">{data.condition.value}</p>}
      <div className="mt-0.5">
        <span className="text-green-400">True</span> / <span className="text-red-400">False</span>
      </div>
    </NodeShell>
  )
}
