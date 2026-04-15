import { useTranslation } from 'react-i18next'
import { Field } from '../PropertiesPanel'
import { ExpandableTextarea } from '@/components/shared/ExpandableTextarea'
import { useVariableSuggestions } from '@/hooks/useVariableSuggestions'
import type { HumanInputKind, HumanNodeData, NodeData } from '@/types/workflow'

interface Props {
  data: HumanNodeData;
  onUpdate: (partial: Partial<NodeData>) => void;
}

export function HumanForm({ data, onUpdate }: Props) {
  const { t } = useTranslation('studio')
  const suggestions = useVariableSuggestions()
  const choicesString = (data.choices ?? []).join(', ')

  return (
    <>
      <Field label={t('form.inputType')}>
        <select
          className="field-input"
          value={data.kind}
          onChange={(e) => onUpdate({ kind: e.target.value as HumanInputKind })}
        >
          <option value="text">{t('form.inputText')}</option>
          <option value="choice">{t('form.inputChoice')}</option>
          <option value="approval">{t('form.inputApproval')}</option>
        </select>
      </Field>

      <Field label={t('form.prompt')}>
        <ExpandableTextarea
          value={data.prompt}
          onChange={(v) => onUpdate({ prompt: v })}
          rows={2}
          placeholder={t('form.humanPromptPlaceholder')}
          label={t('form.humanPrompt')}
          suggestions={suggestions}
        />
      </Field>

      {data.kind === 'choice' && (
        <Field label={t('form.choices')}>
          <input
            className="field-input"
            value={choicesString}
            onChange={(e) => onUpdate({ choices: e.target.value.split(',').map((s) => s.trim()).filter(Boolean) })}
            placeholder={t('form.choicesPlaceholder')}
          />
        </Field>
      )}

      <Field label={t('form.timeout')}>
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
