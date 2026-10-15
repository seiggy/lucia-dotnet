import { useContext } from 'react'
import { ConfirmContext } from '../components/confirm-context'
import type { ConfirmFn } from '../components/confirm-context'

/**
 * Returns `confirm(options)`, which opens the themed confirmation dialog and resolves
 * `true` on confirm and `false` on cancel, Esc or backdrop click.
 *
 * @example
 * const confirm = useConfirm()
 * if (!(await confirm({ title: 'Delete provider?', message: 'Agents using it fall back to the default.', confirmLabel: 'Delete provider' }))) return
 */
export function useConfirm(): ConfirmFn {
  const confirm = useContext(ConfirmContext)
  if (!confirm) throw new Error('useConfirm must be used within ConfirmProvider')
  return confirm
}
