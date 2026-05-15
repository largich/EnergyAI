export interface ChatMessage  { role: 'user' | 'assistant'; content: string }
export interface ChatSession  { id: string; title: string; createdAt: string; isPinned: boolean }
export interface ChatSessionDetail extends ChatSession { messages: ChatMessage[] }

export type SseEvent =
  | { type: 'session'; id: string }
  | { type: 'text';    chunk: string }
  | { type: 'done' }
  | { type: 'error';   message: string }

/**
 * POST /api/chat/message and yield typed SSE events as they arrive.
 * The generator ends when the `done` event is received or the stream closes.
 */
export async function* streamMessage(
  messages: ChatMessage[],
  sessionId: string | null,
  persist: boolean,
  signal?: AbortSignal
): AsyncGenerator<SseEvent> {
  const res = await fetch('/api/chat/message', {
    method:  'POST',
    headers: { 'Content-Type': 'application/json' },
    body:    JSON.stringify({ messages, sessionId, persist }),
    signal,
  })

  if (!res.ok || !res.body) {
    yield { type: 'error', message: `HTTP ${res.status}` }
    return
  }

  const reader  = res.body.getReader()
  const decoder = new TextDecoder()
  let   buffer  = ''

  while (true) {
    const { done, value } = await reader.read()
    if (done) break

    buffer += decoder.decode(value, { stream: true })

    // SSE messages are separated by double newlines
    const parts = buffer.split('\n\n')
    buffer = parts.pop() ?? ''

    for (const part of parts) {
      const lines     = part.trim().split('\n')
      let   eventName = 'message'
      let   dataLine  = ''

      for (const line of lines) {
        if (line.startsWith('event: ')) eventName = line.slice(7).trim()
        if (line.startsWith('data: '))  dataLine  = line.slice(6).trim()
      }

      if (!dataLine) continue

      if (eventName === 'session') {
        const parsed = JSON.parse(dataLine) as { id: string }
        yield { type: 'session', id: parsed.id }
      } else if (eventName === 'text') {
        yield { type: 'text', chunk: JSON.parse(dataLine) as string }
      } else if (eventName === 'done') {
        yield { type: 'done' }
        return
      }
    }
  }
}

// ---- Session REST ----

export async function getSessions(): Promise<ChatSession[]> {
  const r = await fetch('/api/chat/sessions')
  if (!r.ok) throw new Error(`HTTP ${r.status}`)
  return r.json()
}

export async function getSession(id: string): Promise<ChatSessionDetail> {
  const r = await fetch(`/api/chat/sessions/${id}`)
  if (!r.ok) throw new Error(`HTTP ${r.status}`)
  return r.json()
}

export async function deleteSession(id: string): Promise<void> {
  const r = await fetch(`/api/chat/sessions/${id}`, { method: 'DELETE' })
  if (!r.ok && r.status !== 404) throw new Error(`HTTP ${r.status}`)
}
