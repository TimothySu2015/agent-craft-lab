import type { NodeProps } from '@xyflow/react'
import { NodeShell } from './shared/NodeShell'
import { NODE_REGISTRY } from './registry'
import type { EndNodeData } from '@/types/workflow'

const config = NODE_REGISTRY.end

export function EndNode({ data, selected }: NodeProps & { data: EndNodeData }) {
  return <NodeShell {...config} title={data.name} selected={selected} />
}
