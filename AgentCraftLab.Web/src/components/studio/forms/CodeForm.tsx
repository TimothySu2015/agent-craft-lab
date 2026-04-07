import { useTranslation } from 'react-i18next'
import { Field } from '../PropertiesPanel'
import { ExpandableTextarea } from '@/components/shared/ExpandableTextarea'
import { MonacoCodeEditor } from '@/components/shared/MonacoCodeEditor'
import type { CodeNodeData, NodeData } from '@/types/workflow'

interface Props {
  data: CodeNodeData
  onUpdate: (partial: Partial<NodeData>) => void
}

const SCRIPT_LANGUAGES = [
  { value: 'javascript', label: 'JavaScript' },
  { value: 'csharp', label: 'C#' },
] as const

export function CodeForm({ data, onUpdate }: Props) {
  const { t } = useTranslation('studio')
  const scriptLang = data.scriptLanguage || 'javascript'
  const isCSharp = scriptLang === 'csharp'

  return (
    <>
      <Field label={t('form.transformType')}>
        <select className="field-input" value={data.transformType} onChange={(e) => onUpdate({ transformType: e.target.value })}>
          <option value="template">{t('transform.template')}</option>
          <option value="regex-extract">{t('transform.regexExtract')}</option>
          <option value="regex-replace">{t('transform.regexReplace')}</option>
          <option value="json-path">{t('transform.jsonPath')}</option>
          <option value="trim">{t('transform.trim')}</option>
          <option value="split-take">{t('transform.splitTake')}</option>
          <option value="upper">{t('transform.upper')}</option>
          <option value="lower">{t('transform.lower')}</option>
          <option value="script">{t('transform.script')}</option>
        </select>
      </Field>

      {data.transformType === 'template' && (
        <Field label={t('form.template')}>
          <ExpandableTextarea
            className="font-mono text-[10px]"
            value={data.template}
            onChange={(v) => onUpdate({ template: v })}
            rows={4}
            placeholder="{{input}}"
            label="Code — Handlebars Template"
            language="handlebars"
          />
          <p className="text-[8px] text-muted-foreground mt-0.5">Use {'{{input}}'} for previous node output. Supports {'{{#each}}'} for arrays.</p>
        </Field>
      )}

      {data.transformType === 'script' && (
        <>
          {/* Language Selector */}
          <Field label={t('script.languageLabel')}>
            <select className="field-input" value={scriptLang}
              onChange={(e) => {
                onUpdate({ scriptLanguage: e.target.value, template: '' })
              }}>
              {SCRIPT_LANGUAGES.map((lang) => (
                <option key={lang.value} value={lang.value}>{lang.label}</option>
              ))}
            </select>
          </Field>

          {/* Monaco Preview + Script Studio */}
          <Field label={isCSharp ? t('script.csharpCodeLabel') : t('script.codeLabel')}>
            <MonacoCodeEditor
              value={data.template}
              onChange={(v) => onUpdate({ template: v })}
              language={isCSharp ? 'csharp' : 'javascript'}
              label={`Code — ${isCSharp ? 'C#' : 'JavaScript'}`}
            />
          </Field>
        </>
      )}

      {(data.transformType === 'regex-extract' || data.transformType === 'regex-replace') && (
        <>
          <Field label={t('form.pattern')}>
            <input className="field-input font-mono text-[10px]" value={data.pattern}
              onChange={(e) => onUpdate({ pattern: e.target.value })} placeholder="(\d+)" />
          </Field>
          {data.transformType === 'regex-replace' && (
            <Field label={t('form.replacement')}>
              <input className="field-input font-mono text-[10px]" value={data.replacement}
                onChange={(e) => onUpdate({ replacement: e.target.value })} placeholder="$1" />
            </Field>
          )}
        </>
      )}

      {data.transformType === 'json-path' && (
        <Field label={t('form.pattern')}>
          <input className="field-input font-mono text-[10px]" value={data.pattern}
            onChange={(e) => onUpdate({ pattern: e.target.value })} placeholder="$.data.items[0].name" />
        </Field>
      )}

      {data.transformType === 'trim' && (
        <Field label={t('form.maxLength')}>
          <input type="number" className="field-input" value={data.maxLength ?? 0} min={0}
            onChange={(e) => onUpdate({ maxLength: Number(e.target.value) })} />
          <p className="text-[8px] text-muted-foreground mt-0.5">0 = trim whitespace only</p>
        </Field>
      )}

      {data.transformType === 'split-take' && (
        <>
          <Field label={t('form.delimiter')}>
            <input className="field-input font-mono text-[10px]" value={data.delimiter ?? '\\n'}
              onChange={(e) => onUpdate({ delimiter: e.target.value })} placeholder="\n" />
          </Field>
          <Field label={t('form.takeIndex')}>
            <input type="number" className="field-input" value={data.splitIndex ?? 0} min={0}
              onChange={(e) => onUpdate({ splitIndex: Number(e.target.value) })} />
            <p className="text-[8px] text-muted-foreground mt-0.5">0-based index of the part to take</p>
          </Field>
        </>
      )}
    </>
  )
}
