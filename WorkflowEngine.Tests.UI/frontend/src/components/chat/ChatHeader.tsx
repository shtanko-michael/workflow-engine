import { SCENARIOS } from '../../constants/scenarios'
import type { Dialog } from '../../types'

type ChatHeaderProps = {
  activeDialog: Dialog | null
}

export function ChatHeader({ activeDialog }: ChatHeaderProps) {
  const scenarioName = activeDialog
    ? SCENARIOS.find((s) => s.id === activeDialog.workflowType)?.name ??
      activeDialog.workflowType
    : '—'

  return (
    <header className="border-b border-neutral-800 bg-neutral-950/70 px-6 py-4 backdrop-blur">
      <div className="flex items-center justify-between">
        <h1 className="text-lg font-semibold">
          {activeDialog ? activeDialog.title : 'Select a chat'}
        </h1>
        <div className="rounded-full border border-neutral-800 px-3 py-1 text-xs text-neutral-400">
          {scenarioName}
        </div>
      </div>
    </header>
  )
}
