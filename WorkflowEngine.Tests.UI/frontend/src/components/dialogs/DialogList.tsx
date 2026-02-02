import { DialogListItem } from './DialogListItem'
import type { Dialog } from '../../types'

type DialogListProps = {
  dialogs: Dialog[]
  activeDialogId: string | null
  onSelectDialog: (id: string) => void
  onDeleteDialog: (dialog: Dialog) => void
}

export function DialogList({
  dialogs,
  activeDialogId,
  onSelectDialog,
  onDeleteDialog,
}: DialogListProps) {
  return (
    <div className="flex-1 space-y-2 overflow-y-auto pr-1">
      {dialogs.length === 0 && (
        <p className="text-sm text-neutral-500">No dialogs yet.</p>
      )}
      {dialogs.map((dialog) => (
        <DialogListItem
          key={dialog.id}
          dialog={dialog}
          isActive={dialog.id === activeDialogId}
          onSelect={() => onSelectDialog(dialog.id)}
          onDelete={(e) => {
            e.stopPropagation()
            onDeleteDialog(dialog)
          }}
        />
      ))}
    </div>
  )
}
