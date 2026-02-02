import { Modal } from '../ui/Modal'
import type { Dialog } from '../../types'

type DeleteDialogModalProps = {
  dialog: Dialog
  onConfirm: () => void
  onCancel: () => void
  loading?: boolean
}

export function DeleteDialogModal({
  dialog,
  onConfirm,
  onCancel,
  loading = false,
}: DeleteDialogModalProps) {
  return (
    <Modal onClose={onCancel} maxWidth="sm">
      <h2 className="mb-2 text-lg font-semibold text-neutral-100">
        Delete chat?
      </h2>
      <p className="mb-4 text-sm text-neutral-400">
        &quot;{dialog.title}&quot; will be permanently deleted. This action
        cannot be undone.
      </p>
      <div className="flex gap-3">
        <button
          type="button"
          onClick={onCancel}
          className="flex-1 rounded-lg border border-neutral-700 px-3 py-2 text-sm text-neutral-300 transition hover:bg-neutral-800"
        >
          Cancel
        </button>
        <button
          type="button"
          onClick={onConfirm}
          disabled={loading}
          className="flex-1 rounded-lg bg-red-600 px-3 py-2 text-sm font-medium text-white transition hover:bg-red-700 disabled:opacity-50"
        >
          Delete
        </button>
      </div>
    </Modal>
  )
}
