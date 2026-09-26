import { useState } from 'react'
import type { SessionEvent, EventKind } from '../data/types'

function fmt(sec: number) {
  const m = Math.floor(sec / 60)
  const s = Math.floor(sec % 60).toString().padStart(2, '0')
  return `${m}:${s}`
}

const kindLabel: Record<EventKind, string> = {
  speak:        'speak',
  tool_call:    'tool',
  cue_template: 'template',
  llm_reply:    'llm',
  measurement:  'measure',
  report:       'report',
}

const kindColor: Record<EventKind, string> = {
  speak:        '#A3B0B6',
  tool_call:    '#6FB8C4',
  cue_template: '#9CC2B5',
  llm_reply:    '#E4E9EB',
  measurement:  '#9CC2B5',
  report:       '#E3A86B',
}

interface CoachFeedProps {
  log: SessionEvent[]
}

export function CoachFeed({ log }: CoachFeedProps) {
  const [flagged, setFlagged] = useState<Set<number>>(new Set())

  function toggle(i: number) {
    setFlagged(prev => {
      const next = new Set(prev)
      next.has(i) ? next.delete(i) : next.add(i)
      return next
    })
  }

  return (
    <div className="bg-[#2A3337] border border-[#3D484E] p-4">
      <p className="text-[#A3B0B6] text-[9px] uppercase tracking-[0.18em] mb-3">
        Coach agent — session log
      </p>
      <div className="flex flex-col gap-0.5">
        {log.map((ev, i) => (
          <div
            key={i}
            className={`flex gap-3 items-start px-3 py-2 border-l-2 ${
              flagged.has(i) ? 'border-[#E08585] bg-[#E08585]/5' : 'border-transparent hover:border-[#3D484E]'
            }`}
          >
            <span className="font-mono text-[10px] text-[#A3B0B6] w-8 shrink-0 pt-0.5">{fmt(ev.t)}</span>
            <span
              className="text-[9px] uppercase tracking-widest w-14 shrink-0 pt-0.5"
              style={{ color: kindColor[ev.kind] }}
            >
              {kindLabel[ev.kind]}
            </span>
            <span className="text-xs text-[#E4E9EB] flex-1 leading-relaxed">
              {ev.kind === 'tool_call'
                ? <><span className="text-[#6FB8C4]">{ev.tool}</span>({JSON.stringify(ev.args)})</>
                : ev.content
              }
            </span>
            {ev.kind === 'llm_reply' && (
              <button
                onClick={() => toggle(i)}
                className={`text-[10px] shrink-0 transition-colors ${
                  flagged.has(i) ? 'text-[#E08585]' : 'text-[#3D484E] hover:text-[#E08585]'
                }`}
                title="Flag this reply"
              >
                ⚑
              </button>
            )}
          </div>
        ))}
      </div>
    </div>
  )
}
