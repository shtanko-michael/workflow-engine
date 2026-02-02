type ChatInputProps = {
  value: string
  onChange: (value: string) => void
  onSend: () => void
  disabled?: boolean
  sendDisabled?: boolean
  placeholder?: string
}

export function ChatInput({
  value,
  onChange,
  onSend,
  disabled = false,
  sendDisabled = false,
  placeholder = 'Send a message...',
}: ChatInputProps) {
  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter') {
      onSend()
    }
  }

  return (
    <div className="mx-auto w-full max-w-3xl px-4 py-4">
      <div className="flex items-end gap-3 rounded-2xl border border-neutral-800 bg-neutral-900 px-4 py-3 shadow-sm">
        <input
          type="text"
          value={value}
          onChange={(e) => onChange(e.target.value)}
          onKeyDown={handleKeyDown}
          disabled={disabled}
          placeholder={placeholder}
          className="h-9 flex-1 bg-transparent text-sm text-neutral-100 placeholder:text-neutral-500 focus:outline-none"
        />
        <button
          type="button"
          onClick={onSend}
          disabled={sendDisabled}
          className="rounded-xl bg-emerald-500 px-4 py-2 text-sm font-semibold text-black transition hover:bg-emerald-400 disabled:opacity-50"
        >
          Send
        </button>
      </div>
    </div>
  )
}
