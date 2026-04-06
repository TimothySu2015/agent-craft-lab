/**
 * ErrorBoundary — 全域錯誤邊界，捕獲 React 元件崩潰並顯示友善錯誤畫面。
 * 防止白屏，提供重試按鈕。
 */
import { Component, type ReactNode } from 'react'
import { AlertTriangle, RotateCcw } from 'lucide-react'

interface Props {
  children: ReactNode
}

interface State {
  hasError: boolean
  error: Error | null
}

export class ErrorBoundary extends Component<Props, State> {
  state: State = { hasError: false, error: null }

  static getDerivedStateFromError(error: Error): State {
    return { hasError: true, error }
  }

  componentDidCatch(error: Error, info: React.ErrorInfo) {
    console.error('[ErrorBoundary]', error, info.componentStack)
  }

  handleReset = () => {
    this.setState({ hasError: false, error: null })
  }

  render() {
    if (!this.state.hasError) return this.props.children

    return (
      <div className="flex h-screen items-center justify-center bg-background">
        <div className="max-w-md text-center px-6">
          <AlertTriangle size={40} className="text-red-400 mx-auto mb-4" />
          <h1 className="text-lg font-semibold text-foreground mb-2">Something went wrong</h1>
          <p className="text-xs text-muted-foreground mb-4">
            An unexpected error occurred. You can try reloading the page or going back to the Studio.
          </p>
          {this.state.error && (
            <pre className="rounded-md border border-border bg-card p-3 text-[10px] font-mono text-red-400 text-left mb-4 max-h-[120px] overflow-y-auto">
              {this.state.error.message}
            </pre>
          )}
          <div className="flex gap-2 justify-center">
            <button
              onClick={this.handleReset}
              className="flex items-center gap-1.5 rounded-md border border-border px-4 py-2 text-xs text-muted-foreground hover:text-foreground transition-colors cursor-pointer"
            >
              <RotateCcw size={13} /> Try Again
            </button>
            <button
              onClick={() => window.location.href = '/'}
              className="flex items-center gap-1.5 rounded-md bg-blue-600 px-4 py-2 text-xs font-semibold text-white hover:bg-blue-500 transition-colors cursor-pointer"
            >
              Back to Studio
            </button>
          </div>
        </div>
      </div>
    )
  }
}
