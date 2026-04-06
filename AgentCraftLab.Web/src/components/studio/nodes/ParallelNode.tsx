import type { NodeProps } from '@xyflow/react'
import { NodeShell } from './shared/NodeShell'
import { NODE_REGISTRY } from './registry'
import type { ParallelNodeData } from '@/types/workflow'

const config = NODE_REGISTRY.parallel

export function ParallelNode({ data, selected }: NodeProps & { data: ParallelNodeData }) {
  // branches 可能是字串（正常）或陣列（FlowPlan fallback）
  const branchStr = Array.isArray(data.branches)
    ? (data.branches as any[]).map((b: any) => typeof b === 'string' ? b : b.name).join(',')
    : data.branches ?? ''
  const branchCount = branchStr ? branchStr.split(',').filter(Boolean).length : 0
  const outputs = branchCount + 1

  return (
    <NodeShell {...config} outputs={outputs} title={data.name} subtitle={data.mergeStrategy} selected={selected}>
      {branchStr && <p className="truncate">Branches: {branchStr}</p>}
    </NodeShell>
  )
}
