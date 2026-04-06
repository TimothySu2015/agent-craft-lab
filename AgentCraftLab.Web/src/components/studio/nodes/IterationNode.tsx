import type { NodeProps } from '@xyflow/react'
import { NodeShell } from './shared/NodeShell'
import { NODE_REGISTRY } from './registry'
import type { IterationNodeData } from '@/types/workflow'

const config = NODE_REGISTRY.iteration

export function IterationNode({ data, selected }: NodeProps & { data: IterationNodeData }) {
  return (
    <NodeShell {...config} title={data.name} subtitle={data.splitMode} selected={selected}>
      <p>Max: {data.maxItems}{data.maxConcurrency && data.maxConcurrency > 1 ? ` · ×${data.maxConcurrency}` : ''}</p>
    </NodeShell>
  )
}
