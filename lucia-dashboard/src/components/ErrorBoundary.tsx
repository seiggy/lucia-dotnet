import { Component, type ErrorInfo, type ReactNode } from 'react'
import { AlertTriangle, RefreshCw } from 'lucide-react'

interface Props {
  children: ReactNode
}

interface State {
  hasError: boolean
  error: Error | null
}

class ErrorBoundary extends Component<Props, State> {
  constructor(props: Props) {
    super(props)
    this.state = { hasError: false, error: null }
  }

  static getDerivedStateFromError(error: Error): State {
    return { hasError: true, error }
  }

  componentDidCatch(error: Error, info: ErrorInfo): void {
    console.error('[ErrorBoundary] Uncaught render error:', error, info.componentStack)
  }

  private handleReload = (): void => {
    window.location.reload()
  }

  private handleRetry = (): void => {
    this.setState({ hasError: false, error: null })
  }

  render(): ReactNode {
    if (this.state.hasError) {
      return (
        <div className="min-h-screen bg-void flex items-center justify-center p-6">
          <div className="bg-basalt border border-stone rounded-xl p-8 max-w-md w-full text-center space-y-5 shadow-lg">
            <div className="flex justify-center">
              <AlertTriangle className="text-amber w-12 h-12" />
            </div>
            <div className="space-y-2">
              <h1 className="text-light text-xl font-semibold">Something went wrong</h1>
              <p className="text-dust text-sm">
                The dashboard encountered an unexpected error. You can try again or reload the page.
              </p>
            </div>
            {this.state.error && (
              <pre className="bg-charcoal border border-stone rounded-lg text-dust text-xs p-3 text-left overflow-x-auto whitespace-pre-wrap break-words">
                {this.state.error.message}
              </pre>
            )}
            <div className="flex gap-3 justify-center pt-1">
              <button
                onClick={this.handleRetry}
                className="flex items-center gap-2 px-4 py-2 bg-charcoal border border-stone text-light text-sm rounded-lg hover:border-amber transition-colors"
              >
                <RefreshCw className="w-4 h-4" />
                Try again
              </button>
              <button
                onClick={this.handleReload}
                className="flex items-center gap-2 px-4 py-2 bg-amber-glow text-light text-sm rounded-lg hover:opacity-90 transition-opacity"
              >
                Reload page
              </button>
            </div>
          </div>
        </div>
      )
    }

    return this.props.children
  }
}

export default ErrorBoundary
