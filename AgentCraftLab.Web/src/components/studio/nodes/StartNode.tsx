import type { NodeProps } from '@xyflow/react'
import { NodeShell } from './shared/NodeShell'
import { NODE_REGISTRY } from './registry'
import type { StartNodeData } from '@/types/workflow'

const config = NODE_REGISTRY.start

export function StartNode({ data, selected }: NodeProps & { data: StartNodeData }) {
  return <NodeShell {...config} title={data.name} selected={selected} />
}
