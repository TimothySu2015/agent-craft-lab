import type { NodeProps } from '@xyflow/react'
import { NodeShell } from './shared/NodeShell'
import { NODE_REGISTRY } from './registry'
import type { A2ANodeData } from '@/types/workflow'

const config = NODE_REGISTRY['a2a-agent']

export function A2ANode({ data, selected }: NodeProps & { data: A2ANodeData }) {
  return (
    <NodeShell {...config} title={data.name} subtitle={data.a2AFormat} selected={selected}>
      {data.a2AUrl && <p className="truncate">{data.a2AUrl}</p>}
    </NodeShell>
  )
}
