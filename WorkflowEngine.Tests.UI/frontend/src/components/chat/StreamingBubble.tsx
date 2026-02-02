type StreamingBubbleProps = {
  content: string
}

export function StreamingBubble({ content }: StreamingBubbleProps) {
  return (
    <div className="border-b border-neutral-800/70 bg-neutral-950 py-6">
      <div className="mx-auto flex w-full max-w-3xl gap-4 px-6">
        <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-indigo-500 text-xs font-semibold text-black">
          AI
        </div>
        <div className="flex-1">
          <div className="mb-1 text-xs uppercase tracking-wide text-neutral-500">
            Assistant
          </div>
          {content ? (
            <div className="whitespace-pre-wrap text-sm leading-relaxed text-neutral-100">
              {content}
            </div>
          ) : (
            <div className="flex items-center gap-2 text-sm text-neutral-300">
              <span className="h-2 w-2 animate-bounce rounded-full bg-neutral-400" />
              <span className="h-2 w-2 animate-bounce rounded-full bg-neutral-400 [animation-delay:150ms]" />
              <span className="h-2 w-2 animate-bounce rounded-full bg-neutral-400 [animation-delay:300ms]" />
              <span className="ml-2 text-neutral-500">Thinking...</span>
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
