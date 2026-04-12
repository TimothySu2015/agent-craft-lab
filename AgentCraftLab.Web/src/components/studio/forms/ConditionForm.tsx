import { Field } from '../PropertiesPanel'
import { ExpandableTextarea } from '@/components/shared/ExpandableTextarea'
import { useVariableSuggestions } from '@/hooks/useVariableSuggestions'
import type { ConditionKind, ConditionNodeData, LoopNodeData, NodeData } from '@/types/workflow'

interface Props {
  data: ConditionNodeData | LoopNodeData;
  onUpdate: (partial: Partial<NodeData>) => void;
}

export function ConditionForm({ data, onUpdate }: Props) {
  const suggestions = useVariableSuggestions()
  const isLoop = data.type === 'loop'

  return (
    <>
      <Field label="Condition Type">
        <select
          className="field-input"
          value={data.condition.kind}
          onChange={(e) => onUpdate({ condition: { ...data.condition, kind: e.target.value as ConditionKind } })}
        >
          <option value="contains">contains</option>
          <option value="regex">regex</option>
          <option value="llmJudge">llm-judge</option>
          <option value="expression">expression</option>
        </select>
      </Field>

      <Field label="Expression">
        <ExpandableTextarea
          value={data.condition.value}
          onChange={(v) => onUpdate({ condition: { ...data.condition, value: v } })}
          rows={2}
          placeholder={data.condition.kind === 'regex' ? 'regex pattern...' : 'text to check...'}
          label="Condition — Expression"
          suggestions={suggestions}
        />
      </Field>

      {isLoop && (
        <Field label="Max Iterations">
          <input
            type="number"
            className="field-input"
            value={(data as LoopNodeData).maxIterations}
            onChange={(e) => onUpdate({ maxIterations: Number(e.target.value) })}
            min={1}
            max={100}
          />
        </Field>
      )}
    </>
  )
}
