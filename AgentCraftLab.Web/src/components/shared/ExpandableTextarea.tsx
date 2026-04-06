/**
 * ExpandableTextarea — 可展開的文字編輯器。
 * 平時顯示為小 textarea，點擊展開按鈕後打開 Full-screen Modal editor。
 * 支援 Edit / Preview（Markdown）雙模式。
 */
import { useState, useRef, useEffect, forwardRef } from 'react'
import { Maximize2, X, Eye, Edit3, Sparkles, Loader2 } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useConfirmDialog } from '@/components/shared/ConfirmDialog'
import { PromptRefinerDialog, type PromptRefinerResult } from '@/components/shared/PromptRefinerDialog'
import Markdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { markdownComponents } from '@/components/shared/markdown-components'

interface Props {
  value: string
  onChange: (value: string) => void
  placeholder?: string
  rows?: number
  className?: string
  label?: string
  /** 語言標籤（如 'javascript', 'markdown', 'json', 'handlebars'），顯示在 Modal 標題，未來可接 syntax highlighting */
  language?: string
  /** 點擊 Optimize 按鈕時呼叫，回傳優化結果。傳入 undefined 表示不顯示按鈕。 */
  onOptimize?: (currentText: string) => Promise<PromptRefinerResult | null>
}

export function ExpandableTextarea({ value, onChange, placeholder, rows = 3, className = '', label, language, onOptimize }: Props) {
  const [expanded, setExpanded] = useState(false)

  return (
    <>
      {/* Inline textarea + expand button */}
      <div className="relative group">
        <textarea
          className={`field-textarea pr-7 ${className}`}
          value={value}
          onChange={(e) => onChange(e.target.value)}
          rows={rows}
          placeholder={placeholder}
        />
        <button
          onClick={() => setExpanded(true)}
          title="Expand editor"
          className="absolute top-1.5 right-1.5 rounded p-0.5 text-muted-foreground/50 hover:text-foreground hover:bg-secondary opacity-0 group-hover:opacity-100 transition-opacity cursor-pointer"
        >
          <Maximize2 size={12} />
        </button>
      </div>

      {/* Full-screen Modal */}
      {expanded && (
        <ExpandedEditor
          value={value}
          onChange={onChange}
          onClose={() => setExpanded(false)}
          label={label}
          language={language}
          placeholder={placeholder}
          onOptimize={onOptimize}
        />
      )}
    </>
  )
}

function ExpandedEditor({ value, onChange, onClose, label, language, placeholder, onOptimize }: {
  value: string
  onChange: (value: string) => void
  onClose: () => void
  label?: string
  language?: string
  placeholder?: string
  onOptimize?: (currentText: string) => Promise<PromptRefinerResult | null>
}) {
  const { t } = useTranslation('common')
  const { confirm, confirmDialog } = useConfirmDialog()
  const [mode, setMode] = useState<'edit' | 'preview'>('edit')
  const [draft, setDraft] = useState(value ?? '')
  const [optimizeLoading, setOptimizeLoading] = useState(false)
  const [optimizeResult, setOptimizeResult] = useState<PromptRefinerResult | null>(null)
  const textareaRef = useRef<HTMLTextAreaElement>(null)

  // 打開時 focus
  useEffect(() => {
    if (mode === 'edit') textareaRef.current?.focus()
  }, [mode])

  const handleSave = () => {
    onChange(draft)
    onClose()
  }

  const tryClose = () => {
    if (draft !== value) {
      confirm(t('expandable.discardChanges', 'Discard unsaved changes?'), { danger: false }).then((ok) => { if (ok) onClose() })
    } else {
      onClose()
    }
  }

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
      e.preventDefault()
      handleSave()
    }
    if (e.key === 'Escape') tryClose()
  }

  const charCount = draft.length
  const lineCount = draft.split('\n').length

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60" onClick={tryClose}>
      <div
        className="w-[90vw] max-w-[900px] h-[80vh] rounded-lg border border-border bg-card shadow-2xl flex flex-col"
        onClick={(e) => e.stopPropagation()}
        onKeyDown={handleKeyDown}
      >
        {/* Header */}
        <div className="flex items-center justify-between border-b border-border px-4 py-2.5 shrink-0">
          <div className="flex items-center gap-3">
            <h2 className="text-sm font-semibold text-foreground">{label || 'Instructions'}</h2>
            {language && (
              <span className="rounded bg-blue-500/15 px-1.5 py-0.5 text-[9px] font-mono text-blue-400">{language}</span>
            )}
            <div className="flex rounded-md border border-border overflow-hidden">
              <button
                onClick={() => setMode('edit')}
                className={`flex items-center gap-1 px-2.5 py-1 text-[10px] font-medium transition-colors cursor-pointer ${mode === 'edit' ? 'bg-secondary text-foreground' : 'text-muted-foreground hover:text-foreground'}`}
              >
                <Edit3 size={10} /> {t('edit')}
              </button>
              <button
                onClick={() => setMode('preview')}
                className={`flex items-center gap-1 px-2.5 py-1 text-[10px] font-medium transition-colors cursor-pointer ${mode === 'preview' ? 'bg-secondary text-foreground' : 'text-muted-foreground hover:text-foreground'}`}
              >
                <Eye size={10} /> {t('preview')}
              </button>
            </div>
            {onOptimize && (
              <button
                onClick={async () => {
                  setOptimizeLoading(true)
                  try {
                    const result = await onOptimize(draft)
                    if (result) setOptimizeResult(result)
                  } finally {
                    setOptimizeLoading(false)
                  }
                }}
                disabled={optimizeLoading || !draft.trim()}
                className="flex items-center gap-1 rounded-md border border-amber-500/30 bg-amber-500/10 px-2.5 py-1 text-[10px] font-medium text-amber-400 hover:bg-amber-500/20 disabled:opacity-40 disabled:cursor-not-allowed transition-colors cursor-pointer"
              >
                {optimizeLoading ? <Loader2 size={10} className="animate-spin" /> : <Sparkles size={10} />}
                {optimizeLoading ? t('promptRefiner.optimizing') : t('promptRefiner.optimize')}
              </button>
            )}
          </div>
          <div className="flex items-center gap-3">
            <span className="text-[9px] text-muted-foreground">{lineCount} lines, {charCount} chars</span>
            <button onClick={onClose} className="text-muted-foreground hover:text-foreground cursor-pointer">
              <X size={16} />
            </button>
          </div>
        </div>

        {/* Body */}
        <div className="flex-1 overflow-hidden">
          {mode === 'edit' ? (
            <LineNumberEditor
              ref={textareaRef}
              value={draft}
              onChange={setDraft}
              placeholder={placeholder}
            />
          ) : (
            <div className="w-full h-full overflow-y-auto p-4 text-sm leading-relaxed text-muted-foreground">
              {draft ? (
                <Markdown remarkPlugins={[remarkGfm]} components={markdownComponents}>
                  {draft}
                </Markdown>
              ) : (
                <p className="text-sm text-muted-foreground italic">{t('noContent')}</p>
              )}
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="flex items-center justify-between border-t border-border px-4 py-2 shrink-0">
          <span className="text-[9px] text-muted-foreground">{t('editorHint')}</span>
          <div className="flex gap-2">
            <button
              onClick={tryClose}
              className="rounded-md border border-border px-3 py-1.5 text-xs text-muted-foreground hover:text-foreground transition-colors cursor-pointer"
            >
              {t('cancel')}
            </button>
            <button
              onClick={handleSave}
              className="rounded-md bg-blue-600 px-4 py-1.5 text-xs font-semibold text-white hover:bg-blue-500 transition-colors cursor-pointer"
            >
              {t('apply')}
            </button>
          </div>
        </div>
      </div>
      {confirmDialog}
      {optimizeResult && (
        <PromptRefinerDialog
          result={optimizeResult}
          onApply={(refined) => {
            setDraft(refined)
            setOptimizeResult(null)
            setMode('edit')
          }}
          onClose={() => setOptimizeResult(null)}
        />
      )}
    </div>
  )
}

const EDITOR_LINE_HEIGHT = 'leading-[21px]'

/** 行號編輯器 — gutter + textarea 同步滾動。 */
const LineNumberEditor = forwardRef<HTMLTextAreaElement, {
  value: string; onChange: (v: string) => void; placeholder?: string
}>(({ value, onChange, placeholder }, ref) => {
  const gutterRef = useRef<HTMLDivElement>(null)
  const lineCount = value.split('\n').length

  const handleScroll = (e: React.UIEvent<HTMLTextAreaElement>) => {
    if (gutterRef.current) gutterRef.current.scrollTop = e.currentTarget.scrollTop
  }

  return (
    <div className="flex h-full overflow-hidden bg-background">
      {/* Gutter */}
      <div
        ref={gutterRef}
        className="shrink-0 overflow-hidden select-none border-r border-border/50 bg-card py-3 text-right"
        style={{ width: `${Math.max(3, String(lineCount).length + 1.5)}ch` }}
      >
        {Array.from({ length: lineCount }, (_, i) => (
          <div key={i} className={`px-2 text-[11px] font-mono ${EDITOR_LINE_HEIGHT} text-muted-foreground/40`}>
            {i + 1}
          </div>
        ))}
      </div>
      {/* Editor */}
      <textarea
        ref={ref}
        className={`flex-1 resize-none bg-transparent text-sm font-mono text-foreground py-3 px-3 focus:outline-none ${EDITOR_LINE_HEIGHT}`}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        onScroll={handleScroll}
        placeholder={placeholder}
        spellCheck={false}
      />
    </div>
  )
})
