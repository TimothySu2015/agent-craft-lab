/**
 * 自訂範本 store — 雙層儲存：
 * 1. 後端 /api/templates（SQLite/MongoDB 持久化，主要來源）
 * 2. localStorage 作為離線快取 + fallback
 *
 * 啟動時從後端同步，儲存時同步寫入後端。
 */
import { create } from 'zustand'
import { persist } from 'zustand/middleware'
import type { Node, Edge } from '@xyflow/react'
import type { NodeData } from '@/types/workflow'
import { api } from '@/lib/api'

export interface CustomTemplate {
  id: string
  name: string
  description: string
  createdAt: string
  nodes: Node<NodeData>[]
  edges: Edge[]
  /** 後端 template document ID */
  backendId?: string
}

interface CustomTemplatesState {
  templates: CustomTemplate[]
  /** 從後端載入自訂範本（啟動時呼叫） */
  loadFromBackend: () => Promise<void>
  addTemplate: (name: string, description: string, nodes: Node<NodeData>[], edges: Edge[]) => Promise<void>
  removeTemplate: (id: string) => Promise<void>
}

export const useCustomTemplatesStore = create<CustomTemplatesState>()(
  persist(
    (set, get) => ({
      templates: [],

      loadFromBackend: async () => {
        try {
          const docs = await api.templates.list()
          const backendTemplates: CustomTemplate[] = docs.map((doc) => {
            let nodes: Node<NodeData>[] = []
            let edges: Edge[] = []
            if (doc.workflowJson) {
              try {
                const parsed = JSON.parse(doc.workflowJson)
                nodes = parsed.nodes ?? []
                edges = parsed.edges ?? []
              } catch { /* invalid JSON, skip */ }
            }
            return {
              id: doc.id,
              name: doc.name,
              description: doc.description,
              createdAt: doc.createdAt,
              nodes,
              edges,
              backendId: doc.id,
            }
          })

          // 合併：後端範本 + localStorage 中尚未同步的範本
          set((s) => {
            const backendIds = new Set(backendTemplates.map((t) => t.backendId))
            const localOnly = s.templates.filter((t) => !t.backendId && !backendIds.has(t.id))
            return { templates: [...backendTemplates, ...localOnly] }
          })
        } catch {
          // 後端不可用，使用 localStorage fallback
        }
      },

      addTemplate: async (name, description, nodes, edges) => {
        const clonedNodes = structuredClone(nodes).filter((n: any) => !n.type?.endsWith('-group'))
        const clonedEdges = structuredClone(edges)
        const workflowJson = JSON.stringify({ nodes: clonedNodes, edges: clonedEdges })

        try {
          const doc = await api.templates.create({
            name,
            description,
            category: 'My Templates',
            workflowJson,
          })
          const tpl: CustomTemplate = {
            id: doc.id,
            name: doc.name,
            description: doc.description,
            createdAt: doc.createdAt,
            nodes: clonedNodes,
            edges: clonedEdges,
            backendId: doc.id,
          }
          set((s) => ({ templates: [tpl, ...s.templates] }))
        } catch {
          // Fallback：只存 localStorage
          const tpl: CustomTemplate = {
            id: `custom-${Date.now()}`,
            name,
            description,
            createdAt: new Date().toISOString(),
            nodes: clonedNodes,
            edges: clonedEdges,
          }
          set((s) => ({ templates: [tpl, ...s.templates] }))
        }
      },

      removeTemplate: async (id) => {
        const tpl = get().templates.find((t) => t.id === id)
        if (tpl?.backendId) {
          try { await api.templates.delete(tpl.backendId) } catch { /* ignore */ }
        }
        set((s) => ({ templates: s.templates.filter((t) => t.id !== id) }))
      },
    }),
    { name: 'agentcraftlab-custom-templates' },
  ),
)
