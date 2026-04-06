/**
 * ConditionGroupNode — IF/ELSE 群組容器。
 * 虛線邊框 + 雙欄（True / False）指示標籤。
 */
import { GitBranch } from 'lucide-react'
import { GroupNodeShell } from './GroupNodeShell'

export function ConditionGroupNode() {
  return (
    <GroupNodeShell
      label="IF / ELSE"
      icon={GitBranch}
      borderClass="border-dashed border-amber-500/30"
      bgClass="bg-amber-500/5"
      lineColor="#f59e0b"
    >
      <div className="flex gap-3 text-[9px]">
        <span className="text-green-400 font-medium">True</span>
        <span className="text-muted-foreground">/</span>
        <span className="text-red-400 font-medium">False</span>
      </div>
    </GroupNodeShell>
  )
}
