import { useEffect, useState } from 'react'
import { Button } from './Button'

// Notes the therapist leaves for the patient's visit. They are written up on the whiteboard in the
// patient's RehabMii visit scene, under "From your licensed therapist", and read aloud by Alex.
// Served by the RehabMii coordinator (coordinator/visit.ts) through the /api proxy in vite.config.ts.

interface Note { id: string; text: string; author: string; at: string }
interface BoardUpdate { kind: string; heading: string; detail: string }
interface Reply { id: string; kind: string; text: string; at: string }

const LIMIT = 280

export function VisitNotes() {
  const [notes, setNotes] = useState<Note[]>([])
  const [board, setBoard] = useState<BoardUpdate[]>([])
  const [replies, setReplies] = useState<Reply[]>([])
  const [text, setText] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [offline, setOffline] = useState(false)
  const [busy, setBusy] = useState(false)

  async function load() {
    try {
      const [n, v, r] = await Promise.all([fetch('/api/visit/notes'), fetch('/api/visit'), fetch('/api/visit/replies')])
      if (!n.ok || !v.ok || !r.ok) throw Error()
      setReplies(await r.json())
      setNotes(await n.json())
      setBoard((await v.json()).board.updates)
      setOffline(false)
    } catch { setOffline(true) }
  }
  useEffect(() => { load() }, [])

  async function post() {
    setBusy(true); setError(null)
    try {
      const r = await fetch('/api/visit/notes', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ text, author: 'Clinician portal' }),
      })
      if (!r.ok) throw Error((await r.json()).error ?? 'Could not save the note')
      setText(''); await load()
    } catch (e) { setError((e as Error).message) } finally { setBusy(false) }
  }

  async function remove(id: string) {
    await fetch(`/api/visit/notes/${id}`, { method: 'DELETE' })
    await load()
  }

  return (
    <div className="bg-white border border-[#DDE2E8] rounded-md shadow-sm p-4">
      <div className="flex items-center justify-between mb-3">
        <p className="text-[#5A6472] text-[9px] uppercase tracking-[0.18em]">Visit whiteboard</p>
        <span className="text-[10px] text-[#5A6472] font-mono">
          {offline ? 'Coordinator offline' : `${board.length} on the board`}
        </span>
      </div>

      {/* What the patient relayed at the end of their visits, newest first. "Something hurt" leads the eye. */}
      {!offline && replies.length > 0 && (
        <div className="mb-4">
          <p className="text-[#5A6472] text-[9px] uppercase tracking-[0.18em] mb-1.5">From the patient</p>
          <ul className="space-y-1">
            {replies.slice().reverse().slice(0, 5).map(r => (
              <li key={r.id} className="text-xs text-[#1A1D23]">
                <span className="font-mono text-[10px] uppercase mr-2" style={{ color: r.kind === 'hurt' ? '#C62828' : '#0D9488' }}>{r.kind}</span>
                {r.text}
                <span className="text-[#5A6472] font-mono text-[10px]"> · {new Date(r.at).toLocaleDateString()}</span>
              </li>
            ))}
          </ul>
        </div>
      )}

      {!offline && (
        <ul className="mb-4 space-y-1">
          {board.map((u, i) => (
            <li key={i} className="text-xs text-[#1A1D23]">
              <span className="font-mono text-[10px] text-[#1666C0] uppercase mr-2">{u.kind}</span>
              {u.heading}<span className="text-[#5A6472]"> · {u.detail}</span>
            </li>
          ))}
        </ul>
      )}

      <textarea
        value={text}
        onChange={e => setText(e.target.value.slice(0, LIMIT))}
        placeholder="A note for the patient's next visit, e.g. Ice your shoulder after golf."
        rows={2}
        className="w-full bg-[#F4F6F8] border border-[#DDE2E8] text-[#1A1D23] text-sm p-2 mb-2 focus:outline-none focus:border-[#1666C0]"
      />
      <div className="flex items-center justify-between mb-3">
        <span className="text-[10px] text-[#5A6472] font-mono">{text.length} / {LIMIT}</span>
        <Button size="sm" disabled={busy || offline || !text.trim()} onClick={post}>Add to whiteboard</Button>
      </div>
      {error && <p className="text-xs text-[#C62828] mb-2">{error}</p>}

      {notes.length > 0 && (
        <ul className="space-y-1 border-t border-[#DDE2E8] pt-3">
          {notes.slice().reverse().map(n => (
            <li key={n.id} className="flex items-start justify-between gap-3 text-xs">
              <span className="text-[#1A1D23]">{n.text}
                <span className="text-[#5A6472] font-mono text-[10px]"> · {new Date(n.at).toLocaleDateString()}</span>
              </span>
              <Button size="sm" variant="ghost" onClick={() => remove(n.id)}>Remove</Button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
