/**
 * CodeDialog — 顯示生成的 C# 程式碼（Prism syntax highlighting + 複製按鈕）。
 */
import { useState, useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { X, Copy, Check } from 'lucide-react'
import { Highlight, themes, Prism } from 'prism-react-renderer'
import Prismjs from 'prismjs'
import 'prismjs/components/prism-csharp'
import 'prismjs/components/prism-json'

// 將 prismjs 註冊的語言同步到 prism-react-renderer
if (Prismjs.languages.csharp && !Prism.languages.csharp) {
  Prism.languages.csharp = Prismjs.languages.csharp
}
if (Prismjs.languages.json && !Prism.languages.json) {
  Prism.languages.json = Prismjs.languages.json
}
import { useWorkflowStore } from '@/stores/workflow-store'
import { generateCSharpCode } from '@/lib/codegen'

interface Props {
  open: boolean
  onClose: () => void
}

export function CodeDialog({ open, onClose }: Props) {
  const { t } = useTranslation('studio')
  const nodes = useWorkflowStore((s) => s.nodes)
  const edges = useWorkflowStore((s) => s.edges)
  const [copied, setCopied] = useState(false)

  const code = useMemo(() => generateCSharpCode(nodes, edges), [nodes, edges])

  if (!open) return null

  const handleCopy = async () => {
    await navigator.clipboard.writeText(code)
    setCopied(true)
    setTimeout(() => setCopied(false), 2000)
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" onClick={onClose}>
      <div
        className="w-[750px] max-h-[80vh] rounded-lg border border-border bg-card shadow-xl flex flex-col"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between border-b border-border px-4 py-3 shrink-0">
          <h2 className="text-sm font-semibold text-foreground">{t('code.title')}</h2>
          <div className="flex items-center gap-2">
            <button
              onClick={handleCopy}
              className="flex items-center gap-1 rounded-md border border-border bg-secondary px-2.5 py-1 text-xs text-muted-foreground hover:text-foreground transition-colors cursor-pointer"
            >
              {copied ? <Check size={12} className="text-green-400" /> : <Copy size={12} />}
              {copied ? t('code.copied') : t('code.copy')}
            </button>
            <button onClick={onClose} className="text-muted-foreground hover:text-foreground cursor-pointer">
              <X size={16} />
            </button>
          </div>
        </div>
        <div className="flex-1 overflow-auto">
          <SyntaxBlock code={code} language="csharp" />
        </div>
      </div>
    </div>
  )
}

/** 通用 Syntax Highlight 元件 — 可在其他地方複用（JSON、YAML 等） */
export function SyntaxBlock({ code, language }: { code: string; language: string }) {
  return (
    <Highlight theme={themes.oneDark} code={code} language={language}>
      {({ style, tokens, getLineProps, getTokenProps }) => (
        <pre className="p-4 text-[11px] leading-relaxed overflow-auto" style={{ ...style, background: 'transparent' }}>
          {tokens.map((line, i) => (
            <div key={i} {...getLineProps({ line })} className="table-row">
              <span className="table-cell pr-4 text-right text-muted-foreground/40 select-none w-8">{i + 1}</span>
              <span className="table-cell">
                {line.map((token, j) => <span key={j} {...getTokenProps({ token })} />)}
              </span>
            </div>
          ))}
        </pre>
      )}
    </Highlight>
  )
}
