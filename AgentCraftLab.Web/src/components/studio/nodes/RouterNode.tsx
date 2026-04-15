import type { NodeProps } from '@xyflow/react'
import { useTranslation } from 'react-i18next'
import { NodeShell } from './shared/NodeShell'
import { NODE_REGISTRY } from './registry'
import type { RouterNodeData } from '@/types/workflow'

const config = NODE_REGISTRY.router

export function RouterNode({ data, selected }: NodeProps & { data: RouterNodeData }) {
  const { t } = useTranslation('studio')
  const routes = data.routes ?? []
  const routeStr = routes.map(r => r.name).join(', ')
  const outputs = Math.max(routes.length, 2)

  return (
    <NodeShell {...config} outputs={outputs} title={data.name} subtitle={t('node.routerSubtitle')} selected={selected}>
      {routeStr && <p className="truncate">{t('node.routes')}{routeStr}</p>}
    </NodeShell>
  )
}
