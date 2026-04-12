import type { NodeProps } from '@xyflow/react'
import { NodeShell } from './shared/NodeShell'
import { NODE_REGISTRY } from './registry'
import type { A2ANodeData } from '@/types/workflow'

const config = NODE_REGISTRY['a2a-agent']

export function A2ANode({ data, selected }: NodeProps & { data: A2ANodeData }) {
  return (
    <NodeShell {...config} title={data.name} subtitle={data.format} selected={selected}>
      {data.url && <p className="truncate">{data.url}</p>}
    </NodeShell>
  )
}
