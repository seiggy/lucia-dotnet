import { createContext } from 'react'

export interface ConfirmOptions {
  title: string
  /** Say what will happen and what it affects, not just "Are you sure?". */
  message: string
  confirmLabel?: string
  cancelLabel?: string
  /** Defaults to `danger`. */
  tone?: 'danger' | 'default'
}

export type ConfirmFn = (options: ConfirmOptions) => Promise<boolean>

export const ConfirmContext = createContext<ConfirmFn | null>(null)
