import type { MessageWithVersions } from '../../types'

type MessageRowProps = {
  message: MessageWithVersions
  isEditing: boolean
  editContent: string
  onEditContentChange: (value: string) => void
  onStartEdit: () => void
  onSaveEdit: () => void
  onCancelEdit: () => void
  onSwitchVersion: (versionId: string) => void
  /** When user clicks a quick-reply option, send it as the next message. */
  onOptionSelect?: (option: string) => void
  loading?: boolean
  /** True when this row is the streaming placeholder (no messages yet, streaming first response). */
  isStreamingPlaceholder?: boolean
}

const roleLabel: Record<MessageWithVersions['role'], string> = {
  user: 'You',
  assistant: 'Assistant',
  system: 'System',
}

const avatarStyles: Record<
  MessageWithVersions['role'],
  { label: string; className: string }
> = {
  user: { label: 'U', className: 'bg-emerald-500 text-black' },
  assistant: { label: 'AI', className: 'bg-indigo-500 text-black' },
  system: { label: 'S', className: 'bg-amber-500 text-black' },
}

export function MessageRow({
  message,
  isEditing,
  editContent,
  onEditContentChange,
  onStartEdit,
  onSaveEdit,
  onCancelEdit,
  onSwitchVersion,
  onOptionSelect,
  loading = false,
  isStreamingPlaceholder = false,
}: MessageRowProps) {
  const isUser = message.role === 'user'
  const avatar = avatarStyles[message.role]
  const rowClass = isUser ? 'bg-neutral-900' : 'bg-neutral-950'
  const hasVersions = !isStreamingPlaceholder && message.totalVersions > 1
  const canEdit = isUser && !loading && !isStreamingPlaceholder

  return (
    <div
      className={`group relative border-b border-neutral-800/70 py-6 ${rowClass}`}
    >
      <div className="mx-auto flex w-full max-w-3xl gap-4 px-6">
        <div
          className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-full text-xs font-semibold ${avatar.className}`}
        >
          {avatar.label}
        </div>
        <div className="flex-1">
          <div className="mb-1 flex items-center justify-between">
            <div className="text-xs uppercase tracking-wide text-neutral-500">
              {roleLabel[message.role]}
            </div>
            {isUser && (
              <button
                type="button"
                onClick={onStartEdit}
                disabled={!canEdit}
                className="rounded px-2 py-1 text-xs text-neutral-400 opacity-0 transition hover:bg-neutral-800 hover:text-neutral-200 group-hover:opacity-100 disabled:opacity-50"
              >
                Edit
              </button>
            )}
          </div>

          {isEditing ? (
            <div className="space-y-2">
              <textarea
                value={editContent}
                onChange={(e) => onEditContentChange(e.target.value)}
                className="w-full rounded border border-neutral-700 bg-neutral-800 px-3 py-2 text-sm text-neutral-100 focus:outline-none focus:ring-2 focus:ring-emerald-500"
                rows={3}
              />
              <div className="flex gap-2">
                <button
                  type="button"
                  onClick={onSaveEdit}
                  disabled={loading}
                  className="rounded bg-emerald-600 px-3 py-1 text-xs text-white transition hover:bg-emerald-700 disabled:opacity-50"
                >
                  Save
                </button>
                <button
                  type="button"
                  onClick={onCancelEdit}
                  className="rounded bg-neutral-700 px-3 py-1 text-xs text-neutral-300 transition hover:bg-neutral-600"
                >
                  Cancel
                </button>
              </div>
            </div>
          ) : (
            <>
              {isStreamingPlaceholder && !message.content ? (
                <div className="flex items-center gap-2 text-sm text-neutral-300">
                  <span className="h-2 w-2 animate-bounce rounded-full bg-neutral-400" />
                  <span className="h-2 w-2 animate-bounce rounded-full bg-neutral-400 [animation-delay:150ms]" />
                  <span className="h-2 w-2 animate-bounce rounded-full bg-neutral-400 [animation-delay:300ms]" />
                  <span className="ml-2 text-neutral-500">Thinking...</span>
                </div>
              ) : (
                <>
                  <div className="whitespace-pre-wrap text-sm leading-relaxed text-neutral-100">
                    {message.content}
                  </div>
                  {!isUser && message.options && message.options.length > 0 && (
                    <div className="mt-3 flex flex-wrap gap-2">
                      {message.options.map((opt) => (
                        <button
                          key={opt}
                          type="button"
                          onClick={() => onOptionSelect?.(opt)}
                          disabled={loading}
                          className="rounded-lg border border-neutral-600 bg-neutral-800/80 px-3 py-1.5 text-sm text-neutral-200 transition hover:border-neutral-500 hover:bg-neutral-700/80 disabled:opacity-50"
                        >
                          {opt}
                        </button>
                      ))}
                    </div>
                  )}
                </>
              )}
              {hasVersions && (
                <VersionSwitcher
                  message={message}
                  onSwitchVersion={onSwitchVersion}
                  loading={loading}
                />
              )}
            </>
          )}
        </div>
      </div>
    </div>
  )
}

type VersionSwitcherProps = {
  message: MessageWithVersions
  onSwitchVersion: (versionId: string) => void
  loading?: boolean
}

function VersionSwitcher({
  message,
  onSwitchVersion,
  loading = false,
}: VersionSwitcherProps) {
  const { currentVersionIndex, totalVersions, versions } = message
  const canGoPrev = currentVersionIndex > 0 && !loading
  const canGoNext =
    currentVersionIndex < totalVersions - 1 && !loading

  const handlePrev = () => {
    if (canGoPrev) {
      const prevVersion = versions[currentVersionIndex - 1]
      if (prevVersion) onSwitchVersion(prevVersion.id)
    }
  }

  const handleNext = () => {
    if (canGoNext) {
      const nextVersion = versions[currentVersionIndex + 1]
      if (nextVersion) onSwitchVersion(nextVersion.id)
    }
  }

  return (
    <div className="mt-3 flex items-center gap-1.5 text-xs">
      <button
        type="button"
        onClick={handlePrev}
        disabled={!canGoPrev}
        className="flex h-6 w-6 items-center justify-center rounded border border-neutral-700 bg-neutral-800 text-neutral-400 transition hover:bg-neutral-700 hover:text-neutral-200 disabled:cursor-not-allowed disabled:opacity-30"
        title="Previous version"
      >
        ◄
      </button>
      <span className="min-w-12 rounded border border-neutral-700 bg-neutral-800 px-2 py-1 text-center font-medium text-neutral-300">
        {currentVersionIndex + 1}/{totalVersions}
      </span>
      <button
        type="button"
        onClick={handleNext}
        disabled={!canGoNext}
        className="flex h-6 w-6 items-center justify-center rounded border border-neutral-700 bg-neutral-800 text-neutral-400 transition hover:bg-neutral-700 hover:text-neutral-200 disabled:cursor-not-allowed disabled:opacity-30"
        title="Next version"
      >
        ►
      </button>
    </div>
  )
}
