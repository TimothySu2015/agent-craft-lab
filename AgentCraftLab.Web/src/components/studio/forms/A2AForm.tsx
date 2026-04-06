import { Field } from '../PropertiesPanel'
import type { A2ANodeData, NodeData } from '@/types/workflow'

interface Props {
  data: A2ANodeData;
  onUpdate: (partial: Partial<NodeData>) => void;
}

export function A2AForm({ data, onUpdate }: Props) {
  return (
    <>
      <Field label="A2A URL">
        <input
          className="field-input font-mono text-[10px]"
          value={data.a2AUrl}
          onChange={(e) => onUpdate({ a2AUrl: e.target.value })}
          placeholder="http://localhost:5001"
        />
      </Field>

      <Field label="Format">
        <select
          className="field-input"
          value={data.a2AFormat}
          onChange={(e) => onUpdate({ a2AFormat: e.target.value })}
        >
          <option value="auto">Auto Detect</option>
          <option value="google">Google A2A</option>
          <option value="microsoft">Microsoft A2A</option>
        </select>
      </Field>

      <Field label="Instructions">
        <textarea
          className="field-textarea"
          value={data.instructions}
          onChange={(e) => onUpdate({ instructions: e.target.value })}
          rows={2}
        />
      </Field>
    </>
  )
}
