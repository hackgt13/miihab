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
  speak:        '#5A6472',
  tool_call:    '#1666C0',
  cue_template: '#0D9488',
  llm_reply:    '#1A1D23',
  measurement:  '#0D9488',
  report:       '#C67C1A',
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
    <div className="bg-white border border-[#DDE2E8] rounded-md p-4 shadow-sm">
      <p className="text-[#5A6472] text-[9px] uppercase tracking-[0.18em] mb-3">
        Coach agent - session log
      </p>
      <div className="flex flex-col gap-0.5">
        {log.map((ev, i) => (
          <div
            key={i}
            className={`flex gap-3 items-start px-3 py-2 border-l-2 rounded-sm ${
              flagged.has(i) ? 'border-[#C62828] bg-[#C62828]/5' : 'border-transparent hover:border-[#DDE2E8] hover:bg-[#F4F6F8]'
            }`}
          >
            <span className="font-mono text-[10px] text-[#5A6472] w-8 shrink-0 pt-0.5">{fmt(ev.t)}</span>
            <span
              className="text-[9px] uppercase tracking-widest w-14 shrink-0 pt-0.5"
              style={{ color: kindColor[ev.kind] }}
            >
              {kindLabel[ev.kind]}
            </span>
            <span className="text-xs text-[#1A1D23] flex-1 leading-relaxed">
              {ev.kind === 'tool_call'
                ? <><span className="text-[#1666C0]">{ev.tool}</span>({JSON.stringify(ev.args)})</>
                : ev.content
              }
            </span>
            {ev.kind === 'llm_reply' && (
              <button
                onClick={() => toggle(i)}
                className={`text-[10px] shrink-0 transition-colors ${
                  flagged.has(i) ? 'text-[#C62828]' : 'text-[#DDE2E8] hover:text-[#C62828]'
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
