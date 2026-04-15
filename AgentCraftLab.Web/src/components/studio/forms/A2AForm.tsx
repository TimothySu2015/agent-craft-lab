import { useTranslation } from 'react-i18next'
import { Field } from '../PropertiesPanel'
import type { A2ANodeData, A2AFormat, NodeData } from '@/types/workflow'

interface Props {
  data: A2ANodeData;
  onUpdate: (partial: Partial<NodeData>) => void;
}

export function A2AForm({ data, onUpdate }: Props) {
  const { t } = useTranslation('studio')
  return (
    <>
      <Field label={t('form.a2aUrl')}>
        <input
          className="field-input font-mono text-[10px]"
          value={data.url}
          onChange={(e) => onUpdate({ url: e.target.value })}
          placeholder="http://localhost:5001"
        />
      </Field>

      <Field label={t('form.format')}>
        <select
          className="field-input"
          value={data.format}
          onChange={(e) => onUpdate({ format: e.target.value as A2AFormat })}
        >
          <option value="auto">{t('form.autoDetect')}</option>
          <option value="google">{t('form.googleA2a')}</option>
          <option value="microsoft">{t('form.microsoftA2a')}</option>
        </select>
      </Field>

      <Field label={t('form.instructions')}>
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
