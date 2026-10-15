import { useId, useRef } from 'react'
import Modal from './Modal'

interface ConfirmDialogProps {
  open: boolean
  title: string
  message: string
  confirmLabel?: string
  cancelLabel?: string
  /** `danger` (default) styles the confirm button red; use `default` for routine, non-destructive confirms. */
  tone?: 'danger' | 'default'
  onConfirm: () => void
  onCancel: () => void
}

/**
 * Confirmation dialog on top of {@link Modal}.
 *
 * Focus starts on Cancel so Enter never confirms by accident.
 */
export default function ConfirmDialog({
  open,
  title,
  message,
  confirmLabel = 'Delete',
  cancelLabel = 'Cancel',
  tone = 'danger',
  onConfirm,
  onCancel,
}: ConfirmDialogProps) {
  const cancelButtonRef = useRef<HTMLButtonElement>(null)
  const messageId = useId()

  return (
    <Modal open={open} onClose={onCancel} title={title} size="sm" initialFocusRef={cancelButtonRef} descriptionId={messageId}>
      <p id={messageId} className="text-sm text-fog">{message}</p>
      <div className="mt-5 flex justify-end gap-3">
        <button
          ref={cancelButtonRef}
          type="button"
          onClick={onCancel}
          className="min-h-11 rounded-lg px-4 py-2 text-sm text-fog transition-colors hover:bg-stone/40 hover:text-cloud"
        >
          {cancelLabel}
        </button>
        <button
          type="button"
          onClick={onConfirm}
          className={`min-h-11 rounded-lg px-4 py-2 text-sm font-medium transition-colors ${
            tone === 'danger'
              ? 'bg-rose/20 text-rose hover:bg-rose/30'
              : 'bg-amber text-on-accent hover:bg-amber-glow'
          }`}
        >
          {confirmLabel}
        </button>
      </div>
    </Modal>
  )
}
