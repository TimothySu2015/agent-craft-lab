import { useTranslation } from 'react-i18next'
import { Field } from '../PropertiesPanel'
import { ExpandableTextarea } from '@/components/shared/ExpandableTextarea'
import { MonacoCodeEditor } from '@/components/shared/MonacoCodeEditor'
import type { CodeNodeData, NodeData, ScriptLanguage, TransformKind } from '@/types/workflow'

interface Props {
  data: CodeNodeData
  onUpdate: (partial: Partial<NodeData>) => void
}

const SCRIPT_LANGUAGES = [
  { value: 'javaScript', label: 'JavaScript' },
  { value: 'cSharp', label: 'C#' },
] as const

export function CodeForm({ data, onUpdate }: Props) {
  const { t } = useTranslation('studio')
  const scriptLang: ScriptLanguage = data.language ?? 'javaScript'
  const isCSharp = scriptLang === 'cSharp'

  return (
    <>
      <Field label={t('form.transformType')}>
        <select className="field-input" value={data.kind} onChange={(e) => onUpdate({ kind: e.target.value as TransformKind })}>
          <option value="template">{t('transform.template')}</option>
          <option value="regex">{t('transform.regexExtract')}</option>
          <option value="jsonPath">{t('transform.jsonPath')}</option>
          <option value="trim">{t('transform.trim')}</option>
          <option value="split">{t('transform.splitTake')}</option>
          <option value="upper">{t('transform.upper')}</option>
          <option value="lower">{t('transform.lower')}</option>
          <option value="truncate">Truncate</option>
          <option value="script">{t('transform.script')}</option>
        </select>
      </Field>

      {data.kind === 'template' && (
        <Field label={t('form.template')}>
          <ExpandableTextarea
            className="font-mono text-[10px]"
            value={data.expression}
            onChange={(v) => onUpdate({ expression: v })}
            rows={4}
            placeholder="{{input}}"
            label="Code — Handlebars Template"
            language="handlebars"
          />
          <p className="text-[8px] text-muted-foreground mt-0.5">Use {'{{input}}'} for previous node output. Supports {'{{#each}}'} for arrays.</p>
        </Field>
      )}

      {data.kind === 'script' && (
        <>
          {/* Language Selector */}
          <Field label={t('script.languageLabel')}>
            <select className="field-input" value={scriptLang}
              onChange={(e) => {
                onUpdate({ language: e.target.value as ScriptLanguage, expression: '' })
              }}>
              {SCRIPT_LANGUAGES.map((lang) => (
                <option key={lang.value} value={lang.value}>{lang.label}</option>
              ))}
            </select>
          </Field>

          {/* Monaco Preview + Script Studio */}
          <Field label={isCSharp ? t('script.csharpCodeLabel') : t('script.codeLabel')}>
            <MonacoCodeEditor
              value={data.expression}
              onChange={(v) => onUpdate({ expression: v })}
              language={isCSharp ? 'csharp' : 'javascript'}
              label={`Code — ${isCSharp ? 'C#' : 'JavaScript'}`}
            />
          </Field>
        </>
      )}

      {data.kind === 'regex' && (
        <>
          <Field label={t('form.pattern')}>
            <input className="field-input font-mono text-[10px]" value={data.expression}
              onChange={(e) => onUpdate({ expression: e.target.value })} placeholder="(\d+)" />
          </Field>
          <Field label={t('form.replacement')}>
            <input className="field-input font-mono text-[10px]" value={data.replacement ?? ''}
              onChange={(e) => onUpdate({ replacement: e.target.value })} placeholder="$1 (leave empty for extract mode)" />
          </Field>
        </>
      )}

      {data.kind === 'jsonPath' && (
        <Field label={t('form.pattern')}>
          <input className="field-input font-mono text-[10px]" value={data.expression}
            onChange={(e) => onUpdate({ expression: e.target.value })} placeholder="$.data.items[0].name" />
        </Field>
      )}

      {data.kind === 'trim' && (
        <Field label={t('form.maxLength')}>
          <input type="number" className="field-input" value={data.maxLength ?? 0} min={0}
            onChange={(e) => onUpdate({ maxLength: Number(e.target.value) })} />
          <p className="text-[8px] text-muted-foreground mt-0.5">0 = trim whitespace only</p>
        </Field>
      )}

      {data.kind === 'truncate' && (
        <Field label={t('form.maxLength')}>
          <input type="number" className="field-input" value={data.maxLength ?? 0} min={0}
            onChange={(e) => onUpdate({ maxLength: Number(e.target.value) })} />
          <p className="text-[8px] text-muted-foreground mt-0.5">Maximum characters (0 = no limit)</p>
        </Field>
      )}

      {data.kind === 'split' && (
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
