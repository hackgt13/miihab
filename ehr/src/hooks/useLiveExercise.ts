import { useEffect, useRef, useState } from 'react'
import type { SessionOverride } from '../data/unityProtocol'

// The set the patient is doing right now, rep by rep, from the coordinator's exercise stream (/exercise, the same
// messages the headset studio reads). While a set runs the portal's live readings come from here and update as
// each rep lands; between sets `running` is false and the page shows the last recorded session.
//
// Same host as the page: the coordinator serves the EHR at /ehr, and `npm run dev` proxies /exercise to it.

export type LiveStatus = 'connecting' | 'open' | 'closed'

export interface LiveExercise {
  status: LiveStatus
  running: boolean
  override: SessionOverride | null
  angleDeg: number | null        // the arm right now
  lastRep: string | null         // "Rep 3 counted · 54°"
}

export function useLiveExercise(): LiveExercise {
  const [state, setState] = useState<LiveExercise>({ status: 'connecting', running: false, override: null, angleDeg: null, lastRep: null })
  const set = useRef({ attempted: 0, valid: 0, peaks: [] as number[], prescribed: 0, trunk: 0 })

  useEffect(() => {
    let ws: WebSocket | null = null, retry: ReturnType<typeof setTimeout> | null = null, closed = false
    const override = (): SessionOverride => {
      const s = set.current
      return { romDeg: s.peaks.length ? Math.round(Math.max(...s.peaks)) : 0, trunkDeg: Math.round(s.trunk),
        repsValid: s.valid, repsAttempted: s.attempted, prescribedReps: s.prescribed, painVas: null }
    }
    const connect = () => {
      const scheme = location.protocol === 'https:' ? 'wss' : 'ws'
      ws = new WebSocket(`${scheme}://${location.host}/exercise?role=viewer`)
      ws.onopen = async () => {
        setState(s => ({ ...s, status: 'open' }))
        // Opened mid-set: take the reps already made from the set so far, then follow the stream from here.
        try {
          const r = await fetch('/exercise', { cache: 'no-store' })
          const d = await r.json()
          // No set now: a set that ended while this socket was down must not stay "Live" with stale numbers.
          if (!d.running || !d.summary) { setState(s => ({ ...s, running: false, angleDeg: null })); return }
          const reps: { peakDeg?: number; valid?: boolean; compensationMaxDeg?: number }[] = d.summary.reps ?? []
          set.current = { attempted: Number(d.summary.attempted ?? reps.length), valid: Number(d.summary.valid ?? 0),
            peaks: reps.filter(x => x.valid).map(x => Number(x.peakDeg)).filter(Number.isFinite), prescribed: Number(d.summary.prescribed ?? 0),
            trunk: Math.max(0, ...reps.map(x => Number(x.compensationMaxDeg)).filter(Number.isFinite)) }
          setState(s => ({ ...s, running: true, override: override() }))
        } catch { /* the stream alone still counts from the next rep */ }
      }
      // Not live while unheard: the page falls back to the last recorded session, and the reconnect's check above
      // puts "Live" back if the set is still running.
      ws.onclose = () => { setState(s => ({ ...s, status: 'closed', running: false, angleDeg: null })); if (!closed) retry = setTimeout(connect, 2000) }
      ws.onmessage = event => {
        let m: any; try { m = JSON.parse(String(event.data)) } catch { return }
        const p = m.payload ?? {}
        if (m.type === 'exercise.started') {
          set.current = { attempted: 0, valid: 0, peaks: [], trunk: 0,
            prescribed: Number(p.config?.prescribedReps ?? p.prescribed ?? 0) }
          setState(s => ({ ...s, running: true, override: override(), angleDeg: null, lastRep: null }))
        } else if (m.type === 'exercise.sample') {
          const angle = Number.isFinite(p.angleDeg) ? Math.round(p.angleDeg) : null
          setState(s => ({ ...s, running: true, angleDeg: angle }))
        } else if (m.type === 'exercise.event' && p.type === 'rep.completed') {
          const s = set.current
          s.attempted++; if (p.valid) s.valid++
          // Peak ROM is the reach of reps that counted: a rep rejected for compensation (leaning into it) would
          // otherwise inflate the number the physician compares against a baseline of valid reps.
          if (p.valid && Number.isFinite(p.peakDeg)) s.peaks.push(p.peakDeg)
          if (Number.isFinite(p.compensationMaxDeg)) s.trunk = Math.max(s.trunk, p.compensationMaxDeg)
          const line = `Rep ${s.attempted} ${p.valid ? 'counted' : `not counted (${String(p.reason ?? '').replace(/_/g, ' ')})`}` +
            (Number.isFinite(p.peakDeg) ? ` · ${Math.round(p.peakDeg)}°` : '')
          setState(st => ({ ...st, override: override(), lastRep: line }))
        } else if (m.type === 'exercise.summary') {
          setState(s => ({ ...s, running: false, angleDeg: null }))
        }
      }
    }
    connect()
    return () => { closed = true; if (retry) clearTimeout(retry); ws?.close() }
  }, [])

  return state
}
