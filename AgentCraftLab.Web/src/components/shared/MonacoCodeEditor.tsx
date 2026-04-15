/**
 * MonacoCodeEditor — Monaco Editor 包裝元件。
 * 用於 Code 節點 script 模式，提供語法高亮、括號配對、自動縮排。
 * Inline 顯示程式碼預覽，點擊打開全螢幕 Script Studio（AI 生成 + 編輯 + 測試）。
 */
import { useState, useCallback, useRef } from 'react'
import Editor, { type OnMount } from '@monaco-editor/react'
import type { editor } from 'monaco-editor'
import { Maximize2, X, Sparkles, Loader2, Play, AlignLeft } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useDefaultCredential } from '@/hooks/useDefaultCredential'

interface Props {
  value: string
  onChange: (value: string) => void
  language: string
  label?: string
  /** 內嵌預覽高度（px），預設 120 */
  inlineHeight?: number
}

/** Monaco 語言對應表 */
function toMonacoLanguage(lang: string): string {
  switch (lang) {
    case 'csharp': return 'csharp'
    case 'javascript': case 'js': return 'javascript'
    case 'json': return 'json'
    default: return lang
  }
}

const EDITOR_OPTIONS: editor.IStandaloneEditorConstructionOptions = {
  minimap: { enabled: false },
  fontSize: 12,
  lineNumbersMinChars: 3,
  scrollBeyondLastLine: false,
  automaticLayout: true,
  tabSize: 4,
  wordWrap: 'on',
  renderLineHighlight: 'line',
  bracketPairColorization: { enabled: true },
  padding: { top: 8, bottom: 8 },
  scrollbar: { verticalScrollbarSize: 8, horizontalScrollbarSize: 8 },
}

export function MonacoCodeEditor({ value, onChange, language, label, inlineHeight = 120 }: Props) {
  const { t } = useTranslation('studio')
  const [expanded, setExpanded] = useState(false)
  const monacoLang = toMonacoLanguage(language)

  return (
    <>
      {/* Inline Preview — 唯讀預覽 + 點擊打開編輯器 */}
      <div className="relative group rounded-md border border-border overflow-hidden cursor-pointer"
        onClick={() => setExpanded(true)}
        onKeyDown={(e) => e.stopPropagation()}>
        <Editor
          height={inlineHeight}
          language={monacoLang}
          theme="vs-dark"
          value={value}
          options={{ ...EDITOR_OPTIONS, readOnly: true, lineNumbers: 'on', renderLineHighlight: 'none', domReadOnly: true }}
          loading={<div className="flex items-center justify-center h-full text-xs text-muted-foreground">{t('editor.loading')}</div>}
        />
        <div className="absolute inset-0 flex items-center justify-center bg-black/0 hover:bg-black/30 transition-colors">
          <span className="opacity-0 group-hover:opacity-100 transition-opacity text-xs text-white bg-black/60 rounded-md px-3 py-1.5 flex items-center gap-1.5">
            <Maximize2 size={12} /> {t('editor.openScriptStudio')}
          </span>
        </div>
      </div>

      {/* Full-screen Script Studio */}
      {expanded && (
        <ScriptStudio
          value={value}
          onChange={onChange}
          language={language}
          monacoLang={monacoLang}
          label={label}
          onClose={() => setExpanded(false)}
        />
      )}
    </>
  )
}

function ScriptStudio({ value, onChange, language, monacoLang, label, onClose }: {
  value: string
  onChange: (value: string) => void
  language: string
  monacoLang: string
  label?: string
  onClose: () => void
}) {
  const { t } = useTranslation(['studio', 'common'])
  const getCredential = useDefaultCredential()
  const editorRef = useRef<editor.IStandaloneCodeEditor | null>(null)

  const [draft, setDraft] = useState(value ?? '')
  const [prompt, setPrompt] = useState('')
  const [generating, setGenerating] = useState(false)
  const [genError, setGenError] = useState('')
  const [testInput, setTestInput] = useState('')
  const [testResult, setTestResult] = useState<{ success: boolean; output: string; error?: string; elapsedMs?: number } | null>(null)
  const [testing, setTesting] = useState(false)

  const isCSharp = language === 'csharp'
  const charCount = draft.length
  const lineCount = draft.split('\n').length

  const handleSave = () => {
    onChange(draft)
    onClose()
  }

  const handleFormat = () => {
    editorRef.current?.getAction('editor.action.formatDocument')?.run()
  }

  const handleEditorMount: OnMount = useCallback((editor) => {
    editorRef.current = editor
    editor.focus()
  }, [])

  const handleGenerate = async () => {
    if (!prompt.trim()) return
    const cred = getCredential()
    if (!cred) { setGenError(t('studio:script.noKey')); return }

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
          language,
        }),
      })
      if (!res.ok) {
        const err = await res.json().catch(() => ({ message: res.statusText }))
        throw new Error(err.message || res.statusText)
      }
      const result = await res.json()
      if (result.code) {
        setDraft(result.code)
        if (result.testInput) {
          setTestInput(result.testInput)
        }
      }
    } catch (err) {
      setGenError((err as Error).message)
    } finally {
      setGenerating(false)
    }
  }

  const handleTestRun = async () => {
    if (!draft.trim()) return
    setTesting(true)
    setTestResult(null)
    try {
      const res = await fetch('/api/script-test', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ code: draft, input: testInput, language }),
      })
      const result = await res.json()
      setTestResult(result)
    } catch (err) {
      setTestResult({ success: false, output: '', error: (err as Error).message })
    } finally {
      setTesting(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60" onClick={onClose}>
      <div
        className="w-[92vw] max-w-[1000px] h-[88vh] rounded-lg border border-border bg-card shadow-2xl flex flex-col"
        onClick={(e) => e.stopPropagation()}
        onKeyDown={(e) => {
          e.stopPropagation()
          if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) { e.preventDefault(); handleSave() }
          if (e.key === 'Escape') onClose()
        }}
      >
        {/* Header */}
        <div className="flex items-center justify-between border-b border-border px-4 py-2.5 shrink-0">
          <div className="flex items-center gap-3">
            <h2 className="text-sm font-semibold text-foreground">{label || t('studio:editor.scriptStudio')}</h2>
            <span className="rounded bg-blue-500/15 px-1.5 py-0.5 text-[9px] font-mono text-blue-400">{language}</span>
          </div>
          <div className="flex items-center gap-3">
            <span className="text-[9px] text-muted-foreground">{lineCount} lines, {charCount} chars</span>
            <button onClick={onClose} className="text-muted-foreground hover:text-foreground cursor-pointer">
              <X size={16} />
            </button>
          </div>
        </div>

        {/* AI Generate Bar */}
        <div className="flex items-center gap-2 border-b border-border/50 px-4 py-2 shrink-0 bg-card/50">
          <Sparkles size={13} className="text-violet-400 shrink-0" />
          <input className="flex-1 bg-transparent border border-border rounded-md px-2.5 py-1.5 text-xs text-foreground placeholder:text-muted-foreground/50 focus:outline-none focus:border-violet-500/50"
            value={prompt}
            onChange={(e) => setPrompt(e.target.value)}
            placeholder={t('studio:script.promptPlaceholder')}
            onKeyDown={(e) => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); handleGenerate() } }} />
          <button onClick={handleGenerate} disabled={generating || !prompt.trim()}
            className="flex items-center gap-1 rounded-md bg-violet-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-violet-500 disabled:opacity-50 cursor-pointer shrink-0">
            {generating ? <Loader2 size={12} className="animate-spin" /> : <Sparkles size={12} />}
            {t('studio:script.generate')}
          </button>
          {genError && <span className="text-[10px] text-red-400 truncate max-w-[200px]">{genError}</span>}
        </div>

        {/* Editor Body */}
        <div className="flex-1 overflow-hidden">
          <Editor
            height="100%"
            language={monacoLang}
            theme="vs-dark"
            value={draft}
            onChange={(v) => setDraft(v ?? '')}
            onMount={handleEditorMount}
            options={{
              ...EDITOR_OPTIONS,
              fontSize: 14,
              minimap: { enabled: true },
            }}
          />
        </div>

        {/* Test Run Panel */}
        <div className="border-t border-border shrink-0">
          <div className="flex items-start gap-3 px-4 py-2.5">
            {/* Test Input */}
            <div className="flex-1 min-w-0">
              <label className="text-[9px] text-muted-foreground mb-1 block">{t('studio:script.testLabel')}</label>
              <textarea className="w-full rounded-md border border-border bg-background text-xs font-mono text-foreground px-2.5 py-1.5 resize-none focus:outline-none focus:border-blue-500/50"
                value={testInput} rows={2}
                placeholder={t('studio:script.testInputPlaceholder')}
                onChange={(e) => setTestInput(e.target.value)} />
            </div>
            {/* Test Result */}
            <div className="flex-1 min-w-0">
              <label className="text-[9px] text-muted-foreground mb-1 block">{t('studio:script.resultLabel')}</label>
              {testResult ? (
                <div className={`rounded-md border px-2.5 py-1.5 text-xs font-mono whitespace-pre-wrap max-h-[72px] overflow-y-auto ${
                  testResult.success
                    ? 'border-green-500/30 bg-green-500/5 text-green-300'
                    : 'border-red-500/30 bg-red-500/5 text-red-300'
                }`}>
                  {testResult.success ? testResult.output || t('studio:editor.empty') : `Error: ${testResult.error}`}
                  {testResult.elapsedMs != null && (
                    <span className="block text-[9px] text-muted-foreground mt-0.5">{testResult.elapsedMs.toFixed(1)}ms</span>
                  )}
                </div>
              ) : (
                <div className="rounded-md border border-border/50 px-2.5 py-1.5 text-xs text-muted-foreground/50 h-[52px] flex items-center">
                  {t('studio:script.noResult')}
                </div>
              )}
            </div>
            {/* Run Button */}
            <div className="shrink-0 pt-4">
              <button onClick={handleTestRun} disabled={testing || !draft.trim()}
                className="flex items-center gap-1.5 rounded-md bg-green-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-green-500 disabled:opacity-50 cursor-pointer">
                {testing ? <Loader2 size={12} className="animate-spin" /> : <Play size={12} />}
                {t('studio:script.testRun')}
              </button>
            </div>
          </div>
        </div>

        {/* Footer */}
        <div className="flex items-center justify-between border-t border-border px-4 py-2 shrink-0">
          <div className="flex items-center gap-2">
            <span className="text-[9px] text-muted-foreground">
              {isCSharp ? t('studio:script.csharpHint') : t('studio:script.hint')}
            </span>
          </div>
          <div className="flex gap-2">
            <button onClick={handleFormat} title={t('studio:editor.formatTitle')}
              className="flex items-center gap-1 rounded-md border border-border px-2.5 py-1.5 text-xs text-muted-foreground hover:text-foreground transition-colors cursor-pointer">
              <AlignLeft size={12} /> {t('studio:editor.format')}
            </button>
            <button onClick={onClose}
              className="rounded-md border border-border px-3 py-1.5 text-xs text-muted-foreground hover:text-foreground transition-colors cursor-pointer">
              {t('common:cancel')}
            </button>
            <button onClick={handleSave}
              className="rounded-md bg-blue-600 px-4 py-1.5 text-xs font-semibold text-white hover:bg-blue-500 transition-colors cursor-pointer">
              {t('common:apply')}
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}
