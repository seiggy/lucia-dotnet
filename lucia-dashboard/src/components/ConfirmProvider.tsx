import { useCallback, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import ConfirmDialog from './ConfirmDialog'
import { ConfirmContext } from './confirm-context'
import type { ConfirmFn, ConfirmOptions } from './confirm-context'

/** Provides {@link useConfirm}: an awaitable replacement for `window.confirm`. */
export default function ConfirmProvider({ children }: { children: ReactNode }) {
  const [options, setOptions] = useState<ConfirmOptions | null>(null)
  const resolveRef = useRef<((confirmed: boolean) => void) | null>(null)

  const confirm = useCallback<ConfirmFn>(next => {
    // A second request while one is open cancels the first rather than stacking dialogs.
    resolveRef.current?.(false)
    return new Promise<boolean>(resolve => {
      resolveRef.current = resolve
      setOptions(next)
    })
  }, [])

  const settle = (confirmed: boolean) => {
    resolveRef.current?.(confirmed)
    resolveRef.current = null
    setOptions(null)
  }

  return (
    <ConfirmContext.Provider value={confirm}>
      {children}
      <ConfirmDialog
        open={options !== null}
        title={options?.title ?? ''}
        message={options?.message ?? ''}
        confirmLabel={options?.confirmLabel}
        cancelLabel={options?.cancelLabel}
        tone={options?.tone}
        onConfirm={() => settle(true)}
        onCancel={() => settle(false)}
      />
    </ConfirmContext.Provider>
  )
}
