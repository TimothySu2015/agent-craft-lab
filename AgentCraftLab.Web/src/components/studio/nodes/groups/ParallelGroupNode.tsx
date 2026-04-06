/**
 * ParallelGroupNode — 並行群組容器。
 * 雙線邊框 + 動態分支標籤。
 */
import { Columns3 } from 'lucide-react'
import { GroupNodeShell } from './GroupNodeShell'

export function ParallelGroupNode() {
  return (
    <GroupNodeShell
      label="Parallel"
      icon={Columns3}
      borderClass="border-double border-[3px] border-cyan-500/30"
      bgClass="bg-cyan-500/5"
      lineColor="#06b6d4"
    />
  )
}
