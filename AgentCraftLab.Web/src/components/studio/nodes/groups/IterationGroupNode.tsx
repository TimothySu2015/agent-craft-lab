/**
 * IterationGroupNode — foreach 群組容器。
 * 虛線邊框 + repeat 圖示。
 */
import { Repeat } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { GroupNodeShell } from './GroupNodeShell'

export function IterationGroupNode() {
  const { t } = useTranslation('studio')
  return (
    <GroupNodeShell
      label={t('node.foreach')}
      icon={Repeat}
      borderClass="border-dashed border-teal-500/30"
      bgClass="bg-teal-500/5"
      lineColor="#14b8a6"
    />
  )
}
