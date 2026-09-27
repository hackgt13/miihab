import { useEffect, useMemo, useRef, useState } from 'react'
import type { PlanVersion, SessionSummary, VisitReply } from '../data/types'

// The whole program replayed in a few seconds: every session's reach landing on the chart as the clock passes it,
// the target stepping up at each approved plan version, and what the patient reported, in order. It shows a
// physician (or a room of judges) in twelve seconds what weeks of home practice looked like, from the same
// records the rest of this page reads. Nothing here is invented: a seeded session is drawn hollow, as it is
// everywhere else.

interface Props { sessions: SessionSummary[]; plans: PlanVersion[]; replies?: VisitReply[] }

type Moment =
  | { at: number; kind: 'session'; s: SessionSummary }
  | { at: number; kind: 'plan'; p: PlanVersion; target: number | null }
  | { at: number; kind: 'reply'; r: VisitReply }

const W = 760, H = 220, M = { l: 40, r: 16, t: 14, b: 26 }
const DURATION_MS = 12_000
const REPLY_LABEL: Record<string, string> = { hurt: 'Something hurt', hard: 'Too hard', easy: 'Too easy', fine: 'All good', message: 'Message' }

/** The target degrees a plan version sets, read from its settings ("85–100°" → 85). */
function targetOf(p: PlanVersion): number | null {
  const setting = p.settings.find(s => /target/i.test(s.setting))
  const match = String(setting?.current ?? '').match(/-?\d+(\.\d+)?/)
  return match ? Number(match[0]) : null
}

const day = (t: number) => new Date(t).toLocaleDateString([], { month: 'short', day: 'numeric' })

export function ProgramTimelapse({ sessions, plans, replies = [] }: Props) {
  const moments = useMemo<Moment[]>(() => [
    ...sessions.filter(s => s.completed).map(s => ({ at: Date.parse(s.date), kind: 'session' as const, s })),
    ...plans.map(p => ({ at: Date.parse(p.approvedAt ?? p.date), kind: 'plan' as const, p, target: targetOf(p) })),
    ...replies.map(r => ({ at: Date.parse(r.at), kind: 'reply' as const, r })),
  ].filter(m => Number.isFinite(m.at)).sort((a, b) => a.at - b.at), [sessions, plans, replies])

  const start = moments[0]?.at ?? 0, end = moments.at(-1)?.at ?? 1
  const span = Math.max(1, end - start)
  const [progress, setProgress] = useState(1)        // 0..1 of the program shown; starts complete
  const [playing, setPlaying] = useState(false)
  const [speed, setSpeed] = useState(1)
  const raf = useRef<number | null>(null)

  useEffect(() => {
    if (!playing) return
    let last = performance.now()
    const step = (now: number) => {
      const dt = now - last; last = now
      setProgress(p => {
        const next = Math.min(1, p + (dt * speed) / DURATION_MS)
        if (next >= 1) setPlaying(false)
        return next
      })
      raf.current = requestAnimationFrame(step)
    }
    raf.current = requestAnimationFrame(step)
    return () => { if (raf.current) cancelAnimationFrame(raf.current) }
  }, [playing, speed])

  const clock = start + span * progress
  const shown = moments.filter(m => m.at <= clock)
  const points = shown.filter((m): m is Extract<Moment, { kind: 'session' }> => m.kind === 'session')
  const targets = moments.filter((m): m is Extract<Moment, { kind: 'plan' }> => m.kind === 'plan' && m.target != null)

  const degs = [...moments.flatMap(m => m.kind === 'session' ? [m.s.medianPeakDeg] : m.kind === 'plan' && m.target != null ? [m.target] : [])]
  const lo = Math.floor((Math.min(...degs, 40) - 10) / 10) * 10, hi = Math.ceil((Math.max(...degs, 60) + 10) / 10) * 10
  const x = (t: number) => M.l + ((t - start) / span) * (W - M.l - M.r)
  const y = (v: number) => M.t + ((hi - v) / (hi - lo)) * (H - M.t - M.b)

  // The target as a step line, drawn only as far as the clock has reached.
  const targetPath = targets.filter(t => t.at <= clock).map((t, i, all) => {
    const x0 = x(t.at), x1 = x(Math.min(clock, all[i + 1]?.at ?? clock))
    return `M${x0},${y(t.target!)} H${x1}`
  }).join(' ')

  const latest = shown.at(-1)
  const ticker = !latest ? 'Press play to replay the program.' :
    latest.kind === 'session' ? `${day(latest.at)} · Session ${latest.s.session}: ${latest.s.validReps}/${latest.s.prescribedReps} reps counted, reach ${latest.s.medianPeakDeg}°${latest.s.synthetic ? ' (seeded)' : ''}` :
    latest.kind === 'plan' ? `${day(latest.at)} · Plan v${latest.p.version} approved${latest.p.approvedBy ? ` by ${latest.p.approvedBy}` : ''}${latest.target != null ? `: target ${latest.target}°` : ''}` :
    `${day(latest.at)} · Patient: ${REPLY_LABEL[latest.r.kind] ?? latest.r.kind}${latest.r.text ? ` — “${latest.r.text}”` : ''}`

  if (!moments.length) return <p className="text-xs text-[#5A6472] px-1">No sessions or plan versions to replay yet.</p>

  return (
    <div className="bg-white border border-[#DDE2E8] rounded-md shadow-sm p-4">
      <div className="flex items-center justify-between mb-2">
        <p className="text-[#5A6472] text-[9px] uppercase tracking-[0.18em]">Program timelapse · {day(start)} → {day(end)}</p>
        <div className="flex items-center gap-2">
          <select value={speed} onChange={e => setSpeed(Number(e.target.value))}
            className="bg-[#F4F6F8] border border-[#DDE2E8] text-[11px] px-1 py-[2px] text-[#1A1D23]">
            {[0.5, 1, 2].map(v => <option key={v} value={v}>{v}×</option>)}
          </select>
          <button onClick={() => { if (progress >= 1) setProgress(0); setPlaying(p => !p) }}
            className="px-2.5 py-[3px] border border-[#1666C0] bg-[#1666C0] text-white text-[11px] rounded-[2px]">
            {playing ? 'Pause' : progress >= 1 ? '▶ Replay program' : '▶ Play'}
          </button>
        </div>
      </div>

      <svg viewBox={`0 0 ${W} ${H}`} className="w-full" role="img" aria-label="Reach per session over the program, with the plan target">
        {Array.from({ length: (hi - lo) / 10 + 1 }, (_, i) => lo + i * 10).map(v => (
          <g key={v}>
            <line x1={M.l} x2={W - M.r} y1={y(v)} y2={y(v)} stroke="#EEF1F4" />
            <text x={M.l - 6} y={y(v) + 3} textAnchor="end" fontSize="10" fill="#8A94A2">{v}°</text>
          </g>
        ))}
        <path d={targetPath} stroke="#C67C1A" strokeWidth="2" strokeDasharray="5 4" fill="none" />
        <polyline fill="none" stroke="#1666C0" strokeWidth="2" points={points.map(p => `${x(p.at)},${y(p.s.medianPeakDeg)}`).join(' ')} />
        {points.map((p, i) => (
          <circle key={i} cx={x(p.at)} cy={y(p.s.medianPeakDeg)} r="4" stroke="#1666C0" strokeWidth="2"
            fill={p.s.synthetic ? '#FFFFFF' : '#1666C0'} />
        ))}
        {shown.filter(m => m.kind === 'plan').map((m, i) => (
          <g key={`p${i}`}>
            <line x1={x(m.at)} x2={x(m.at)} y1={M.t} y2={H - M.b} stroke="#C67C1A" strokeOpacity=".35" />
            <text x={x(m.at) + 3} y={M.t + 10} fontSize="10" fill="#C67C1A">v{(m as Extract<Moment, { kind: 'plan' }>).p.version}</text>
          </g>
        ))}
        {shown.filter(m => m.kind === 'reply').map((m, i) => {
          const r = (m as Extract<Moment, { kind: 'reply' }>).r
          return <text key={`r${i}`} x={x(m.at)} y={H - M.b - 4} textAnchor="middle" fontSize="12" fill={r.kind === 'hurt' ? '#C62828' : '#0D9488'}>●</text>
        })}
        <line x1={x(clock)} x2={x(clock)} y1={M.t} y2={H - M.b} stroke="#1A1D23" strokeOpacity=".25" />
        <text x={M.l} y={H - 6} fontSize="10" fill="#8A94A2">{day(start)}</text>
        <text x={W - M.r} y={H - 6} textAnchor="end" fontSize="10" fill="#8A94A2">{day(end)}</text>
      </svg>

      <div className="flex items-center justify-between mt-1">
        <p className="text-xs text-[#1A1D23] font-mono">{ticker}</p>
        <p className="text-[10px] text-[#5A6472]">
          <span className="text-[#1666C0]">●</span> reach per session&nbsp;&nbsp;
          <span className="text-[#C67C1A]">– –</span> plan target&nbsp;&nbsp;
          <span className="text-[#C62828]">●</span> patient report
        </p>
      </div>
    </div>
  )
}
