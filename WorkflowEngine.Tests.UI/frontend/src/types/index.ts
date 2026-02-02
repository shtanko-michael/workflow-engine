export type Dialog = {
  id: string
  title: string
  threadId: string
  workflowType: string
  lastCheckpointId?: string | null
  lastInterruptRequestId?: string | null
  createdAt: string
  updatedAt: string
}

export type MessageVersion = {
  id: string
  messageId: string
  content: string
  checkpointId: string
  createdAt: string
}

export type MessageWithVersions = {
  messageId: string
  role: 'user' | 'assistant' | 'system'
  activeVersionId: string
  content: string
  currentVersionIndex: number
  totalVersions: number
  versions: MessageVersion[]
  createdAt: string
}
