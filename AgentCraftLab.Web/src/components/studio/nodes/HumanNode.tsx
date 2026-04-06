import type { NodeProps } from '@xyflow/react'
import { NodeShell } from './shared/NodeShell'
import { NODE_REGISTRY } from './registry'
import type { HumanNodeData } from '@/types/workflow'

const config = NODE_REGISTRY.human

export function HumanNode({ data, selected }: NodeProps & { data: HumanNodeData }) {
  return (
    <NodeShell {...config} title={data.name} subtitle={data.inputType} selected={selected}>
      {data.prompt && <p className="truncate">{data.prompt}</p>}
    </NodeShell>
  )
}
