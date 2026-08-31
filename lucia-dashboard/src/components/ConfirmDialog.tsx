import { useEffect, useRef } from 'react'

interface ConfirmDialogProps {
  open: boolean
  title: string
  message: string
  confirmLabel?: string
  onConfirm: () => void
  onCancel: () => void
}

/**
 * Modal confirmation dialog.
 *
 * Renders a centered overlay with a title, message, cancel and confirm buttons.
 * The confirm button uses a destructive (red) style by default.
 */
export default function ConfirmDialog({
  open,
  title,
  message,
  confirmLabel = 'Delete',
  onConfirm,
  onCancel,
}: ConfirmDialogProps) {
  const dialogRef = useRef<HTMLDialogElement>(null)
  const cancelButtonRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    if (!open) return
    const dialog = dialogRef.current
    const previousFocus = document.activeElement instanceof HTMLElement
      ? document.activeElement
      : null
    if (dialog && !dialog.open) {
      dialog.showModal()
      cancelButtonRef.current?.focus()
    }
    return () => previousFocus?.focus()
  }, [open])

  if (!open) return null
  return (
    <dialog
      ref={dialogRef}
      className="fixed inset-0 z-50 h-screen w-screen max-h-none max-w-none border-0 bg-black/60 p-0 backdrop-blur-sm"
      aria-modal="true"
      aria-labelledby="confirm-dialog-title"
      aria-describedby="confirm-dialog-message"
      onCancel={(event) => {
        event.preventDefault()
        onCancel()
      }}
    >
      <div className="flex h-full items-center justify-center p-4">
        <div className="w-full max-w-sm rounded-xl border border-stone/40 bg-obsidian p-6 shadow-2xl">
        <h3 id="confirm-dialog-title" className="text-base font-semibold text-light">
          {title}
        </h3>
        <p id="confirm-dialog-message" className="mt-2 text-sm text-fog">
          {message}
        </p>
        <div className="mt-5 flex justify-end gap-3">
          <button
            ref={cancelButtonRef}
            type="button"
            onClick={onCancel}
            className="min-h-11 rounded-lg px-4 py-2 text-sm text-fog hover:text-cloud hover:bg-stone/40 transition-colors"
          >
            Cancel
          </button>
          <button
            type="button"
            onClick={onConfirm}
            className="min-h-11 rounded-lg bg-rose/20 px-4 py-2 text-sm font-medium text-rose hover:bg-rose/30 transition-colors"
          >
            {confirmLabel}
          </button>
        </div>
      </div>
      </div>
    </dialog>
  )
}
