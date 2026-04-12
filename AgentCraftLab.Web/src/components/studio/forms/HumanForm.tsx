import { Field } from '../PropertiesPanel'
import { ExpandableTextarea } from '@/components/shared/ExpandableTextarea'
import { useVariableSuggestions } from '@/hooks/useVariableSuggestions'
import type { HumanInputKind, HumanNodeData, NodeData } from '@/types/workflow'

interface Props {
  data: HumanNodeData;
  onUpdate: (partial: Partial<NodeData>) => void;
}

export function HumanForm({ data, onUpdate }: Props) {
  const suggestions = useVariableSuggestions()
  const choicesString = (data.choices ?? []).join(', ')

  return (
    <>
      <Field label="Input Type">
        <select
          className="field-input"
          value={data.kind}
          onChange={(e) => onUpdate({ kind: e.target.value as HumanInputKind })}
        >
          <option value="text">Text</option>
          <option value="choice">Choice</option>
          <option value="approval">Approval</option>
        </select>
      </Field>

      <Field label="Prompt">
        <ExpandableTextarea
          value={data.prompt}
          onChange={(v) => onUpdate({ prompt: v })}
          rows={2}
          placeholder="Message to show the user..."
          label="Human — Prompt"
          suggestions={suggestions}
        />
      </Field>

      {data.kind === 'choice' && (
        <Field label="Choices (comma-separated)">
          <input
            className="field-input"
            value={choicesString}
            onChange={(e) => onUpdate({ choices: e.target.value.split(',').map((s) => s.trim()).filter(Boolean) })}
            placeholder="Option A, Option B, Option C"
          />
        </Field>
      )}

      <Field label="Timeout (seconds, 0 = no timeout)">
        <input
          type="number"
          className="field-input"
          value={data.timeoutSeconds}
          onChange={(e) => onUpdate({ timeoutSeconds: Number(e.target.value) })}
          min={0}
        />
      </Field>
    </>
  )
}
