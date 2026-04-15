import type { NodeProps } from '@xyflow/react'
import { useTranslation } from 'react-i18next'
import { NodeShell } from './shared/NodeShell'
import { NODE_REGISTRY } from './registry'
import type { ParallelNodeData } from '@/types/workflow'

const config = NODE_REGISTRY.parallel

export function ParallelNode({ data, selected }: NodeProps & { data: ParallelNodeData }) {
  const { t } = useTranslation('studio')
  const branches = data.branches ?? []
  const branchStr = branches.map(b => b.name).join(', ')
  const outputs = branches.length + 1

  return (
    <NodeShell {...config} outputs={outputs} title={data.name} subtitle={data.merge} selected={selected}>
      {branchStr && <p className="truncate">{t('node.branches')}{branchStr}</p>}
    </NodeShell>
  )
}
