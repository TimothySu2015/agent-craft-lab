import type { NodeProps } from '@xyflow/react'
import { NodeShell } from './shared/NodeShell'
import { NODE_REGISTRY } from './registry'
import type { HttpRequestNodeData } from '@/types/workflow'

const config = NODE_REGISTRY['http-request']

export function HttpRequestNode({ data, selected }: NodeProps & { data: HttpRequestNodeData }) {
  const spec = data.spec
  return (
    <NodeShell {...config} title={data.name} subtitle="HTTP" selected={selected}>
      {spec?.kind === 'catalog'
        ? spec.apiId && <p className="truncate">{spec.apiId}</p>
        : spec?.kind === 'inline' && spec.url && (
            <p className="truncate">{(spec.method ?? 'get').toUpperCase()} {spec.url}</p>
          )}
    </NodeShell>
  )
}
