import { useEffect, useState } from 'react'
import { apiBase, fetchJson } from './api/client'
import { useChatConnection } from './hooks/useChatConnection'
import { Sidebar } from './components/layout/Sidebar'
import { ChatHeader } from './components/chat/ChatHeader'
import { MessageList } from './components/chat/MessageList'
import { ChatInput } from './components/chat/ChatInput'
import type { Dialog, MessageWithVersions } from './types'

function App() {
  const [dialogs, setDialogs] = useState<Dialog[]>([])
  const [activeDialogId, setActiveDialogId] = useState<string | null>(null)
  const [messages, setMessages] = useState<MessageWithVersions[]>([])
  const [input, setInput] = useState('')
  const [loading, setLoading] = useState(false)
  const [pendingResponse, setPendingResponse] = useState(false)
  const [editingMessageId, setEditingMessageId] = useState<string | null>(null)
  const [editContent, setEditContent] = useState('')
  const [showScenarioModal, setShowScenarioModal] = useState(false)
  const [newDialogTitle, setNewDialogTitle] = useState('')
  const [dialogToDelete, setDialogToDelete] = useState<Dialog | null>(null)
  const [streamingContent, setStreamingContent] = useState('')

  useChatConnection({
    activeDialogId,
    setDialogs,
    setMessages,
    setPendingResponse,
    setLoading,
    setStreamingContent,
  })

  useEffect(() => {
    fetchJson<Dialog[]>(`${apiBase}/api/v1/dialogs`)
      .then(setDialogs)
      .catch(() => setDialogs([]))
  }, [])

  useEffect(() => {
    if (!activeDialogId) {
      setMessages([])
      setPendingResponse(false)
      setStreamingContent('')
      return
    }
    fetchJson<MessageWithVersions[]>(
      `${apiBase}/api/v1/dialogs/${activeDialogId}/messages`,
    )
      .then(setMessages)
      .catch(() => setMessages([]))
  }, [activeDialogId])

  useEffect(() => {
    setPendingResponse(false)
    setStreamingContent('')
  }, [activeDialogId])

  const activeDialog = dialogs.find((d) => d.id === activeDialogId) ?? null

  const handleOpenNewChatModal = () => {
    setNewDialogTitle('')
    setShowScenarioModal(true)
  }

  const handleCreateDialog = async (workflowId: string, title?: string) => {
    setShowScenarioModal(false)
    setLoading(true)
    try {
      const dialog = await fetchJson<Dialog>(`${apiBase}/api/v1/dialogs`, {
        method: 'POST',
        body: JSON.stringify({
          title: title?.trim() || undefined,
          workflowId,
        }),
      })
      setDialogs((prev) => [dialog, ...prev])
      setActiveDialogId(dialog.id)
      const list = await fetchJson<MessageWithVersions[]>(
        `${apiBase}/api/v1/dialogs/${dialog.id}/messages`,
      )
      setMessages(list)
    } catch (error) {
      console.error(error)
    } finally {
      setLoading(false)
    }
  }

  const handleConfirmDeleteDialog = async () => {
    if (!dialogToDelete) return
    const idToDelete = dialogToDelete.id
    setDialogToDelete(null)
    setLoading(true)
    try {
      const res = await fetch(`${apiBase}/api/v1/dialogs/${idToDelete}`, {
        method: 'DELETE',
      })
      if (!res.ok) throw new Error(await res.text() || 'Delete failed')
      setDialogs((prev) => prev.filter((d) => d.id !== idToDelete))
      if (activeDialogId === idToDelete) {
        setActiveDialogId(null)
        setMessages([])
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
      const newMessages = await fetchJson<MessageWithVersions[]>(
        `${apiBase}/api/v1/dialogs/${activeDialog.id}/messages/edit`,
        {
          method: 'POST',
          body: JSON.stringify({ versionId, content: newContent }),
        },
      )
      setMessages(newMessages)
      setPendingResponse(true)
      setStreamingContent('')
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
      await fetchJson(`${apiBase}/api/v1/dialogs/${activeDialog.id}/messages/switch-version`, {
        method: 'POST',
        body: JSON.stringify({ versionId }),
      })
      // Messages will be updated via SignalR 'messagesUpdated' event
    } catch (error) {
      console.error(error)
    } finally {
      setLoading(false)
    }
  }

  const handleSendMessage = async () => {
    if (!activeDialog || !input.trim() || !activeDialog.lastCheckpointId) return
    const content = input.trim()
    setInput('')
    setLoading(true)
    setPendingResponse(true)
    setStreamingContent('')
    try {
      const newMessage = await fetchJson<MessageWithVersions>(
        `${apiBase}/api/v1/dialogs/${activeDialog.id}/messages`,
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
      setMessages((prev) => [...prev, newMessage])
    } catch (error) {
      console.error(error)
      setPendingResponse(false)
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="flex h-screen bg-neutral-900 text-neutral-100">
      <Sidebar
        dialogs={dialogs}
        activeDialogId={activeDialogId}
        loading={loading}
        showNewChatModal={showScenarioModal}
        newDialogTitle={newDialogTitle}
        dialogToDelete={dialogToDelete}
        onNewChatClick={handleOpenNewChatModal}
        onNewChatTitleChange={setNewDialogTitle}
        onCreateDialog={handleCreateDialog}
        onCancelNewChat={() => setShowScenarioModal(false)}
        onSelectDialog={setActiveDialogId}
        onDeleteDialogClick={setDialogToDelete}
        onConfirmDeleteDialog={handleConfirmDeleteDialog}
        onCancelDeleteDialog={() => setDialogToDelete(null)}
      />

      <main className="flex flex-1 flex-col">
        <ChatHeader activeDialog={activeDialog} />

        {activeDialogId === null ? (
          <div className="mx-auto mt-16 max-w-3xl flex-1 px-6 text-center text-sm text-neutral-500">
            Create a new chat to start messaging.
          </div>
        ) : (
          <>
            <MessageList
              messages={messages}
              pendingResponse={pendingResponse}
              streamingContent={streamingContent}
              editingMessageId={editingMessageId}
              editContent={editContent}
              onEditContentChange={setEditContent}
              onStartEdit={(messageId, content) => {
                setEditingMessageId(messageId)
                setEditContent(content)
              }}
              onSaveEdit={handleEditMessage}
              onCancelEdit={() => {
                setEditingMessageId(null)
                setEditContent('')
              }}
              onSwitchVersion={handleSwitchVersion}
              loading={loading}
            />
            <footer className="border-t border-neutral-800 bg-neutral-950/80 backdrop-blur">
              <ChatInput
                value={input}
                onChange={setInput}
                onSend={handleSendMessage}
                disabled={!activeDialog || loading || pendingResponse}
                sendDisabled={!activeDialog || loading || !input.trim()}
              />
            </footer>
          </>
        )}
      </main>
    </div>
  )
}

export default App
