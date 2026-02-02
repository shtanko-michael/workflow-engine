import { Modal } from '../ui/Modal'
import { SCENARIOS } from '../../constants/scenarios'
import type { ScenarioId } from '../../constants/scenarios'

type NewChatModalProps = {
  title: string
  onTitleChange: (value: string) => void
  onSelectScenario: (workflowId: ScenarioId, title?: string) => void
  onCancel: () => void
  loading?: boolean
}

export function NewChatModal({
  title,
  onTitleChange,
  onSelectScenario,
  onCancel,
  loading = false,
}: NewChatModalProps) {
  return (
    <Modal onClose={onCancel} maxWidth="md">
      <h2 className="mb-4 text-lg font-semibold text-neutral-100">New chat</h2>
      <label className="mb-3 block text-sm font-medium text-neutral-400">
        Chat name (optional)
      </label>
      <input
        type="text"
        value={title}
        onChange={(e) => onTitleChange(e.target.value)}
        placeholder="e.g. My chat"
        className="mb-4 w-full rounded-lg border border-neutral-700 bg-neutral-800 px-3 py-2 text-sm text-neutral-100 placeholder:text-neutral-500 focus:outline-none focus:ring-2 focus:ring-emerald-500"
      />
      <label className="mb-2 block text-sm font-medium text-neutral-400">
        Choose scenario
      </label>
      <ul className="space-y-2">
        {SCENARIOS.map((scenario) => (
          <li key={scenario.id}>
            <button
              type="button"
              onClick={() => onSelectScenario(scenario.id, title)}
              disabled={loading}
              className="w-full rounded-lg border border-neutral-700 bg-neutral-800 px-4 py-3 text-left transition hover:bg-neutral-700 disabled:opacity-50"
            >
              <div className="font-medium text-neutral-100">{scenario.name}</div>
              <div className="mt-1 text-xs text-neutral-500">
                {scenario.description}
              </div>
            </button>
          </li>
        ))}
      </ul>
      <button
        type="button"
        onClick={onCancel}
        className="mt-4 w-full rounded-lg border border-neutral-700 px-3 py-2 text-sm text-neutral-300 transition hover:bg-neutral-800"
      >
        Cancel
      </button>
    </Modal>
  )
}
