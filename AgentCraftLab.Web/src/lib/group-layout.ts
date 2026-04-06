/**
 * Group Layout — 計算群組內部節點的排版位置和群組尺寸。
 * 純邏輯函式，從 workflow-store.ts 抽出以降低複雜度並支援獨立測試。
 */
import type { GroupDef } from './group-builder'
import type { LayoutDirection } from '@/types/workflow'

export const GROUP_PAD = 20
export const GROUP_HEADER_HEIGHT = 44
const NODE_W = 280
const NODE_H = 100
const GAP = 30

export interface GroupLayoutResult {
  positions: Map<string, { x: number; y: number }>
  dimensions: { w: number; h: number }
}

/**
 * 計算群組內部的節點排版位置和群組尺寸。
 * LR：控制節點在左，分支在右。TB：控制節點在上，分支在下。
 */
export function layoutGroupInternals(group: GroupDef, direction: LayoutDirection): GroupLayoutResult {
  const isTB = direction === 'TB'

  if (group.type === 'parallel-group') {
    return isTB ? layoutParallelTB(group) : layoutParallelLR(group)
  }
  if (group.type === 'condition-group' && group.branches.length === 2) {
    return isTB ? layoutConditionTB(group) : layoutConditionLR(group)
  }
  // Loop / Iteration / single-branch Condition
  return isTB ? layoutBodyTB(group) : layoutBodyLR(group)
}

// ─── Parallel ───

function layoutParallelLR(g: GroupDef): GroupLayoutResult {
  const positions = new Map<string, { x: number; y: number }>()
  positions.set(g.controlNodeId, { x: GROUP_PAD, y: GROUP_HEADER_HEIGHT })

  const branchStartX = GROUP_PAD + NODE_W + GAP * 2
  let maxChainLen = 0

  for (let bi = 0; bi < g.branches.length; bi++) {
    const ids = [...g.branches[bi].nodeIds]
    const y = GROUP_HEADER_HEIGHT + bi * (NODE_H + GAP)
    for (let ci = 0; ci < ids.length; ci++) {
      positions.set(ids[ci], { x: branchStartX + ci * (NODE_W + GAP), y })
    }
    maxChainLen = Math.max(maxChainLen, ids.length)
  }

  return {
    positions,
    dimensions: {
      w: branchStartX + maxChainLen * (NODE_W + GAP) + GROUP_PAD,
      h: GROUP_HEADER_HEIGHT + g.branches.length * (NODE_H + GAP) + GROUP_PAD,
    },
  }
}

function layoutParallelTB(g: GroupDef): GroupLayoutResult {
  const positions = new Map<string, { x: number; y: number }>()
  positions.set(g.controlNodeId, { x: GROUP_PAD, y: GROUP_HEADER_HEIGHT })

  const branchStartY = GROUP_HEADER_HEIGHT + NODE_H + GAP * 2
  let maxDepth = 0

  for (let bi = 0; bi < g.branches.length; bi++) {
    const ids = [...g.branches[bi].nodeIds]
    const x = GROUP_PAD + bi * (NODE_W + GAP)
    for (let ci = 0; ci < ids.length; ci++) {
      positions.set(ids[ci], { x, y: branchStartY + ci * (NODE_H + GAP) })
    }
    maxDepth = Math.max(maxDepth, ids.length)
  }

  return {
    positions,
    dimensions: {
      w: GROUP_PAD * 2 + g.branches.length * (NODE_W + GAP),
      h: branchStartY + maxDepth * (NODE_H + GAP) + GROUP_PAD,
    },
  }
}

// ─── Condition (dual branch) ───

function layoutConditionLR(g: GroupDef): GroupLayoutResult {
  const positions = new Map<string, { x: number; y: number }>()
  positions.set(g.controlNodeId, { x: GROUP_PAD, y: GROUP_HEADER_HEIGHT })

  // True/False 分支在控制節點下方，各佔一列
  const branchStartY = GROUP_HEADER_HEIGHT + NODE_H + GAP
  let maxChainLen = 0

  for (let bi = 0; bi < 2; bi++) {
    const ids = [...g.branches[bi].nodeIds]
    const y = branchStartY + bi * (NODE_H + GAP)
    for (let ci = 0; ci < ids.length; ci++) {
      positions.set(ids[ci], { x: GROUP_PAD + ci * (NODE_W + GAP), y })
    }
    maxChainLen = Math.max(maxChainLen, ids.length)
  }

  const bodyRowW = maxChainLen * (NODE_W + GAP)
  return {
    positions,
    dimensions: {
      w: Math.max(NODE_W, bodyRowW) + GROUP_PAD * 2,
      h: branchStartY + 2 * (NODE_H + GAP) + GROUP_PAD,
    },
  }
}

function layoutConditionTB(g: GroupDef): GroupLayoutResult {
  const positions = new Map<string, { x: number; y: number }>()
  positions.set(g.controlNodeId, { x: GROUP_PAD, y: GROUP_HEADER_HEIGHT })

  const bodyStartY = GROUP_HEADER_HEIGHT + NODE_H + GAP
  let maxLen = 0

  for (let bi = 0; bi < 2; bi++) {
    const ids = [...g.branches[bi].nodeIds]
    const colX = GROUP_PAD + bi * (NODE_W + GAP * 2)
    for (let ci = 0; ci < ids.length; ci++) {
      positions.set(ids[ci], { x: colX, y: bodyStartY + ci * (NODE_H + GAP) })
    }
    maxLen = Math.max(maxLen, ids.length)
  }

  return {
    positions,
    dimensions: {
      w: GROUP_PAD * 2 + 2 * NODE_W + GAP * 2,
      h: bodyStartY + maxLen * (NODE_H + GAP) + GROUP_PAD,
    },
  }
}

// ─── Loop / Iteration / single-branch ───

function layoutBodyLR(g: GroupDef): GroupLayoutResult {
  const positions = new Map<string, { x: number; y: number }>()
  positions.set(g.controlNodeId, { x: GROUP_PAD, y: GROUP_HEADER_HEIGHT })

  // Body 在控制節點下方水平排列，避免 Exit 邊線穿過 body 節點
  const bodyStartY = GROUP_HEADER_HEIGHT + NODE_H + GAP
  const ids = g.branches.flatMap((b) => [...b.nodeIds])
  for (let ci = 0; ci < ids.length; ci++) {
    positions.set(ids[ci], { x: GROUP_PAD + ci * (NODE_W + GAP), y: bodyStartY })
  }

  const bodyRowW = ids.length * (NODE_W + GAP)
  return {
    positions,
    dimensions: {
      w: Math.max(NODE_W, bodyRowW) + GROUP_PAD * 2,
      h: bodyStartY + NODE_H + GROUP_PAD,
    },
  }
}

function layoutBodyTB(g: GroupDef): GroupLayoutResult {
  const positions = new Map<string, { x: number; y: number }>()
  positions.set(g.controlNodeId, { x: GROUP_PAD, y: GROUP_HEADER_HEIGHT })

  const bodyStartY = GROUP_HEADER_HEIGHT + NODE_H + GAP
  const ids = g.branches.flatMap((b) => [...b.nodeIds])
  for (let ci = 0; ci < ids.length; ci++) {
    positions.set(ids[ci], { x: GROUP_PAD, y: bodyStartY + ci * (NODE_H + GAP) })
  }

  return {
    positions,
    dimensions: {
      w: NODE_W + GROUP_PAD * 2,
      h: bodyStartY + ids.length * (NODE_H + GAP) + GROUP_PAD,
    },
  }
}
