/**
 * useVariableSuggestions — 建構 ExpandableTextarea 的 {{}} 自動補全建議清單。
 * 來源：系統變數 + Workflow 變數 + 節點名稱。
 */
import { useMemo } from 'react'
import { useWorkflowStore } from '@/stores/workflow-store'
import type { VariableSuggestion } from '@/components/shared/ExpandableTextarea'

const SYSTEM_VARS: VariableSuggestion[] = [
  { label: '{{sys:user_id}}', insertText: '{{sys:user_id}}', description: 'Current user ID' },
  { label: '{{sys:timestamp}}', insertText: '{{sys:timestamp}}', description: 'Current timestamp (ISO 8601)' },
  { label: '{{sys:execution_id}}', insertText: '{{sys:execution_id}}', description: 'Execution ID' },
  { label: '{{sys:workflow_name}}', insertText: '{{sys:workflow_name}}', description: 'Workflow name' },
  { label: '{{sys:user_message}}', insertText: '{{sys:user_message}}', description: 'User input message' },
]

export function useVariableSuggestions(): VariableSuggestion[] {
  const nodes = useWorkflowStore((s) => s.nodes)
  const settings = useWorkflowStore((s) => s.workflowSettings)

  return useMemo(() => {
    const suggestions: VariableSuggestion[] = [...SYSTEM_VARS]

    // Workflow 變數
    for (const v of settings.variables ?? []) {
      if (v.name) {
        suggestions.push({
          label: `{{var:${v.name}}}`,
          insertText: `{{var:${v.name}}}`,
          description: v.description || `${v.type} variable`,
        })
      }
    }

    // 節點名稱
    for (const node of nodes) {
      const name = (node.data as any)?.name
      const type = (node.data as any)?.type
      if (name && type !== 'start' && type !== 'end') {
        suggestions.push({
          label: `{{node:${name}}}`,
          insertText: `{{node:${name}}}`,
          description: `Output of ${name}`,
        })
      }
    }

    return suggestions
  }, [nodes, settings.variables])
}
