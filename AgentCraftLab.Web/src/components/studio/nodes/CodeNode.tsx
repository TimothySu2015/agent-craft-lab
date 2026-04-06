import type { NodeProps } from '@xyflow/react'
import { NodeShell } from './shared/NodeShell'
import { NODE_REGISTRY } from './registry'
import type { CodeNodeData } from '@/types/workflow'

const config = NODE_REGISTRY.code

export function CodeNode({ data, selected }: NodeProps & { data: CodeNodeData }) {
  return (
    <NodeShell {...config} title={data.name} subtitle={data.transformType} selected={selected}>
      {data.template && data.template !== '{{input}}' && <p className="truncate font-mono">{data.template}</p>}
    </NodeShell>
  )
}
