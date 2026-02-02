import type { Dialog } from '../../types'

type DialogListItemProps = {
  dialog: Dialog
  isActive: boolean
  onSelect: () => void
  onDelete: (e: React.MouseEvent) => void
}

export function DialogListItem({
  dialog,
  isActive,
  onSelect,
  onDelete,
}: DialogListItemProps) {
  return (
    <div
      className={`group flex w-full items-center gap-1 rounded-lg px-3 py-2 text-left text-sm transition ${
        isActive
          ? 'bg-neutral-800 text-white'
          : 'bg-neutral-900 text-neutral-300 hover:bg-neutral-800'
      }`}
    >
      <button
        type="button"
        onClick={onSelect}
        className="min-w-0 flex-1 text-left"
      >
        <div className="truncate font-medium">{dialog.title}</div>
      </button>
      <button
        type="button"
        onClick={onDelete}
        className="shrink-0 rounded p-1.5 text-neutral-500 opacity-0 transition hover:bg-neutral-700 hover:text-red-400 group-hover:opacity-100"
        title="Delete chat"
      >
        <TrashIcon />
      </button>
    </div>
  )
}

function TrashIcon() {
  return (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      width="16"
      height="16"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <path d="M3 6h18" />
      <path d="M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6" />
      <path d="M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2" />
      <line x1="10" y1="11" x2="10" y2="17" />
      <line x1="14" y1="11" x2="14" y2="17" />
    </svg>
  )
}
