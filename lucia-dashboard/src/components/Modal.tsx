import { useEffect, useId, useRef } from 'react'
import type { ReactNode } from 'react'

interface ModalProps {
  open: boolean
  onClose: () => void
  /** Visible heading; also names the dialog for screen readers. */
  title: string
  children: ReactNode
  /** Max width of the panel. Defaults to `md`. */
  size?: 'sm' | 'md' | 'lg'
  /** Ref of the element to focus on open. Defaults to the first focusable element in the panel. */
  initialFocusRef?: React.RefObject<HTMLElement | null>
  /** Extra classes for the panel, for pages that need a taller or wider body. */
  className?: string
  /** Set to false for forms holding typed input, so a stray click outside cannot discard it. Esc still closes. */
  dismissOnBackdrop?: boolean
  /** Id of the element that describes the dialog, announced after the title. */
  descriptionId?: string
}

const SIZE_CLASS = { sm: 'max-w-sm', md: 'max-w-lg', lg: 'max-w-2xl' } as const

const FOCUSABLE = 'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'

/**
 * Accessible modal built on the native `<dialog>` element.
 *
 * `showModal()` makes the rest of the page inert and traps Tab. Esc and a click on the
 * backdrop call `onClose`. Focus moves into the panel on open and returns to the trigger on close.
 */
export default function Modal({ open, onClose, title, children, size = 'md', initialFocusRef, className = '', dismissOnBackdrop = true, descriptionId }: ModalProps) {
  const dialogRef = useRef<HTMLDialogElement>(null)
  const titleId = useId()

  useEffect(() => {
    if (!open) return
    const dialog = dialogRef.current
    if (!dialog) return
    const previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null
    if (!dialog.open) dialog.showModal()
    const target = initialFocusRef?.current ?? dialog.querySelector<HTMLElement>(FOCUSABLE)
    target?.focus()
    return () => {
      if (dialog.open) dialog.close()
      previousFocus?.focus()
    }
  }, [open, initialFocusRef])

  if (!open) return null

  return (
    <dialog
      ref={dialogRef}
      aria-labelledby={titleId}
      aria-describedby={descriptionId}
      className="fixed inset-0 z-50 h-dvh w-screen max-h-none max-w-none border-0 bg-transparent p-0 backdrop:bg-black/60 backdrop:backdrop-blur-sm"
      onCancel={(event) => {
        event.preventDefault()
        onClose()
      }}
    >
      <div
        className="flex h-full items-center justify-center p-4"
        onClick={(event) => {
          // The wrapper fills the dialog, so a click that lands on it (not the panel) is a backdrop click.
          if (dismissOnBackdrop && event.target === event.currentTarget) onClose()
        }}
      >
        <div className={`flex max-h-full w-full flex-col rounded-xl border border-stone/40 bg-obsidian shadow-2xl ${SIZE_CLASS[size]} ${className}`}>
          <h2 id={titleId} className="shrink-0 px-6 pt-6 font-display text-base font-semibold text-light">
            {title}
          </h2>
          <div className="min-h-0 flex-1 overflow-y-auto px-6 pb-6 pt-3">{children}</div>
        </div>
      </div>
    </dialog>
  )
}
