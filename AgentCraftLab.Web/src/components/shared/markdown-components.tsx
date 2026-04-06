/**
 * Markdown Preview 共用元件樣式 — 供所有需要渲染 Markdown 的地方使用。
 */
import type { Components } from 'react-markdown'

export const markdownComponents: Partial<Components> = {
  h1: ({ children }) => <h1 className="text-xl font-bold text-foreground mt-6 mb-3 pb-2 border-b border-border">{children}</h1>,
  h2: ({ children }) => <h2 className="text-lg font-semibold text-foreground mt-5 mb-2">{children}</h2>,
  h3: ({ children }) => <h3 className="text-base font-semibold text-foreground mt-4 mb-2">{children}</h3>,
  h4: ({ children }) => <h4 className="text-sm font-semibold text-foreground mt-3 mb-1">{children}</h4>,
  h5: ({ children }) => <h5 className="text-xs font-semibold text-foreground mt-2 mb-1">{children}</h5>,
  p: ({ children }) => <p className="mb-2">{children}</p>,
  ul: ({ children }) => <ul className="list-disc pl-5 mb-2 space-y-0.5">{children}</ul>,
  ol: ({ children }) => <ol className="list-decimal pl-5 mb-2 space-y-0.5">{children}</ol>,
  li: ({ children }) => <li className="text-sm">{children}</li>,
  strong: ({ children }) => <strong className="text-foreground font-semibold">{children}</strong>,
  code: ({ children, className }) => className
    ? <code className="block bg-secondary/50 rounded-md p-3 text-xs font-mono overflow-x-auto mb-2">{children}</code>
    : <code className="bg-secondary/50 rounded px-1 py-0.5 text-xs font-mono text-blue-400">{children}</code>,
  hr: () => <hr className="border-border my-4" />,
  table: ({ children }) => (
    <div className="overflow-x-auto mb-3">
      <table className="w-full border-collapse border border-border text-xs">{children}</table>
    </div>
  ),
  thead: ({ children }) => <thead className="bg-secondary/50">{children}</thead>,
  th: ({ children }) => <th className="border border-border px-3 py-1.5 text-left font-semibold text-foreground">{children}</th>,
  td: ({ children }) => <td className="border border-border px-3 py-1.5">{children}</td>,
  blockquote: ({ children }) => <blockquote className="border-l-2 border-blue-500 pl-3 italic text-muted-foreground mb-2">{children}</blockquote>,
}
