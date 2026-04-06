import type { NodeProps } from '@xyflow/react'
import { NodeShell } from './shared/NodeShell'
import { NODE_REGISTRY } from './registry'
import type { HttpRequestNodeData } from '@/types/workflow'

const config = NODE_REGISTRY['http-request']

export function HttpRequestNode({ data, selected }: NodeProps & { data: HttpRequestNodeData }) {
  return (
    <NodeShell {...config} title={data.name} subtitle="HTTP" selected={selected}>
      {data.httpApiId
        ? <p className="truncate">{data.httpApiId}</p>
        : data.httpUrl && <p className="truncate">{data.httpMethod ?? 'GET'} {data.httpUrl}</p>}
    </NodeShell>
  )
}
