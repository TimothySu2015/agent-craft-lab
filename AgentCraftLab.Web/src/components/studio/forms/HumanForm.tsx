import { Field } from '../PropertiesPanel'
import type { HumanNodeData, NodeData } from '@/types/workflow'

interface Props {
  data: HumanNodeData;
  onUpdate: (partial: Partial<NodeData>) => void;
}

export function HumanForm({ data, onUpdate }: Props) {
  return (
    <>
      <Field label="Input Type">
        <select
          className="field-input"
          value={data.inputType}
          onChange={(e) => onUpdate({ inputType: e.target.value })}
        >
          <option value="text">Text</option>
          <option value="choice">Choice</option>
          <option value="approval">Approval</option>
        </select>
      </Field>

      <Field label="Prompt">
        <textarea
          className="field-textarea"
          value={data.prompt}
          onChange={(e) => onUpdate({ prompt: e.target.value })}
          rows={2}
          placeholder="Message to show the user..."
        />
      </Field>

      {data.inputType === 'choice' && (
        <Field label="Choices (comma-separated)">
          <input
            className="field-input"
            value={data.choices}
            onChange={(e) => onUpdate({ choices: e.target.value })}
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
