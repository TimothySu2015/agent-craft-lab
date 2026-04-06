import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Sparkles, Loader2, Play } from 'lucide-react'
import { Field } from '../PropertiesPanel'
import { ExpandableTextarea } from '@/components/shared/ExpandableTextarea'
import { useDefaultCredential } from '@/hooks/useDefaultCredential'
import type { CodeNodeData, NodeData } from '@/types/workflow'

interface Props {
  data: CodeNodeData
  onUpdate: (partial: Partial<NodeData>) => void
}

export function CodeForm({ data, onUpdate }: Props) {
  const { t } = useTranslation('studio')
  const [prompt, setPrompt] = useState('')
  const [generating, setGenerating] = useState(false)
  const [genError, setGenError] = useState('')
  const [testInput, setTestInput] = useState('')
  const [testResult, setTestResult] = useState<{ success: boolean; output: string; error?: string; elapsedMs?: number } | null>(null)
  const [testing, setTesting] = useState(false)
  const getCredential = useDefaultCredential()

  const handleTestRun = async () => {
    if (!data.template?.trim()) return
    setTesting(true)
    setTestResult(null)
    try {
      const res = await fetch('/api/script-test', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ code: data.template, input: testInput }),
      })
      const result = await res.json()
      setTestResult(result)
    } catch (err) {
      setTestResult({ success: false, output: '', error: (err as Error).message })
    } finally {
      setTesting(false)
    }
  }

  const handleGenerate = async () => {
    if (!prompt.trim()) return
    const cred = getCredential()
    if (!cred) { setGenError(t('script.noKey')); return }

    setGenerating(true)
    setGenError('')
    try {
      const res = await fetch('/api/script-generator', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          prompt: prompt.trim(),
          provider: cred.provider,
          model: cred.model || 'gpt-4o',
          apiKey: cred.apiKey,
          endpoint: cred.endpoint || '',
        }),
      })
      if (!res.ok) {
        const err = await res.json().catch(() => ({ message: res.statusText }))
        throw new Error(err.message || res.statusText)
      }
      const result = await res.json()
      if (result.code) {
        onUpdate({ template: result.code })
      }
    } catch (err) {
      setGenError((err as Error).message)
    } finally {
      setGenerating(false)
    }
  }

  return (
    <>
      <Field label={t('form.transformType')}>
        <select className="field-input" value={data.transformType} onChange={(e) => onUpdate({ transformType: e.target.value })}>
          <option value="template">Template</option>
          <option value="regex-extract">Regex Extract</option>
          <option value="regex-replace">Regex Replace</option>
          <option value="json-path">JSON Path</option>
          <option value="trim">Trim</option>
          <option value="split-take">Split & Take</option>
          <option value="upper">Upper</option>
          <option value="lower">Lower</option>
          <option value="script">Script (JavaScript)</option>
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
          {/* AI Generate */}
          <Field label={t('script.generateLabel')}>
            <div className="flex gap-1.5">
              <input className="field-input text-[10px] flex-1" value={prompt}
                onChange={(e) => setPrompt(e.target.value)}
                placeholder={t('script.promptPlaceholder')}
                onKeyDown={(e) => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); handleGenerate() } }} />
              <button onClick={handleGenerate} disabled={generating || !prompt.trim()}
                className="flex items-center gap-1 rounded-md bg-violet-600 px-2.5 py-1 text-[10px] font-medium text-white hover:bg-violet-500 disabled:opacity-50 cursor-pointer shrink-0">
                {generating ? <Loader2 size={11} className="animate-spin" /> : <Sparkles size={11} />}
                {t('script.generate')}
              </button>
            </div>
            {genError && <p className="text-[8px] text-red-400 mt-0.5">{genError}</p>}
          </Field>

          {/* Code Editor */}
          <Field label={t('script.codeLabel')}>
            <ExpandableTextarea
              className="font-mono text-[10px]"
              value={data.template}
              onChange={(v) => onUpdate({ template: v })}
              rows={8}
              placeholder={'const data = JSON.parse(input);\nresult = data.map(d => d.name).join(\', \');'}
              label="Code — JavaScript"
              language="javascript"
            />
            <p className="text-[8px] text-muted-foreground mt-0.5">{t('script.hint')}</p>
          </Field>

          {/* Test Run */}
          <Field label={t('script.testLabel')}>
            <textarea className="field-textarea font-mono text-[10px]" value={testInput} rows={2}
              placeholder={t('script.testInputPlaceholder')}
              onChange={(e) => setTestInput(e.target.value)} />
            <button onClick={handleTestRun} disabled={testing || !data.template?.trim()}
              className="mt-1.5 flex items-center gap-1 rounded-md bg-green-600 px-2.5 py-1 text-[10px] font-medium text-white hover:bg-green-500 disabled:opacity-50 cursor-pointer">
              {testing ? <Loader2 size={11} className="animate-spin" /> : <Play size={11} />}
              {t('script.testRun')}
            </button>
            {testResult && (
              <div className={`mt-1.5 rounded-md border px-2.5 py-2 text-[10px] font-mono whitespace-pre-wrap ${
                testResult.success
                  ? 'border-green-500/30 bg-green-500/5 text-green-300'
                  : 'border-red-500/30 bg-red-500/5 text-red-300'
              }`}>
                {testResult.success ? testResult.output || '(empty output)' : `Error: ${testResult.error}`}
                {testResult.elapsedMs != null && (
                  <span className="block text-[8px] text-muted-foreground mt-1">{testResult.elapsedMs.toFixed(1)}ms</span>
                )}
              </div>
            )}
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
