/**
 * FlowPlan parallel 展開 — 將 branches 物件陣列轉為獨立 agent 節點 + connections。
 * 同時補齊 FlowPlan 沒有的 sequential connections（Done port → 下一節點 → 下一節點...）。
 *
 * FlowPlan 格式：{ nodeType: "parallel", branches: [{name, goal, tools}] }（無 connections，假設順序執行）
 * AI Build 格式：parallel 節點（branches: "名稱1,名稱2"）+ 獨立 agent 節點 + 完整 connections
 */
export function expandFlowPlanParallel(spec: { nodes: any[]; connections?: any[] }) {
  const originalHasConnections = Array.isArray(spec.connections) && spec.connections.length > 0
  const expanded: any[] = []
  const connections: any[] = spec.connections ?? []

  // 記錄每個「原始節點」在展開後的 index（用於生成 sequential connections）
  const originalToExpandedIndex: number[] = []

  for (const node of spec.nodes) {
    const type = node.type || node.nodeType || 'agent'
    const branches = node.branches ?? node.data?.branches

    if (type === 'parallel' && Array.isArray(branches) && branches.length > 0 && typeof branches[0] === 'object') {
      const parallelIndex = expanded.length
      originalToExpandedIndex.push(parallelIndex)

      const branchNames = branches.map((b: any) => b.name)
      expanded.push({
        type: 'parallel',
        name: node.name || 'Parallel',
        branches: branchNames.join(','),
        mergeStrategy: node.mergeStrategy || node.data?.mergeStrategy || 'labeled',
      })

      // 每個 branch 展開為獨立 agent 節點
      for (let bi = 0; bi < branches.length; bi++) {
        const branch = branches[bi]
        const agentIndex = expanded.length
        expanded.push({
          type: 'agent',
          nodeType: 'agent',
          name: branch.name,
          instructions: branch.goal || branch.instructions || '',
          tools: branch.tools ?? [],
        })
        connections.push({
          from: parallelIndex,
          to: agentIndex,
          fromOutput: `output_${bi + 1}`,
        })
      }
    } else {
      originalToExpandedIndex.push(expanded.length)
      expanded.push(node)
    }
  }

  // FlowPlan 沒有 connections 時，補齊 sequential connections（含 parallel Done port）
  if (!originalHasConnections && originalToExpandedIndex.length > 1) {
    for (let i = 0; i < originalToExpandedIndex.length - 1; i++) {
      const fromIdx = originalToExpandedIndex[i]
      const toIdx = originalToExpandedIndex[i + 1]
      const fromNode = expanded[fromIdx]
      const fromType = fromNode.type || fromNode.nodeType || 'agent'

      if (fromType === 'parallel') {
        // Parallel → 下一個主節點：用 Done port
        const branchCount = (fromNode.branches || '').split(',').filter(Boolean).length
        connections.push({
          from: fromIdx,
          to: toIdx,
          fromOutput: `output_${branchCount + 1}`,
        })
      } else {
        // 一般節點 → 下一個節點
        connections.push({
          from: fromIdx,
          to: toIdx,
        })
      }
    }
  }

  spec.nodes = expanded
  spec.connections = connections
}
