/**
 * Workflow 匯出/匯入 — JSON 檔案下載與上傳。
 * 格式：{ version: 1, name, nodes, edges, createdAt }
 */
import type { Node, Edge } from '@xyflow/react'
import type { NodeData } from '@/types/workflow'

export interface WorkflowFile {
  version: number;
  name: string;
  nodes: Node<NodeData>[];
  edges: Edge[];
  createdAt: string;
}

/** 下載 workflow 為 .json 檔案 */
export function exportWorkflow(name: string, nodes: Node<NodeData>[], edges: Edge[]) {
  const realNodes = nodes.filter((n) => !n.type?.endsWith('-group'))
  const data: WorkflowFile = {
    version: 1,
    name: name || 'workflow',
    nodes: realNodes,
    edges,
    createdAt: new Date().toISOString(),
  }

  const json = JSON.stringify(data, null, 2)
  const blob = new Blob([json], { type: 'application/json' })
  const url = URL.createObjectURL(blob)

  const a = document.createElement('a')
  a.href = url
  a.download = `${data.name.replace(/[^a-zA-Z0-9\u4e00-\u9fff-]/g, '_')}.json`
  a.click()
  URL.revokeObjectURL(url)
}

/** 讀取上傳的 .json 檔案，回傳 WorkflowFile */
export function importWorkflow(file: File): Promise<WorkflowFile> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => {
      try {
        const data = JSON.parse(reader.result as string) as WorkflowFile
        if (!data.nodes || !Array.isArray(data.nodes)) {
          reject(new Error('Invalid workflow file: missing nodes'))
          return
        }
        resolve(data)
      } catch (err) {
        reject(new Error('Invalid JSON file'))
      }
    }
    reader.onerror = () => reject(new Error('Failed to read file'))
    reader.readAsText(file)
  })
}
