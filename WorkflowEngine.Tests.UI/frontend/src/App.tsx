import { useEffect, useMemo, useRef, useState } from 'react'
import {
  HubConnectionBuilder,
  type HubConnection,
} from '@microsoft/signalr'

type Dialog = {
  id: string
  title: string
  threadId: string
  workflowType: string
  lastCheckpointId?: string | null
  lastInterruptRequestId?: string | null
  createdAt: string
  updatedAt: string
}

type Message = {
  id: string
  dialogId: string
  role: 'user' | 'assistant' | 'system'
  content: string
  createdAt: string
  requestId?: string | null
}

type MessageVersion = {
  id: string
  messageId: string
  content: string
  checkpointId: string
  createdAt: string
}

type MessageWithVersions = {
  messageId: string
  role: 'user' | 'assistant' | 'system'
  activeVersionId: string
  content: string
  currentVersionIndex: number
  totalVersions: number
  versions: MessageVersion[]
  createdAt: string
}

const apiBase = import.meta.env.VITE_API_BASE ?? 'http://localhost:5186'
const useV2Api = import.meta.env.VITE_USE_V2_API === 'true'

async function fetchJson<T>(url: string, options?: RequestInit): Promise<T> {
  const response = await fetch(url, {
    headers: { 'Content-Type': 'application/json' },
    ...options,
  })
  if (!response.ok) {
    const message = await response.text()
    throw new Error(message || 'Request failed')
  }
  return response.json() as Promise<T>
}

function App() {
  const [dialogs, setDialogs] = useState<Dialog[]>([])
  const [activeDialogId, setActiveDialogId] = useState<string | null>(null)
  const [messages, setMessages] = useState<Message[]>([])
  const [messagesV2, setMessagesV2] = useState<MessageWithVersions[]>([])
  const [input, setInput] = useState('')
  const [loading, setLoading] = useState(false)
  const [pendingResponse, setPendingResponse] = useState(false)
  const [editingMessageId, setEditingMessageId] = useState<string | null>(null)
  const [editContent, setEditContent] = useState('')
  const messagesRef = useRef<HTMLDivElement | null>(null)

  const connection = useMemo<HubConnection>(() => {
    return new HubConnectionBuilder()
      .withUrl(`${apiBase}/hubs/chat`)
      .withAutomaticReconnect([0, 1000, 5000, 10000])
      .build()
  }, [])

  useEffect(() => {
    const apiPath = useV2Api ? '/api/v2/dialogs' : '/api/dialogs'
    fetchJson<Dialog[]>(`${apiBase}${apiPath}`)
      .then(setDialogs)
      .catch(() => setDialogs([]))
  }, [])

  useEffect(() => {
    let isActive = true

    connection.start().catch((error) => {
      if (isActive) {
        console.error('Failed to start the connection', error)
      }
      throw error
    })
    return () => {
      isActive = false
      const stopConnection = async () => {
        try {
          await connection.stop
        } catch {
          // Ignore start errors during teardown.
        }
      }
      void stopConnection()
    }
  }, [])

  useEffect(() => {
    connection.on('messagesAdded', (payload: Message[]) => {
      if (!payload || payload.length === 0) return
      console.log('messagesAdded', payload)
      setMessages((prev) => {
        const map = new Map(prev.map((message) => [message.id, message]))
        for (const message of payload) {
          map.set(message.id, message)
        }
        return Array.from(map.values()).sort((a, b) =>
          a.createdAt.localeCompare(b.createdAt),
        )
      })
      if (payload.some((message) => message.role !== 'user')) {
        setPendingResponse(false)
      }
    })
    return () => {
      connection.off('messagesAdded')
    }
  }, [connection])

  useEffect(() => {
    connection.on('dialogUpdated', (payload: Dialog) => {
      if (!payload) return
      console.log('dialogUpdated', payload)
      setDialogs((prev) =>
        prev.map((dialog) =>
          dialog.id === payload.id ? payload : dialog,
        ),
      )
    })

    connection.on('messagesUpdated', (payload: MessageWithVersions[]) => {
      if (!payload || payload.length === 0) return
      console.log('messagesUpdated', payload)
      setMessagesV2(payload)
      setPendingResponse(false)
      setLoading(false) // Clear loading state when messages are updated via SignalR
    })

    return () => {
      connection.off('dialogUpdated')
      connection.off('messagesUpdated')
    }
  }, [connection])

  useEffect(() => {
    if (!activeDialogId) {
      setMessages([])
      setMessagesV2([])
      setPendingResponse(false)
      return
    }

    connection
      .invoke('JoinDialog', activeDialogId)
      .catch(() => undefined)

    if (useV2Api) {
      fetchJson<MessageWithVersions[]>(
        `${apiBase}/api/v2/dialogs/${activeDialogId}/messages`,
      )
        .then(setMessagesV2)
        .catch(() => setMessagesV2([]))
    } else {
      fetchJson<Message[]>(`${apiBase}/api/dialogs/${activeDialogId}/messages`)
        .then(setMessages)
        .catch(() => setMessages([]))
    }

    return () => {
      connection.invoke('LeaveDialog', activeDialogId).catch(() => undefined)
    }
  }, [activeDialogId, connection])

  useEffect(() => {
    messagesRef.current?.scrollTo({
      top: messagesRef.current.scrollHeight,
      behavior: 'smooth',
    })
  }, [messages])

  useEffect(() => {
    setPendingResponse(false)
  }, [activeDialogId])

  const activeDialog = dialogs.find((dialog) => dialog.id === activeDialogId) ?? null

  const handleCreateDialog = async () => {
    setLoading(true)
    try {
      const apiPath = useV2Api ? '/api/v2/dialogs' : '/api/dialogs'
      const dialog = await fetchJson<Dialog>(`${apiBase}${apiPath}`, {
        method: 'POST',
        body: JSON.stringify({ title: 'New dialog' }),
      })
      setDialogs((prev) => [dialog, ...prev])
      setActiveDialogId(dialog.id)
      
      if (useV2Api) {
        const dialogMessages = await fetchJson<MessageWithVersions[]>(
          `${apiBase}/api/v2/dialogs/${dialog.id}/messages`,
        )
        setMessagesV2(dialogMessages)
      } else {
        const dialogMessages = await fetchJson<Message[]>(
          `${apiBase}/api/dialogs/${dialog.id}/messages`,
        )
        setMessages(dialogMessages)
      }
    } catch (error) {
      console.error(error)
    } finally {
      setLoading(false)
    }
  }

  const handleEditMessage = async (versionId: string, newContent: string) => {
    if (!activeDialog) return
    setLoading(true)
    try {
      await fetchJson(`${apiBase}/api/v2/dialogs/${activeDialog.id}/messages/edit`, {
        method: 'POST',
        body: JSON.stringify({ versionId, content: newContent }),
      })
      setEditingMessageId(null)
      setEditContent('')
    } catch (error) {
      console.error(error)
    } finally {
      setLoading(false)
    }
  }

  const handleSwitchVersion = async (versionId: string) => {
    if (!activeDialog) return
    setLoading(true)
    try {
      await fetchJson(`${apiBase}/api/v2/dialogs/${activeDialog.id}/messages/switch-version`, {
        method: 'POST',
        body: JSON.stringify({ versionId }),
      })
      // Messages will be updated via SignalR 'messagesUpdated' event
      // No need to manually fetch - SignalR will push the update
    } catch (error) {
      console.error(error)
      setLoading(false)
    }
  }

  const handleSendMessage = async () => {
    if (!activeDialog || !input.trim()) return
    if (!activeDialog.lastCheckpointId) return

    const content = input.trim()
    setInput('')
    setLoading(true)
    setPendingResponse(true)

    try {
      if (useV2Api) {
        await fetchJson(`${apiBase}/api/v2/dialogs/${activeDialog.id}/messages`, {
          method: 'POST',
          body: JSON.stringify({
            content,
            threadId: activeDialog.threadId,
            checkpointId: activeDialog.lastCheckpointId,
            requestId: activeDialog.lastInterruptRequestId ?? null,
          }),
        })
      } else {
        const added = await fetchJson<Message[]>(
          `${apiBase}/api/dialogs/${activeDialog.id}/messages`,
          {
            method: 'POST',
            body: JSON.stringify({
              content,
              threadId: activeDialog.threadId,
              checkpointId: activeDialog.lastCheckpointId,
              requestId: activeDialog.lastInterruptRequestId ?? null,
            }),
          },
        )

        setMessages((prev) => {
          const map = new Map(prev.map((message) => [message.id, message]))
          for (const message of added) {
            map.set(message.id, message)
          }
          return Array.from(map.values()).sort((a, b) =>
            a.createdAt.localeCompare(b.createdAt),
          )
        })
      }
    } catch (error) {
      console.error(error)
      setPendingResponse(false)
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="flex h-screen bg-neutral-900 text-neutral-100">
      <aside className="flex w-72 flex-col border-r border-neutral-800 bg-neutral-950 p-4">
        <button
          onClick={handleCreateDialog}
          disabled={loading}
          className="mb-4 w-full rounded-lg border border-neutral-800 bg-neutral-900 px-3 py-2 text-left text-sm font-medium text-neutral-200 transition hover:bg-neutral-800 disabled:opacity-50"
        >
          + New chat
        </button>
        <div className="mb-3 flex items-center justify-between text-xs uppercase tracking-wide text-neutral-500">
          <span>Chats</span>
          <span>{dialogs.length}</span>
        </div>
        <div className="flex-1 space-y-2 overflow-y-auto pr-1">
          {dialogs.length === 0 && (
            <p className="text-sm text-neutral-500">No dialogs yet.</p>
          )}
          {dialogs.map((dialog) => (
            <button
              key={dialog.id}
              onClick={() => setActiveDialogId(dialog.id)}
              className={`w-full rounded-lg px-3 py-2 text-left text-sm transition ${
                dialog.id === activeDialogId
                  ? 'bg-neutral-800 text-white'
                  : 'bg-neutral-900 text-neutral-300 hover:bg-neutral-800'
              }`}
            >
              <div className="truncate font-medium">{dialog.title}</div>
              <div className="truncate text-xs text-neutral-500">
                {dialog.threadId}
              </div>
            </button>
          ))}
        </div>
      </aside>

      <main className="flex flex-1 flex-col">
        <header className="border-b border-neutral-800 bg-neutral-950/70 px-6 py-4 backdrop-blur">
          <div className="flex items-center justify-between">
            <div>
              <h1 className="text-lg font-semibold">
                {activeDialog ? activeDialog.title : 'Select a chat'}
              </h1>
              {activeDialog && (
                <p className="text-xs text-neutral-500">
                  Thread: {activeDialog.threadId}
                </p>
              )}
            </div>
            <div className="rounded-full border border-neutral-800 px-3 py-1 text-xs text-neutral-400">
              Demo Chat
            </div>
          </div>
        </header>

        <div ref={messagesRef} className="flex-1 overflow-y-auto">
          {activeDialogId === null && (
            <div className="mx-auto mt-16 max-w-3xl px-6 text-center text-sm text-neutral-500">
              Create a new chat to start messaging.
            </div>
          )}
          {activeDialogId !== null && messages.length === 0 && messagesV2.length === 0 && (
            <div className="mx-auto mt-16 max-w-3xl px-6 text-center text-sm text-neutral-500">
              No messages yet.
            </div>
          )}
          {(useV2Api ? messagesV2 : []).map((messageWithVersions) => {
            const isUser = messageWithVersions.role === 'user'
            const isSystem = messageWithVersions.role === 'system'
            const avatarLabel = isUser ? 'U' : isSystem ? 'S' : 'AI'
            const avatarClass = isUser
              ? 'bg-emerald-500 text-black'
              : isSystem
                ? 'bg-amber-500 text-black'
                : 'bg-indigo-500 text-black'
            const rowClass = isUser ? 'bg-neutral-900' : 'bg-neutral-950'
            const hasVersions = messageWithVersions.totalVersions > 1
            const canEdit = isUser && !loading

            return (
              <div
                key={messageWithVersions.messageId}
                className={`group relative border-b border-neutral-800/70 py-6 ${rowClass}`}
              >
                <div className="mx-auto flex w-full max-w-3xl gap-4 px-6">
                  <div
                    className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-full text-xs font-semibold ${avatarClass}`}
                  >
                    {avatarLabel}
                  </div>
                  <div className="flex-1">
                    <div className="mb-1 flex items-center justify-between">
                      <div className="text-xs uppercase tracking-wide text-neutral-500">
                        {isUser ? 'You' : isSystem ? 'System' : 'Assistant'}
                      </div>
                      {canEdit && (
                        <button
                          onClick={() => {
                            setEditingMessageId(messageWithVersions.activeVersionId)
                            setEditContent(messageWithVersions.content)
                          }}
                          className="opacity-0 group-hover:opacity-100 text-xs text-neutral-400 hover:text-neutral-200 transition px-2 py-1 rounded hover:bg-neutral-800"
                        >
                          Edit
                        </button>
                      )}
                    </div>
                    
                    {editingMessageId === messageWithVersions.activeVersionId ? (
                      <div className="space-y-2">
                        <textarea
                          value={editContent}
                          onChange={(e) => setEditContent(e.target.value)}
                          className="w-full rounded border border-neutral-700 bg-neutral-800 px-3 py-2 text-sm text-neutral-100 focus:outline-none focus:ring-2 focus:ring-emerald-500"
                          rows={3}
                        />
                        <div className="flex gap-2">
                          <button
                            onClick={() => handleEditMessage(messageWithVersions.activeVersionId, editContent)}
                            disabled={loading}
                            className="rounded bg-emerald-600 px-3 py-1 text-xs text-white hover:bg-emerald-700 disabled:opacity-50 transition"
                          >
                            Save
                          </button>
                          <button
                            onClick={() => {
                              setEditingMessageId(null)
                              setEditContent('')
                            }}
                            className="rounded bg-neutral-700 px-3 py-1 text-xs text-neutral-300 hover:bg-neutral-600 transition"
                          >
                            Cancel
                          </button>
                        </div>
                      </div>
                    ) : (
                      <>
                        <div className="whitespace-pre-wrap text-sm leading-relaxed text-neutral-100">
                          {messageWithVersions.content}
                        </div>
                        {hasVersions && (
                          <div className="mt-3 flex items-center gap-1.5 text-xs">
                            <button
                              onClick={() => {
                                const currentIndex = messageWithVersions.currentVersionIndex
                                if (currentIndex > 0) {
                                  const prevVersion = messageWithVersions.versions[currentIndex - 1]
                                  handleSwitchVersion(prevVersion.id)
                                }
                              }}
                              disabled={messageWithVersions.currentVersionIndex === 0 || loading}
                              className="flex items-center justify-center w-6 h-6 rounded border border-neutral-700 bg-neutral-800 hover:bg-neutral-700 disabled:opacity-30 disabled:cursor-not-allowed transition text-neutral-400 hover:text-neutral-200"
                              title="Previous version"
                            >
                              ◄
                            </button>
                            <span className="px-2 py-1 rounded border border-neutral-700 bg-neutral-800 text-neutral-300 font-medium min-w-[3rem] text-center">
                              {messageWithVersions.currentVersionIndex + 1}/{messageWithVersions.totalVersions}
                            </span>
                            <button
                              onClick={() => {
                                const currentIndex = messageWithVersions.currentVersionIndex
                                if (currentIndex < messageWithVersions.versions.length - 1) {
                                  const nextVersion = messageWithVersions.versions[currentIndex + 1]
                                  handleSwitchVersion(nextVersion.id)
                                }
                              }}
                              disabled={
                                messageWithVersions.currentVersionIndex >=
                                  messageWithVersions.totalVersions - 1 || loading
                              }
                              className="flex items-center justify-center w-6 h-6 rounded border border-neutral-700 bg-neutral-800 hover:bg-neutral-700 disabled:opacity-30 disabled:cursor-not-allowed transition text-neutral-400 hover:text-neutral-200"
                              title="Next version"
                            >
                              ►
                            </button>
                          </div>
                        )}
                      </>
                    )}
                  </div>
                </div>
              </div>
            )
          })}
          {(!useV2Api ? messages : []).map((message) => {
            const isUser = message.role === 'user'
            const isSystem = message.role === 'system'
            const avatarLabel = isUser ? 'U' : isSystem ? 'S' : 'AI'
            const avatarClass = isUser
              ? 'bg-emerald-500 text-black'
              : isSystem
                ? 'bg-amber-500 text-black'
                : 'bg-indigo-500 text-black'
            const rowClass = isUser ? 'bg-neutral-900' : 'bg-neutral-950'

            return (
              <div
                key={message.id}
                className={`border-b border-neutral-800/70 py-6 ${rowClass}`}
              >
                <div className="mx-auto flex w-full max-w-3xl gap-4 px-6">
                  <div
                    className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-full text-xs font-semibold ${avatarClass}`}
                  >
                    {avatarLabel}
                  </div>
                  <div className="flex-1">
                    <div className="mb-1 text-xs uppercase tracking-wide text-neutral-500">
                      {isUser ? 'You' : isSystem ? 'System' : 'Assistant'}
                    </div>
                    <div className="whitespace-pre-wrap text-sm leading-relaxed text-neutral-100">
                      {message.content}
                    </div>
                  </div>
                </div>
              </div>
            )
          })}
          {pendingResponse && activeDialogId !== null && (
            <div className="border-b border-neutral-800/70 bg-neutral-950 py-6">
              <div className="mx-auto flex w-full max-w-3xl gap-4 px-6">
                <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-indigo-500 text-xs font-semibold text-black">
                  AI
                </div>
                <div className="flex-1">
                  <div className="mb-1 text-xs uppercase tracking-wide text-neutral-500">
                    Assistant
                  </div>
                  <div className="flex items-center gap-2 text-sm text-neutral-300">
                    <span className="h-2 w-2 animate-bounce rounded-full bg-neutral-400" />
                    <span className="h-2 w-2 animate-bounce rounded-full bg-neutral-400 [animation-delay:150ms]" />
                    <span className="h-2 w-2 animate-bounce rounded-full bg-neutral-400 [animation-delay:300ms]" />
                    <span className="ml-2 text-neutral-500">Thinking...</span>
                  </div>
                </div>
              </div>
            </div>
          )}
        </div>

        <footer className="border-t border-neutral-800 bg-neutral-950/80 backdrop-blur">
          <div className="mx-auto w-full max-w-3xl px-4 py-4">
            <div className="flex items-end gap-3 rounded-2xl border border-neutral-800 bg-neutral-900 px-4 py-3 shadow-sm">
              <input
                type="text"
                value={input}
                onChange={(event) => setInput(event.target.value)}
                onKeyDown={(event) => {
                  if (event.key === 'Enter') {
                    handleSendMessage()
                  }
                }}
                disabled={!activeDialog || loading || pendingResponse}
                placeholder="Send a message..."
                className="flex-1 bg-transparent text-sm text-neutral-100 placeholder:text-neutral-500 focus:outline-none"
              />
              <button
                onClick={handleSendMessage}
                disabled={!activeDialog || loading || !input.trim()}
                className="rounded-xl bg-emerald-500 px-4 py-2 text-sm font-semibold text-black transition hover:bg-emerald-400 disabled:opacity-50"
              >
                Send
              </button>
            </div>
            <p className="mt-2 text-center text-xs text-neutral-500">
              Responses can be delayed while the workflow resumes.
            </p>
          </div>
        </footer>
      </main>
    </div>
  )
}

export default App
