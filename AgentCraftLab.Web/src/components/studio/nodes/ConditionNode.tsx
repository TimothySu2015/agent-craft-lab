import type { NodeProps } from '@xyflow/react'
import { useTranslation } from 'react-i18next'
import { NodeShell } from './shared/NodeShell'
import { NODE_REGISTRY } from './registry'
import type { ConditionNodeData } from '@/types/workflow'

const config = NODE_REGISTRY.condition

export function ConditionNode({ data, selected }: NodeProps & { data: ConditionNodeData }) {
  const { t } = useTranslation('studio')
  return (
    <NodeShell {...config} title={data.name} subtitle={data.condition?.kind} selected={selected}>
      {data.condition?.value && <p className="truncate">{data.condition.value}</p>}
      <div className="mt-0.5">
        <span className="text-green-400">{t('node.branchTrue')}</span> / <span className="text-red-400">{t('node.branchFalse')}</span>
      </div>
    </NodeShell>
  )
}
