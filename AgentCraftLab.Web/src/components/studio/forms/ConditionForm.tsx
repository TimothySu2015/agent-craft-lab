import { Field } from '../PropertiesPanel'
import { ExpandableTextarea } from '@/components/shared/ExpandableTextarea'
import { useVariableSuggestions } from '@/hooks/useVariableSuggestions'
import type { ConditionNodeData, LoopNodeData, NodeData } from '@/types/workflow'

interface Props {
  data: ConditionNodeData | LoopNodeData;
  onUpdate: (partial: Partial<NodeData>) => void;
}

export function ConditionForm({ data, onUpdate }: Props) {
  const suggestions = useVariableSuggestions()

  return (
    <>
      <Field label="Condition Type">
        <select
          className="field-input"
          value={data.conditionType}
          onChange={(e) => onUpdate({ conditionType: e.target.value })}
        >
          <option value="contains">contains</option>
          <option value="regex">regex</option>
          <option value="llm-judge">llm-judge</option>
        </select>
      </Field>

      <Field label="Expression">
        <ExpandableTextarea
          value={data.conditionExpression}
          onChange={(v) => onUpdate({ conditionExpression: v })}
          rows={2}
          placeholder={data.conditionType === 'regex' ? 'regex pattern...' : 'text to check...'}
          label="Condition — Expression"
          suggestions={suggestions}
        />
      </Field>

      <Field label="Max Iterations">
        <input
          type="number"
          className="field-input"
          value={data.maxIterations}
          onChange={(e) => onUpdate({ maxIterations: Number(e.target.value) })}
          min={1}
          max={100}
        />
      </Field>
    </>
  )
}
