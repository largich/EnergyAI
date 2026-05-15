import { useEffect, useRef, useState } from 'react'
import type { ChatMessage, ChatSession } from '../lib/chatApi'
import { deleteSession, getSession, getSessions, streamMessage } from '../lib/chatApi'

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------
interface UiMessage extends ChatMessage {
  streaming?: boolean
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------
export default function ChatPage() {
  const [sessions, setSessions]       = useState<ChatSession[]>([])
  const [activeId, setActiveId]       = useState<string | null>(null)
  const [messages, setMessages]       = useState<UiMessage[]>([])
  const [input, setInput]             = useState('')
  const [persist, setPersist]         = useState(false)
  const [busy, setBusy]               = useState(false)
  const abortRef                      = useRef<AbortController | null>(null)
  const bottomRef                     = useRef<HTMLDivElement>(null)

  // Load session list on mount
  useEffect(() => { loadSessions() }, [])

  // Scroll to bottom when messages change
  useEffect(() => { bottomRef.current?.scrollIntoView({ behavior: 'smooth' }) }, [messages])

  async function loadSessions() {
    try { setSessions(await getSessions()) } catch { /* ignore */ }
  }

  async function openSession(id: string) {
    try {
      const detail = await getSession(id)
      setActiveId(id)
      setMessages(detail.messages)
    } catch { /* ignore */ }
  }

  async function handleDelete(e: React.MouseEvent, id: string) {
    e.stopPropagation()
    await deleteSession(id)
    if (activeId === id) { setActiveId(null); setMessages([]) }
    await loadSessions()
  }

  function newChat() {
    abortRef.current?.abort()
    setActiveId(null)
    setMessages([])
    setInput('')
  }

  async function handleSend() {
    const text = input.trim()
    if (!text || busy) return

    const userMsg: UiMessage = { role: 'user', content: text }
    const outgoing = [...messages, userMsg]
    setMessages(outgoing)
    setInput('')
    setBusy(true)

    const abort = new AbortController()
    abortRef.current = abort

    // Placeholder for the streaming assistant reply
    setMessages([...outgoing, { role: 'assistant', content: '', streaming: true }])

    let currentSessionId = activeId

    try {
      for await (const event of streamMessage(
        outgoing.map(m => ({ role: m.role, content: m.content })),
        currentSessionId,
        persist,
        abort.signal
      )) {
        if (event.type === 'session') {
          currentSessionId = event.id
          setActiveId(event.id)
          await loadSessions()
        } else if (event.type === 'text') {
          setMessages(prev => {
            const next = [...prev]
            const last = next[next.length - 1]
            if (last?.role === 'assistant') {
              next[next.length - 1] = { ...last, content: last.content + event.chunk }
            }
            return next
          })
        } else if (event.type === 'done') {
          setMessages(prev => {
            const next = [...prev]
            if (next[next.length - 1]?.streaming)
              next[next.length - 1] = { ...next[next.length - 1], streaming: false }
            return next
          })
          if (persist) await loadSessions()
        }
      }
    } catch (err: unknown) {
      if ((err as Error)?.name !== 'AbortError') {
        setMessages(prev => [...prev.slice(0, -1), {
          role: 'assistant', content: '⚠ Error communicating with the agent. Please try again.'
        }])
      }
    } finally {
      setBusy(false)
    }
  }

  function handleKeyDown(e: React.KeyboardEvent) {
    if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); handleSend() }
  }

  // ---------------------------------------------------------------------------
  // Render
  // ---------------------------------------------------------------------------
  return (
    <div className="chat-page">

      {/* Sidebar */}
      <aside className="chat-sidebar">
        <button className="btn-new-chat" onClick={newChat}>+ New chat</button>

        <div className="persist-toggle">
          <label>
            <input
              type="checkbox"
              checked={persist}
              onChange={e => setPersist(e.target.checked)}
            />
            {' '}Save conversations
          </label>
        </div>

        <ul className="session-list">
          {sessions.map(s => (
            <li
              key={s.id}
              className={`session-item ${s.id === activeId ? 'active' : ''}`}
              onClick={() => openSession(s.id)}
            >
              <span className="session-title" title={s.title}>{s.title}</span>
              <button
                className="btn-delete-session"
                onClick={e => handleDelete(e, s.id)}
                title="Delete"
              >×</button>
            </li>
          ))}
          {sessions.length === 0 && (
            <li className="session-empty">No saved chats yet</li>
          )}
        </ul>
      </aside>

      {/* Main chat area */}
      <div className="chat-main">
        <div className="chat-messages">
          {messages.length === 0 && (
            <div className="chat-welcome">
              <h2>Energy AI Assistant</h2>
              <p>Ask me anything about your energy consumption data.</p>
              <div className="chat-suggestions">
                {[
                  'Show anomalies in the last 7 days',
                  'Compare electricity usage this week vs last week',
                  'Which equipment consumes the most gas this month?',
                  'Are there any possible water leaks?',
                ].map(s => (
                  <button key={s} className="suggestion-chip"
                    onClick={() => { setInput(s) }}>
                    {s}
                  </button>
                ))}
              </div>
            </div>
          )}

          {messages.map((m, i) => (
            <div key={i} className={`chat-message ${m.role}`}>
              <div className="message-role">{m.role === 'user' ? 'You' : 'AI'}</div>
              <div className="message-content">
                {m.content}
                {m.streaming && <span className="cursor-blink">▌</span>}
              </div>
            </div>
          ))}
          <div ref={bottomRef} />
        </div>

        <div className="chat-input-bar">
          <textarea
            className="chat-input"
            rows={2}
            placeholder="Ask about consumption, anomalies, comparisons…"
            value={input}
            onChange={e => setInput(e.target.value)}
            onKeyDown={handleKeyDown}
            disabled={busy}
          />
          <button
            className="btn-send"
            onClick={handleSend}
            disabled={busy || !input.trim()}
          >
            {busy ? '…' : 'Send'}
          </button>
        </div>
      </div>
    </div>
  )
}
