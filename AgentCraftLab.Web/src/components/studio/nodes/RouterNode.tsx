import type { NodeProps } from '@xyflow/react'
import { NodeShell } from './shared/NodeShell'
import { NODE_REGISTRY } from './registry'
import type { RouterNodeData } from '@/types/workflow'

const config = NODE_REGISTRY.router

export function RouterNode({ data, selected }: NodeProps & { data: RouterNodeData }) {
  const routeCount = data.routes ? data.routes.split(',').filter(Boolean).length : 0
  const outputs = Math.max(routeCount, 2)

  return (
    <NodeShell {...config} outputs={outputs} title={data.name} subtitle="Router" selected={selected}>
      {data.routes && <p className="truncate">Routes: {data.routes}</p>}
    </NodeShell>
  )
}
