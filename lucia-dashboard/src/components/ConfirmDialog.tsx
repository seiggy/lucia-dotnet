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
  if (!open) return null
  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm"
      role="dialog"
      aria-modal="true"
      aria-labelledby="confirm-dialog-title"
      aria-describedby="confirm-dialog-message"
    >
      <div className="w-full max-w-sm rounded-xl border border-stone/40 bg-obsidian p-6 shadow-2xl">
        <h3 id="confirm-dialog-title" className="text-base font-semibold text-light">
          {title}
        </h3>
        <p id="confirm-dialog-message" className="mt-2 text-sm text-fog">
          {message}
        </p>
        <div className="mt-5 flex justify-end gap-3">
          <button
            onClick={onCancel}
            className="rounded-lg px-4 py-2 text-sm text-fog hover:text-cloud hover:bg-stone/40 transition-colors"
          >
            Cancel
          </button>
          <button
            onClick={onConfirm}
            className="rounded-lg bg-rose/20 px-4 py-2 text-sm font-medium text-rose hover:bg-rose/30 transition-colors"
          >
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  )
}
