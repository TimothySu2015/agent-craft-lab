import type { NodeProps } from '@xyflow/react'
import { useTranslation } from 'react-i18next'
import { NodeShell } from './shared/NodeShell'
import { NODE_REGISTRY } from './registry'
import type { RagNodeData } from '@/types/workflow'

const config = NODE_REGISTRY.rag

export function RagNode({ data, selected }: NodeProps & { data: RagNodeData }) {
  const { t } = useTranslation('studio')
  const hasKb = (data.knowledgeBaseIds ?? []).length > 0
  const topK = data.rag?.topK ?? 5
  const searchMode = data.rag?.searchMode ?? 'hybrid'
  return (
    <NodeShell {...config} title={data.name} subtitle={hasKb ? t('node.knowledgeBase') : t('node.noKbSelected')} selected={selected}>
      <p>{t('node.topK')}{topK} · {searchMode}</p>
    </NodeShell>
  )
}
