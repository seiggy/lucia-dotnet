import { X } from 'lucide-react'
import type { Toast } from '../hooks/useToast'

interface ToastContainerProps {
  toasts: Toast[]
  onDismiss: (id: number) => void
}

/**
 * Fixed-position toast notification container.
 *
 * Renders a stack of success/error toasts in the top-right corner.
 * Pair with the `useToast` hook for state management.
 */
export default function ToastContainer({ toasts, onDismiss }: ToastContainerProps) {
  if (toasts.length === 0) return null
  return (
    <div className="fixed top-4 right-4 z-50 flex flex-col gap-2" role="status" aria-live="polite">
      {toasts.map((t) => (
        <div
          key={t.id}
          className={`flex items-center gap-3 rounded-xl px-4 py-3 shadow-lg text-sm font-medium transition-all duration-300 ${
            t.type === 'success'
              ? 'bg-sage/20 text-light border border-sage/30'
              : 'bg-ember/20 text-light border border-ember/30'
          }`}
        >
          <span aria-hidden="true">{t.type === 'success' ? '✓' : '✕'}</span>
          <span className="flex-1">{t.message}</span>
          <button
            onClick={() => onDismiss(t.id)}
            className="ml-2 opacity-70 hover:opacity-100"
            aria-label="Dismiss notification"
          >
            <X className="h-3.5 w-3.5" />
          </button>
        </div>
      ))}
    </div>
  )
}
