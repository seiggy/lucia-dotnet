import { useState, useCallback, useRef } from 'react'

export interface Toast {
  id: number
  message: string
  type: 'success' | 'error'
}

/**
 * Shared toast notification hook.
 *
 * Provides `toasts` state, an `addToast` helper that auto-dismisses after
 * `duration` ms, and a manual `dismissToast` callback.
 */
export function useToast(duration = 3500) {
  const [toasts, setToasts] = useState<Toast[]>([])
  const idRef = useRef(0)

  const addToast = useCallback(
    (message: string, type: Toast['type'] = 'success') => {
      const id = ++idRef.current
      setToasts((prev) => [...prev, { id, message, type }])
      setTimeout(() => setToasts((prev) => prev.filter((t) => t.id !== id)), duration)
    },
    [duration],
  )

  const dismissToast = useCallback((id: number) => {
    setToasts((prev) => prev.filter((t) => t.id !== id))
  }, [])

  return { toasts, addToast, dismissToast } as const
}
