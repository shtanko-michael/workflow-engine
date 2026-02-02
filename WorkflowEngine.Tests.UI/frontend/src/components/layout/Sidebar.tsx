import { DialogList } from '../dialogs/DialogList'
import { NewChatModal } from '../modals/NewChatModal'
import { DeleteDialogModal } from '../modals/DeleteDialogModal'
import type { Dialog } from '../../types'
import type { ScenarioId } from '../../constants/scenarios'

type SidebarProps = {
  dialogs: Dialog[]
  activeDialogId: string | null
  loading: boolean
  showNewChatModal: boolean
  newDialogTitle: string
  dialogToDelete: Dialog | null
  onNewChatClick: () => void
  onNewChatTitleChange: (value: string) => void
  onCreateDialog: (workflowId: ScenarioId, title?: string) => void
  onCancelNewChat: () => void
  onSelectDialog: (id: string) => void
  onDeleteDialogClick: (dialog: Dialog) => void
  onConfirmDeleteDialog: () => void
  onCancelDeleteDialog: () => void
}

export function Sidebar({
  dialogs,
  activeDialogId,
  loading,
  showNewChatModal,
  newDialogTitle,
  dialogToDelete,
  onNewChatClick,
  onNewChatTitleChange,
  onCreateDialog,
  onCancelNewChat,
  onSelectDialog,
  onDeleteDialogClick,
  onConfirmDeleteDialog,
  onCancelDeleteDialog,
}: SidebarProps) {
  return (
    <aside className="flex w-72 flex-col border-r border-neutral-800 bg-neutral-950 p-4">
      <button
        type="button"
        onClick={onNewChatClick}
        disabled={loading}
        className="mb-4 w-full rounded-lg border border-neutral-800 bg-neutral-900 px-3 py-2 text-left text-sm font-medium text-neutral-200 transition hover:bg-neutral-800 disabled:opacity-50"
      >
        + New chat
      </button>

      {showNewChatModal && (
        <NewChatModal
          title={newDialogTitle}
          onTitleChange={onNewChatTitleChange}
          onSelectScenario={onCreateDialog}
          onCancel={onCancelNewChat}
          loading={loading}
        />
      )}

      {dialogToDelete && (
        <DeleteDialogModal
          dialog={dialogToDelete}
          onConfirm={onConfirmDeleteDialog}
          onCancel={onCancelDeleteDialog}
          loading={loading}
        />
      )}

      <div className="mb-3 flex items-center justify-between text-xs uppercase tracking-wide text-neutral-500">
        <span>Chats</span>
        <span>{dialogs.length}</span>
      </div>

      <DialogList
        dialogs={dialogs}
        activeDialogId={activeDialogId}
        onSelectDialog={onSelectDialog}
        onDeleteDialog={onDeleteDialogClick}
      />
    </aside>
  )
}
