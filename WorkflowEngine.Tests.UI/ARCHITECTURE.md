# WorkflowEngine.Tests.UI — Architecture & Client–Server Flow

Detailed analysis of how the chat UI interacts with the backend: dialogs, messages, versions, active branch, and real-time updates.

---

## 1. Data Model Overview

### 1.1 Conversation (Dialog)

| Field | Purpose |
|-------|--------|
| `Id` | Unique dialog id (used in API and SignalR group) |
| `ThreadId` | Workflow execution thread (checkpoints are keyed by thread_id) |
| `Title` | Display name |
| `WorkflowType` | `demo_chat` or `ai_chat` |
| **`ActiveLeafMessageId`** | **Id of the message that is the current “end” of the visible branch** |
| `LastCheckpointId` | Last workflow checkpoint id (required to send next message) |
| `LastInterruptRequestId` | Id of the last interrupt (AskHuman) for resuming |

**Active branch** = the single path from root to the message whose id is `ActiveLeafMessageId`.  
All “current” messages shown in the UI are exactly this branch.

### 1.2 Messages (Tree, Not Flat List)

Messages form a **tree**:

- **Root**: no `ParentId` (in practice the first message is the workflow’s first reply, e.g. “Hello! I'm an AI assistant…”).
- **Child**: `ParentId` = id of the previous message in the chain.
- **Siblings**: same `ParentId`; different “versions” of the same user turn (after edit) or different branches.

Additional fields:

| Field | Purpose |
|-------|--------|
| `ParentId` | Previous message in the chain (null for root) |
| `Role` | `user` \| `assistant` \| `system` |
| `Content` | Text |
| `CheckpointId` | Checkpoint after which this message was produced (for workflow resume) |
| `CheckpointNs` | Checkpoint namespace (branch identifier for persistence) |
| `RequestId` | Interrupt request id when created from AskHuman |

**Branch** = path from root to a **leaf** (a message that has no children in the tree).  
**Active branch** = branch ending at `Conversation.ActiveLeafMessageId`.

---

## 2. How the Active Branch Is Determined

- **Stored:** `Conversation.ActiveLeafMessageId` is the id of the message that is the **leaf** of the active branch.
- **Resolved:**  
  `GetMessagesAsync(conversationId)` → load conversation → take `ActiveLeafMessageId` → `GetBranchToLeafAsync(conversationId, ActiveLeafMessageId)`.
- **GetBranchToLeafAsync** (MessageRepository):
  1. Start from `leafMessageId`.
  2. Walk backwards via `ParentId` until `ParentId == null`.
  3. Collect messages in reverse order, then reverse the list → ordered from root to leaf.

So: **active branch = path from root to the message whose id is `ActiveLeafMessageId`.**  
Whoever sets `ActiveLeafMessageId` (create dialog, send message, edit message, switch version) defines what the UI “current thread” is.

---

## 3. API & SignalR Overview

| Transport | Usage |
|----------|--------|
| **REST** | Create dialog, get dialogs, get messages, send message, edit message, switch version, delete dialog |
| **SignalR (ChatHub)** | Join/leave dialog group; receive `dialogUpdated`, `messagesUpdated`, `assistantChunk` |

**ChatHub:**

- `JoinDialog(dialogId)` — add connection to group `dialogId`.
- `LeaveDialog(dialogId)` — remove from group `dialogId`.

Server sends to **group(dialogId)** so only clients that joined that dialog get updates.

---

## 4. Create Dialog — Step by Step

### 4.1 Client

1. User clicks “New chat”, chooses scenario (e.g. AI Chat), optionally enters title.
2. `POST /api/v1/dialogs` with `{ title?, workflowId }`.
3. Response: `Dialog` (id, title, threadId, workflowType, lastCheckpointId, lastInterruptRequestId, …).
4. Client pushes the new dialog into the list and sets it as active (`setActiveDialogId(dialog.id)`).
5. Client then `GET /api/v1/dialogs/{id}/messages` and sets messages in state (see “Fetch messages” below).

### 4.2 Server (CreateDialogAsync)

1. Create **ConversationEntity**: new `Id`, new `ThreadId`, `Title`, `WorkflowType`. No `ActiveLeafMessageId` yet.
2. Save conversation.
3. **Run workflow once** (no human message):  
   `RunWorkflowAsync(threadId, workflowType, resumeMessage: null, checkpointId: null, checkpointNs: null)`  
   → workflow runs from start (e.g. “start” node → first AI reply → AskHuman).  
   State contains first AI message and `LastCheckpointId`, `InterruptRequestId`.
4. **SaveMessagesFromStateAsync**: persist the **last** message from state (the first AI reply) with `parentMessageId: null`, set `checkpointId` / `checkpointNs` / `InterruptRequestId`.
5. Set `conversation.ActiveLeafMessageId = lastSaved.Id` (this message is the first and only in the branch, so it’s the leaf).
6. Set `conversation.LastCheckpointId`, `LastInterruptRequestId` from state.
7. Update `conversation` (e.g. `UpdatedAt`), save.
8. **Broadcast** `dialogUpdated` and `messagesUpdated` via SignalR (group = conversation.Id).

So after create: **active branch = single message (first AI reply)**.  
Client either uses the GET response or the first `messagesUpdated` to show that branch.

---

## 5. Send Message — Step by Step

### 5.1 Client

1. User types and sends.
2. Client checks `activeDialog.lastCheckpointId` (required).
3. `POST /api/v1/dialogs/{dialogId}/messages` with `{ content, threadId, checkpointId, requestId? }`.
4. Response: one **MessageWithVersionsDto** (the new user message).
5. Client appends it to local messages and sets `pendingResponse = true`, `streamingContent = ''`.
6. Client shows “Thinking…” and then accumulates `assistantChunk` from SignalR until `messagesUpdated` arrives.

### 5.2 Server (SendMessageAsync)

1. Load conversation; validate `conversation.LastCheckpointId == request.CheckpointId` (“checkpoint mismatch” if not).
2. Get current branch: `GetBranchToLeafAsync(conversationId, conversation.ActiveLeafMessageId)`; take **last** message in branch (current leaf).
3. Resolve `checkpointNs` from that message (e.g. reuse or derive).
4. **CreateMessageAsync**: new message with `ParentId = lastMessage.Id`, `Role = "user"`, `Content`, `CheckpointId`, `RequestId`, `CheckpointNs`.  
   This extends the tree: new user message is the new leaf (but we don’t set active leaf yet in DB for conversation).
5. Build `HumanMessage` for workflow.
6. **Fire-and-forget** `Task.Run` → `ProcessWorkflowAsync(conversationId, humanMessage, checkpointId, checkpointNs, userMessage.Id)`.
7. Return the new user message (controller maps to `MessageWithVersionsDto`).

So the HTTP response returns **immediately** with the new user message. The UI appends it and shows “Thinking…”. The rest happens in the background.

### 5.3 Server (ProcessWorkflowAsync — background)

1. Load conversation again.
2. **RunWorkflowAsync(threadId, workflowType, humanMessage, checkpointId, checkpointNs, conversationId)**  
   - Restores workflow state from checkpoint (thread_id + checkpoint_id + checkpoint_ns).  
   - Injects `humanMessage` into state and runs from the node after AskHuman (e.g. “handleInput”).  
   - For AI Chat: calls LLM (streaming if `conversationId` passed), sends **assistantChunk** via SignalR to group(conversationId).  
   - Stops at next AskHuman; new checkpoint is saved.
3. **SaveMessagesFromStateAsync**: persist the **last** message from state (the new AI reply) with `parentMessageId = userMessageId`, same checkpoint/ns.
4. Set `conversation.ActiveLeafMessageId = lastSaved.Id` (new AI message is the new leaf).
5. Update `conversation.LastCheckpointId`, `LastInterruptRequestId`, save.
6. **Broadcast** `dialogUpdated` and **messagesUpdated** (full active branch) to group(conversationId).

So: **active branch** is updated to “old branch + user message + new AI message”.  
Client receives `assistantChunk` during run, then `messagesUpdated` with the full new branch and clears “Thinking…” and streaming.

---

## 6. Edit Message (New Version) — Step by Step

Editing creates a **new branch**: a new user message (sibling of the edited one) with new content; everything that was **below** the edited message is no longer on the active branch.

### 6.1 Client

1. User edits a user message and clicks Save.
2. `POST /api/v1/dialogs/{dialogId}/messages/edit` with `{ versionId, content }`  
   (`versionId` = id of the message being edited).
3. Response: **list** of **MessageWithVersionsDto** = new active branch (from root to the new edited message only; messages below are cut off).
4. Client sets `messages = newMessages`, `pendingResponse = true`, `streamingContent = ''`, closes edit mode.
5. UI shows the new branch (edited message at the end), then “Thinking…” and streaming; when `messagesUpdated` arrives, UI shows full new branch including the new AI reply.

### 6.2 Server (EditMessageAsync)

1. Load message by `messageId` (versionId); ensure it’s a user message.
2. Load conversation.
3. **CreateSiblingAsync(editedMessageId, newContent)**:
   - New message with **same ParentId** as the edited message, same Role, new Content, new Id, new `CheckpointNs = newMessage.Id`.
   - So: two siblings under the same parent (old version and new “edited” version).
4. **UpdateActiveLeafAsync(conversationId, newMessage.Id)** → in DB, conversation’s active leaf is now this new message (and repo’s conversation entity is updated).
5. Set `conversation.ActiveLeafMessageId = newMessage.Id` and save conversation.
6. Resolve parent’s `CheckpointId` / `CheckpointNs`; **EnsureCheckpointNamespaceSeedAsync** so the new branch has a checkpoint namespace seeded from parent’s checkpoint.
7. **GetBranchWithAlternativesAsync(conversationId)** → build current branch (root → new leaf) with alternatives per step; map to DTOs.
8. **Fire-and-forget** `ProcessWorkflowAfterEditAsync(conversationId, newMessage.Id, newContent, newMessage.RequestId, parentCheckpointId, branchCheckpointNs)`.
9. Return the DTO list (new branch without any AI reply below the edited message).

So the HTTP response is **immediate**: new branch = “everything above + edited message”, no new AI message yet.  
Client shows that and “Thinking…”; workflow runs in background with streaming, then `messagesUpdated` delivers the full new branch including the new AI reply.

### 6.3 Server (ProcessWorkflowAfterEditAsync — background)

1. Load conversation.
2. Build `HumanMessage` from `newMessageId`, `newContent`, `requestId`.
3. **RunWorkflowAsync(threadId, workflowType, humanMessage, parentCheckpointId, branchCheckpointNs, conversationId)**  
   - Restores from parent checkpoint/ns; injects the “edited” human message; runs handleInput etc.; streams **assistantChunk**.
4. **SaveMessagesFromStateAsync**: save new AI message(s) with parent = `newMessageId`.
5. Set `conversation.ActiveLeafMessageId = lastSaved.Id`, update `LastCheckpointId`, `LastInterruptRequestId`, save.
6. **Broadcast** `dialogUpdated` and **messagesUpdated**.

So after edit: **active branch** = “branch up to and including edited message” + “new AI reply”.  
Old branch (old version of the message + all messages below) still exists in DB but is no longer the active branch.

---

## 7. Fetch Dialog / Messages — Step by Step

### 7.1 Get list of dialogs

- **Client:** `GET /api/v1/dialogs` (e.g. on load).
- **Server:** `GetDialogsAsync()` → all conversations; map to `DialogDto[]`.
- Used to render the sidebar.

### 7.2 Get messages of one dialog (active branch)

- **Client:** When user selects a dialog (`setActiveDialogId`), client:
  1. Calls `JoinDialog(activeDialogId)` (SignalR).
  2. Calls `GET /api/v1/dialogs/{dialogId}/messages`.
- **Server (GetMessages in controller):**
  1. `GetMessagesAsync(dialogId)` → `GetBranchToLeafAsync(conversationId, conversation.ActiveLeafMessageId)` → list of `MessageEntity` from root to leaf.
  2. For each position in the branch: get **alternatives** = `GetChildrenAsync(previousMessage.Id)` (siblings of the current message in the branch).
  3. Build `MessageWithAlternatives` (active message + list of alternatives + currentIndex/totalVersions).
  4. Map to `MessageWithVersionsDto[]` and return.

So “fetch messages” = “fetch **active branch** with version info at each step”.  
Client stores this and renders the thread; “switch version” uses the same branch logic but changes which leaf is active (see below).

---

## 8. Switch Version — Step by Step

User chooses another “version” of a message (another sibling in the tree). That defines a **different branch** (and possibly a different leaf). The server sets the active branch to that branch.

### 8.1 Client

1. User clicks prev/next version (e.g. ◄ / ►) for a message.
2. `POST /api/v1/dialogs/{dialogId}/messages/switch-version` with `{ versionId }`  
   (`versionId` = id of the message to switch to).
3. No response body needed; client relies on **SignalR** `messagesUpdated` to get the new branch and refresh the list.

### 8.2 Server (SwitchVersionAsync)

1. Load message by `messageId` (versionId).
2. Load conversation.
3. **GetLeafOfBranchContainingAsync(conversationId, messageId)**  
   From this message, walk down the tree (following any child) to the **leaf** of that branch. So we get the leaf of the branch that contains the chosen version.
4. **UpdateActiveLeafAsync(conversationId, leaf.Id)** → conversation’s active leaf is now this leaf (different branch).
5. Optionally update `conversation.LastCheckpointId` from the last message in that branch; save conversation.
6. **Broadcast** `dialogUpdated` and **messagesUpdated** so the client receives the new active branch (and dialog metadata).

So: **active branch** is redefined to the branch that contains the selected message and ends at its leaf.  
Client only needs to refresh from `messagesUpdated`; no need to refetch via REST.

---

## 9. SignalR Events — When and What

| Event | Sender | When | Payload |
|-------|--------|------|--------|
| **dialogUpdated** | Server | After create/update of conversation (create dialog, after workflow run, after edit run, after switch version) | Full `Dialog` DTO (id, title, threadId, lastCheckpointId, activeLeafMessageId implied by server state, …) |
| **messagesUpdated** | Server | After workflow completes (send message, edit message) or after switch version | Full active branch as `MessageWithVersionsDto[]` |
| **assistantChunk** | Server | During AI workflow run (streaming LLM); only for AI Chat with streaming | `(dialogId, chunk)` — client appends to `streamingContent` for current dialog |

Client subscribes only after **JoinDialog(dialogId)**; server sends to **Clients.Group(dialogId)**. So only the tab/client that joined that dialog receives these events.

---

## 10. High-Level Flow Diagrams

### 10.1 Message tree and active branch

```
Conversation
  ActiveLeafMessageId = "msg-A3"

  [Root]  M0 (assistant, "Hello...")
     |
     +-- M1 (user, "Hi")
     |      |
     |      +-- M2 (assistant, "Hi there!")
     |      |      |
     |      |      +-- M3 (user, "Bye")
     |      |      |      |
     |      |      |      +-- M4 (assistant, "Goodbye!")
     |      |      |
     |      +-- M1' (user, "Hello")   ← edit created sibling
     |             |
     |             +-- A1 (assistant, "...")
     |             |
     |             +-- A2 (assistant, "...")  ← leaf of another branch
     |
     ...

  Active branch = path from root to ActiveLeafMessageId.
  E.g. if ActiveLeafMessageId = M4 → branch = [M0, M1, M2, M3, M4].
  If user edits M1 to "Hello" → new message M1'; ActiveLeafMessageId = A1 or A2 (after workflow).
  Old branch M0→M1→M2→M3→M4 still in DB but not active.
```

### 10.2 Create dialog (simplified)

```
Client                          Server
  |                               |
  |  POST /dialogs                 |
  |  { workflowId, title? }       |
  |------------------------------>|
  |                               |  Create conversation
  |                               |  Run workflow (start → AskHuman)
  |                               |  Save 1 message (first AI)
  |                               |  Set ActiveLeafMessageId = that message
  |                               |  Broadcast dialogUpdated, messagesUpdated
  |<------------------------------|
  |  200 { dialog }                |
  |                               |
  |  GET /dialogs/{id}/messages    |
  |------------------------------>|
  |<------------------------------|
  |  200 [ one message ]           |
  |                               |
  |  (optional: messagesUpdated   |
  |   already received via SignalR)
```

### 10.3 Send message (simplified)

```
Client                          Server
  |                               |
  |  POST /dialogs/{id}/messages   |
  |  { content, checkpointId }    |
  |------------------------------>|
  |                               |  Create user message (child of current leaf)
  |                               |  Start ProcessWorkflowAsync (background)
  |<------------------------------|
  |  200 { new user message }     |
  |                               |
  |  setMessages([...prev, new])   |
  |  setPendingResponse(true)     |
  |  show "Thinking..."           |
  |                               |  ... workflow runs ...
  |<------- assistantChunk -------|  (streaming)
  |  append to streamingContent   |
  |                               |  ... workflow done ...
  |                               |  Save AI message, set ActiveLeafMessageId
  |<------- messagesUpdated ------|
  |  setMessages(full branch)     |
  |  setPendingResponse(false)    |
```

### 10.4 Edit message (simplified)

```
Client                          Server
  |                               |
  |  POST /dialogs/{id}/messages/edit
  |  { versionId, content }       |
  |------------------------------>|
  |                               |  CreateSibling(versionId, content)
  |                               |  UpdateActiveLeaf(newMessage.Id)
  |                               |  GetBranchWithAlternatives → new branch
  |                               |  Start ProcessWorkflowAfterEdit (background)
  |<------------------------------|
  |  200 [ branch without new AI ] |
  |                               |
  |  setMessages(newBranch)       |
  |  setPendingResponse(true)     |
  |  show "Thinking..."           |
  |                               |  ... workflow runs ...
  |<------- assistantChunk -------|
  |                               |  Save AI message, set ActiveLeafMessageId
  |<------- messagesUpdated ------|
  |  setMessages(full branch)     |
```

---

## 11. Summary Table

| Action | Who sets ActiveLeafMessageId | When | Client gets branch |
|--------|-----------------------------|------|---------------------|
| **Create dialog** | Server after first workflow run | After SaveMessagesFromStateAsync | GET messages or messagesUpdated |
| **Send message** | Server in ProcessWorkflowAsync | After saving new AI message | messagesUpdated (+ assistantChunk during) |
| **Edit message** | Server in EditMessageAsync (to new user msg), then in ProcessWorkflowAfterEditAsync (to new AI msg) | Immediately for edit, then after workflow | 200 with new branch, then messagesUpdated |
| **Switch version** | Server in SwitchVersionAsync | After UpdateActiveLeafAsync(leaf of chosen branch) | messagesUpdated |

All “current messages” in the UI are the **active branch**: the single path from root to `Conversation.ActiveLeafMessageId`, built by `GetBranchToLeafAsync(conversationId, ActiveLeafMessageId)`.
