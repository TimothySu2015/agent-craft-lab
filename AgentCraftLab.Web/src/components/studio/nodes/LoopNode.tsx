import type { NodeProps } from '@xyflow/react'
import { NodeShell } from './shared/NodeShell'
import { NODE_REGISTRY } from './registry'
import type { LoopNodeData } from '@/types/workflow'

const config = NODE_REGISTRY.loop

export function LoopNode({ data, selected }: NodeProps & { data: LoopNodeData }) {
  return (
    <NodeShell {...config} title={data.name} subtitle={`${data.conditionType} (max ${data.maxIterations})`} selected={selected}>
      {data.conditionExpression && <p className="truncate">{data.conditionExpression}</p>}
    </NodeShell>
  )
}
