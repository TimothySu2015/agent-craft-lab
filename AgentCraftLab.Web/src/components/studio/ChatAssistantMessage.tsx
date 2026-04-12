/**
 * ChatAssistantMessage — CopilotKit Execute Chat 自訂 AssistantMessage。
 * 偵測回應內容中的 JSON 區塊，以 JSON View 渲染。
 */
import { useState, useMemo } from 'react'
import { ChevronRight, Code2, Copy } from 'lucide-react'
import JsonView from 'react18-json-view'
import 'react18-json-view/src/style.css'
import Markdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { markdownComponents } from '@/components/shared/markdown-components'

interface AssistantMessageProps {
  message?: { content?: string; generativeUI?: () => React.JSX.Element | null; generativeUIPosition?: string }
  isLoading: boolean
  isCurrentMessage?: boolean
  isGenerating: boolean
  subComponent?: React.JSX.Element
  [key: string]: unknown
}

type Segment = { type: 'text'; content: string } | { type: 'json'; parsed: unknown; raw: string }

/**
 * 將內容拆分成 text / json 交替的 segments。
 * 偵測策略：
 *  1. ```json code blocks
 *  2. 行首起始的完整 JSON object/array（用大括號/中括號配對）
 */
function parseSegments(text: string): Segment[] {
  const segments: Segment[] = []
  let remaining = text

  while (remaining.length > 0) {
    // 1. 找 ```json code block
    const codeBlockMatch = remaining.match(/```(?:json)?\s*\n([\s\S]*?)\n\s*```/)
    // 2. 找行首的 JSON（{ 或 [）
    const jsonStartMatch = remaining.match(/(?:^|\n)([\t ]*[{[])/)

    // 決定哪個先出現
    const codeBlockIdx = codeBlockMatch?.index ?? Infinity
    const jsonStartIdx = jsonStartMatch?.index ?? Infinity

    if (codeBlockIdx === Infinity && jsonStartIdx === Infinity) {
      // 沒有更多 JSON，剩餘全是文字
      if (remaining.trim()) segments.push({ type: 'text', content: remaining })
      break
    }

    if (codeBlockIdx <= jsonStartIdx && codeBlockMatch) {
      // code block 先出現
      const before = remaining.slice(0, codeBlockIdx)
      if (before.trim()) segments.push({ type: 'text', content: before })

      const jsonStr = codeBlockMatch[1].trim()
      try {
        const parsed = JSON.parse(jsonStr)
        segments.push({ type: 'json', parsed, raw: jsonStr })
      } catch {
        // 不是合法 JSON，當作文字
        segments.push({ type: 'text', content: codeBlockMatch[0] })
      }
      remaining = remaining.slice(codeBlockIdx + codeBlockMatch[0].length)
    } else if (jsonStartMatch) {
      // 行首 JSON 先出現
      const actualStart = jsonStartIdx + jsonStartMatch[0].length - jsonStartMatch[1].length
      const before = remaining.slice(0, actualStart)
      if (before.trim()) segments.push({ type: 'text', content: before })

      const extracted = extractJsonBlock(remaining.slice(actualStart))
      if (extracted) {
        segments.push({ type: 'json', parsed: extracted.parsed, raw: extracted.raw })
        remaining = remaining.slice(actualStart + extracted.raw.length)
      } else {
        // 配對失敗，跳過這個字元當文字
        const skipTo = actualStart + 1
        if (before.trim()) {
          // 已經 push 過 before
          const lastSeg = segments[segments.length - 1]
          if (lastSeg.type === 'text') lastSeg.content += remaining[actualStart]
        } else {
          segments.push({ type: 'text', content: remaining.slice(0, skipTo) })
        }
        remaining = remaining.slice(skipTo)
      }
    }
  }

  return segments
}

/** 從字串開頭提取一個完整的 JSON object 或 array（大括號/中括號配對） */
function extractJsonBlock(text: string): { parsed: unknown; raw: string } | null {
  const open = text[0]
  if (open !== '{' && open !== '[') return null
  const close = open === '{' ? '}' : ']'

  let depth = 0
  let inString = false
  let escape = false

  for (let i = 0; i < text.length; i++) {
    const ch = text[i]

    if (escape) { escape = false; continue }
    if (ch === '\\' && inString) { escape = true; continue }
    if (ch === '"') { inString = !inString; continue }
    if (inString) continue

    if (ch === open) depth++
    else if (ch === close) {
      depth--
      if (depth === 0) {
        const raw = text.slice(0, i + 1)
        try {
          const parsed = JSON.parse(raw)
          return { parsed, raw }
        } catch {
          return null
        }
      }
    }
  }

  return null
}

export function ChatAssistantMessage(props: AssistantMessageProps) {
  const { message, isLoading, subComponent } = props
  const content = message?.content || ''
  const generativeUI = message?.generativeUI?.() ?? subComponent
  const generativeUIPosition = message?.generativeUIPosition ?? 'after'

  const segments = useMemo(() => content ? parseSegments(content) : [], [content])
  const hasJson = segments.some((s) => s.type === 'json')

  if (isLoading && !content) {
    return <LoadingDots />
  }

  return (
    <>
      {generativeUI && generativeUIPosition === 'before' && (
        <div style={{ marginBottom: '0.5rem' }}>{generativeUI}</div>
      )}
      <div className="copilotKitMessage copilotKitAssistantMessage">
        {hasJson ? (
          // 有 JSON → 分段渲染
          segments.map((seg, i) =>
            seg.type === 'text' ? (
              <Markdown key={i} remarkPlugins={[remarkGfm]} components={markdownComponents}>
                {seg.content}
              </Markdown>
            ) : (
              <JsonBlock key={i} parsed={seg.parsed} raw={seg.raw} />
            )
          )
        ) : (
          // 純文字 → Markdown 渲染
          <Markdown remarkPlugins={[remarkGfm]} components={markdownComponents}>
            {content}
          </Markdown>
        )}
      </div>
      {generativeUI && generativeUIPosition !== 'before' && (
        <div style={{ marginBottom: '0.5rem' }}>{generativeUI}</div>
      )}
      {isLoading && <LoadingDots />}
    </>
  )
}

function JsonBlock({ parsed, raw }: { parsed: unknown; raw: string }) {
  const [expanded, setExpanded] = useState(true)
  const [copied, setCopied] = useState(false)

  const handleCopy = () => {
    navigator.clipboard.writeText(raw)
    setCopied(true)
    setTimeout(() => setCopied(false), 2000)
  }

  return (
    <div className="rounded-md border border-border bg-secondary/30 overflow-hidden mt-1 mb-2">
      <div className="flex items-center justify-between px-2.5 py-1.5 border-b border-border/50">
        <button
          onClick={() => setExpanded(!expanded)}
          className="flex items-center gap-1.5 text-[10px] font-medium text-muted-foreground hover:text-foreground cursor-pointer"
        >
          <ChevronRight size={12} className={`transition-transform ${expanded ? 'rotate-90' : ''}`} />
          <Code2 size={11} />
          JSON
        </button>
        <button
          onClick={handleCopy}
          className="flex items-center gap-1 text-[9px] text-muted-foreground hover:text-foreground cursor-pointer"
        >
          <Copy size={10} />
          {copied ? 'Copied!' : 'Copy'}
        </button>
      </div>
      {expanded && (
        <div className="p-2.5 overflow-x-auto max-h-[400px] overflow-y-auto">
          <JsonView
            src={parsed}
            theme="a11y"
            dark
            style={{ fontSize: '11px', background: 'transparent' }}
          />
        </div>
      )}
    </div>
  )
}

function LoadingDots() {
  return (
    <div className="flex gap-1 px-1 py-2">
      <span className="h-1.5 w-1.5 rounded-full bg-muted-foreground/50 animate-bounce" style={{ animationDelay: '0ms' }} />
      <span className="h-1.5 w-1.5 rounded-full bg-muted-foreground/50 animate-bounce" style={{ animationDelay: '150ms' }} />
      <span className="h-1.5 w-1.5 rounded-full bg-muted-foreground/50 animate-bounce" style={{ animationDelay: '300ms' }} />
    </div>
  )
}
