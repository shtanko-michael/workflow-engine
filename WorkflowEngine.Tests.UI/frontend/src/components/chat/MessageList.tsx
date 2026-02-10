import { useRef, useEffect } from 'react'
import { MessageRow } from './MessageRow'
import { StreamingBubble } from './StreamingBubble'
import type { MessageWithVersions } from '../../types'

type MessageListProps = {
  messages: MessageWithVersions[]
  pendingResponse: boolean
  streamingContent: string
  /** When set, the message with this id is being streamed (shown in list with optional indicator). */
  streamingMessageId: string | null
  editingMessageId: string | null
  editContent: string
  onEditContentChange: (value: string) => void
  onStartEdit: (messageId: string, content: string) => void
  onSaveEdit: (versionId: string, content: string) => void
  onCancelEdit: () => void
  onSwitchVersion: (versionId: string) => void
  /** When user selects a quick-reply option, send it as the next message. */
  onOptionSelect?: (option: string) => void
  loading?: boolean
}

export function MessageList({
  messages,
  pendingResponse,
  streamingContent,
  streamingMessageId,
  editingMessageId,
  editContent,
  onEditContentChange,
  onStartEdit,
  onSaveEdit,
  onCancelEdit,
  onSwitchVersion,
  onOptionSelect,
  loading = false,
}: MessageListProps) {
  const scrollRef = useRef<HTMLDivElement | null>(null)

  // Show streaming bubble only when waiting for first chunk (no messageId yet); once we have streamingMessageId, the message is in the list
  const showStreamingBubble = pendingResponse && !streamingMessageId

  useEffect(() => {
    scrollRef.current?.scrollTo({
      top: scrollRef.current.scrollHeight,
      behavior: 'smooth',
    })
  }, [messages, showStreamingBubble, streamingContent])

  return (
    <div ref={scrollRef} className="flex-1 overflow-y-auto">
      {messages.length === 0 && !pendingResponse && (
        <div className="mx-auto mt-16 max-w-3xl px-6 text-center text-sm text-neutral-500">
          No messages yet.
        </div>
      )}
      {messages.map((message, index) => (
        <MessageRow
          key={`${message.messageId}-${message.activeVersionId}-${index}`}
          message={message}
          isEditing={editingMessageId === message.activeVersionId}
          editContent={editContent}
          onEditContentChange={onEditContentChange}
          onStartEdit={() =>
            onStartEdit(message.activeVersionId, message.content)
          }
          onSaveEdit={() => onSaveEdit(message.activeVersionId, editContent)}
          onCancelEdit={onCancelEdit}
          onSwitchVersion={onSwitchVersion}
          onOptionSelect={onOptionSelect}
          loading={loading}
          isStreamingPlaceholder={message.messageId === streamingMessageId}
        />
      ))}
      {showStreamingBubble && (
        <StreamingBubble content={streamingContent} />
      )}
    </div>
  )
}
