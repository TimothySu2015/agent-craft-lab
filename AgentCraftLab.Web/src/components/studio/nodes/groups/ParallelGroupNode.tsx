/**
 * ParallelGroupNode — 並行群組容器。
 * 雙線邊框 + 動態分支標籤。
 */
import { Columns3 } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { GroupNodeShell } from './GroupNodeShell'

export function ParallelGroupNode() {
  const { t } = useTranslation('studio')
  return (
    <GroupNodeShell
      label={t('node.parallelLabel')}
      icon={Columns3}
      borderClass="border-double border-[3px] border-cyan-500/30"
      bgClass="bg-cyan-500/5"
      lineColor="#06b6d4"
    />
  )
}
