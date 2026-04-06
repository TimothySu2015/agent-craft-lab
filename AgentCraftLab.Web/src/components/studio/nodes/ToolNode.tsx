import type { NodeProps } from '@xyflow/react'
import { NodeShell } from './shared/NodeShell'
import { NODE_REGISTRY } from './registry'
import type { ToolNodeData } from '@/types/workflow'

const config = NODE_REGISTRY.tool

export function ToolNode({ data, selected }: NodeProps & { data: ToolNodeData }) {
  return (
    <NodeShell {...config} title={data.name} subtitle={data.toolSource} selected={selected}>
      {data.description && <p className="truncate">{data.description}</p>}
    </NodeShell>
  )
}
