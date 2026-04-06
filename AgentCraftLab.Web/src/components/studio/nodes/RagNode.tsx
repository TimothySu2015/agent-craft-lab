import type { NodeProps } from '@xyflow/react'
import { NodeShell } from './shared/NodeShell'
import { NODE_REGISTRY } from './registry'
import type { RagNodeData } from '@/types/workflow'

const config = NODE_REGISTRY.rag
const QUALITY_LABELS = ['Precise', 'Balanced', 'Broad'] as const

export function RagNode({ data, selected }: NodeProps & { data: RagNodeData }) {
  const quality = QUALITY_LABELS[data.ragSearchQuality ?? 1] ?? 'Custom'
  const hasKb = (data.knowledgeBaseIds ?? []).length > 0
  return (
    <NodeShell {...config} title={data.name} subtitle={hasKb ? 'Knowledge Base' : 'No KB selected'} selected={selected}>
      <p>TopK: {data.ragTopK ?? 5} · {quality}</p>
    </NodeShell>
  )
}
