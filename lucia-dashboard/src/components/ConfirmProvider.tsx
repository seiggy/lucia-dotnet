import { useCallback, useEffect, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { useLocation } from 'react-router-dom'
import ConfirmDialog from './ConfirmDialog'
import { ConfirmContext } from './confirm-context'
import type { ConfirmFn, ConfirmOptions } from './confirm-context'

/** Provides {@link useConfirm}: an awaitable replacement for `window.confirm`. */
export default function ConfirmProvider({ children }: { children: ReactNode }) {
  const [pending, setPending] = useState<{ options: ConfirmOptions; locationKey: string } | null>(null)
  const resolveRef = useRef<((confirmed: boolean) => void) | null>(null)
  const { key: locationKey } = useLocation()
  const locationKeyRef = useRef(locationKey)

  useEffect(() => {
    locationKeyRef.current = locationKey
    // Back or a link while a confirmation is open would leave it over a page that no longer owns the action.
    // Cancel it; the dialog hides on its own because its location key no longer matches.
    resolveRef.current?.(false)
    resolveRef.current = null
    // Navigation is the external event here. Without clearing, Forward would bring the same dialog back with nothing to settle.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setPending(null)
  }, [locationKey])

  const confirm = useCallback<ConfirmFn>(next => {
    // A second request while one is open cancels the first rather than stacking dialogs.
    resolveRef.current?.(false)
    return new Promise<boolean>(resolve => {
      resolveRef.current = resolve
      setPending({ options: next, locationKey: locationKeyRef.current })
    })
  }, [])

  const settle = (confirmed: boolean) => {
    resolveRef.current?.(confirmed)
    resolveRef.current = null
    setPending(null)
  }

  const options = pending?.options

  return (
    <ConfirmContext.Provider value={confirm}>
      {children}
      <ConfirmDialog
        open={pending !== null && pending.locationKey === locationKey}
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