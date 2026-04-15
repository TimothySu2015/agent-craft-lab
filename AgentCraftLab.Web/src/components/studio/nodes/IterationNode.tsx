import type { NodeProps } from '@xyflow/react'
import { useTranslation } from 'react-i18next'
import { NodeShell } from './shared/NodeShell'
import { NODE_REGISTRY } from './registry'
import type { IterationNodeData } from '@/types/workflow'

const config = NODE_REGISTRY.iteration

export function IterationNode({ data, selected }: NodeProps & { data: IterationNodeData }) {
  const { t } = useTranslation('studio')
  return (
    <NodeShell {...config} title={data.name} subtitle={data.split} selected={selected}>
      <p>{t('node.maxItems')}{data.maxItems}{data.maxConcurrency && data.maxConcurrency > 1 ? ` · ×${data.maxConcurrency}` : ''}</p>
    </NodeShell>
  )
}
