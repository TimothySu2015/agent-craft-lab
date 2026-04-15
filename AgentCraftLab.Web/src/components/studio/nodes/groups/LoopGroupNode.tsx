/**
 * LoopGroupNode — 迴圈群組容器。
 * 虛線邊框 + 循環箭頭圖示。
 */
import { RefreshCw } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { GroupNodeShell } from './GroupNodeShell'

export function LoopGroupNode() {
  const { t } = useTranslation('studio')
  return (
    <GroupNodeShell
      label={t('node.loopLabel')}
      icon={RefreshCw}
      borderClass="border-dashed border-orange-500/30"
      bgClass="bg-orange-500/5"
      lineColor="#f97316"
    />
  )
}
