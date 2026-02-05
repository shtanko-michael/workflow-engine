import { useCallback, useEffect, useMemo, useRef } from 'react'
import { HubConnectionBuilder, type HubConnection } from '@microsoft/signalr'
import { apiBase } from '../api/client'
import type { Dialog, MessageWithVersions } from '../types'

/** Minimal placeholder for an assistant message created when first chunk arrives (streaming by messageId). */
function streamingMessagePlaceholder(messageId: string, content: string): MessageWithVersions {
  return {
    messageId,
    role: 'assistant',
    activeVersionId: messageId,
    content,
    currentVersionIndex: 0,
    totalVersions: 1,
    versions: [],
    createdAt: new Date().toISOString(),
  }
}

type UseChatConnectionArgs = {
  activeDialogId: string | null
  setDialogs: React.Dispatch<React.SetStateAction<Dialog[]>>
  setMessages: React.Dispatch<React.SetStateAction<MessageWithVersions[]>>
  setPendingResponse: React.Dispatch<React.SetStateAction<boolean>>
  setLoading: React.Dispatch<React.SetStateAction<boolean>>
  setStreamingContent: React.Dispatch<React.SetStateAction<string>>
  setStreamingMessageId: React.Dispatch<React.SetStateAction<string | null>>
}

export type UseChatConnectionResult = {
  connection: HubConnection
  /** Call before setting activeDialogId to a newly created dialog so streaming chunks are received. */
  joinDialogNow: (dialogId: string) => Promise<void>
}

export function useChatConnection({
  activeDialogId,
  setDialogs,
  setMessages,
  setPendingResponse,
  setLoading,
  setStreamingContent,
  setStreamingMessageId,
}: UseChatConnectionArgs): UseChatConnectionResult {
  const connection = useMemo<HubConnection>(() => {
    return new HubConnectionBuilder()
      .withUrl(`${apiBase}/hubs/chat`)
      .withAutomaticReconnect([0, 1000, 5000, 10000])
      .build()
  }, [])

  const isStartedRef = useRef(false)

  useEffect(() => {
    const startConnection = async () => {
      try {
        if (!isStartedRef.current && connection.state === 'Disconnected') {
          await connection.start()
          isStartedRef.current = true
        }
      } catch (error) {
        console.error('Failed to start the connection', error)
      }
    }
    void startConnection()
    return () => {
      // Only stop on unmount, not on every cleanup (React StrictMode)
      // The connection will be reused across re-renders
    }
  }, [connection])

  useEffect(() => {
    if (!activeDialogId) return
    
    const joinDialog = async () => {
      // Wait for connection to be ready
      let attempts = 0
      while (connection.state !== 'Connected' && attempts < 50) {
        await new Promise((resolve) => setTimeout(resolve, 100))
        attempts++
      }
      
      if (connection.state === 'Connected') {
        await connection.invoke('JoinDialog', activeDialogId).catch(console.error)
      }
    }
    
    void joinDialog()
    
    return () => {
      if (connection.state === 'Connected') {
        void connection.invoke('LeaveDialog', activeDialogId).catch(() => undefined)
      }
    }
  }, [connection, activeDialogId])

  useEffect(() => {
    connection.on('dialogUpdated', (payload: Dialog) => {
      if (!payload) return
      setDialogs((prev) =>
        prev.map((dialog) => (dialog.id === payload.id ? payload : dialog)),
      )
    })

    // Backend sends assistantChunk(dialogId, messageId, chunk) when using gateway; or legacy assistantChunk(dialogId, chunk)
    connection.on('assistantChunk', (dialogId: string, messageIdOrChunk: string, chunkOrUndefined?: string) => {
      if (dialogId !== activeDialogId) return
      const hasMessageId = chunkOrUndefined !== undefined
      const messageId = hasMessageId ? messageIdOrChunk : null
      const chunk = hasMessageId ? (chunkOrUndefined ?? '') : (messageIdOrChunk ?? '')

      if (messageId != null) {
        setStreamingMessageId(messageId)
        setMessages((prev) => {
          const existing = prev.find((m) => m.messageId === messageId)
          if (existing) {
            return prev.map((m) =>
              m.messageId === messageId ? { ...m, content: m.content + chunk } : m,
            )
          }
          return [...prev, streamingMessagePlaceholder(messageId, chunk)]
        })
      } else {
        setStreamingContent((prev) => prev + chunk)
      }
    })

    connection.on('messagesUpdated', (payload: MessageWithVersions[]) => {
      if (!payload || payload.length === 0) return
      setMessages(payload)
      setPendingResponse(false)
      setLoading(false)
      setStreamingContent('')
      setStreamingMessageId(null)
    })

    return () => {
      connection.off('dialogUpdated')
      connection.off('assistantChunk')
      connection.off('messagesUpdated')
    }
  }, [
    connection,
    activeDialogId,
    setDialogs,
    setMessages,
    setPendingResponse,
    setLoading,
    setStreamingContent,
    setStreamingMessageId,
  ])

  const joinDialogNow = useCallback(
    async (dialogId: string) => {
      let attempts = 0
      while (connection.state !== 'Connected' && attempts < 50) {
        await new Promise((resolve) => setTimeout(resolve, 100))
        attempts++
      }
      if (connection.state === 'Connected') {
        await connection.invoke('JoinDialog', dialogId).catch(console.error)
      }
    },
    [connection],
  )

  return { connection, joinDialogNow }
}
