import type { NodeProps } from '@xyflow/react'
import { NodeShell } from './shared/NodeShell'
import { NODE_REGISTRY } from './registry'
import type { AgentNodeData } from '@/types/workflow'

const config = NODE_REGISTRY.agent

export function AgentNode({ data, selected }: NodeProps & { data: AgentNodeData }) {
  return (
    <NodeShell {...config} title={data.name} subtitle={data.provider && data.model ? `${data.provider} / ${data.model}` : undefined} selected={selected}>
      {data.instructions && <p className="truncate">{data.instructions}</p>}
      {data.tools?.length > 0 && (
        <div className="flex flex-wrap gap-0.5 mt-0.5">
          {data.tools.map(t => (
            <span key={t} className="rounded bg-yellow-500/15 px-1 text-[8px] text-yellow-400">{t}</span>
          ))}
        </div>
      )}
    </NodeShell>
  )
}
