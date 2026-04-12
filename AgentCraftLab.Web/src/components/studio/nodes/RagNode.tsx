import type { NodeProps } from '@xyflow/react'
import { NodeShell } from './shared/NodeShell'
import { NODE_REGISTRY } from './registry'
import type { RagNodeData } from '@/types/workflow'

const config = NODE_REGISTRY.rag

export function RagNode({ data, selected }: NodeProps & { data: RagNodeData }) {
  const hasKb = (data.knowledgeBaseIds ?? []).length > 0
  const topK = data.rag?.topK ?? 5
  const searchMode = data.rag?.searchMode ?? 'hybrid'
  return (
    <NodeShell {...config} title={data.name} subtitle={hasKb ? 'Knowledge Base' : 'No KB selected'} selected={selected}>
      <p>TopK: {topK} · {searchMode}</p>
    </NodeShell>
  )
}
