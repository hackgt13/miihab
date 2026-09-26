import { useState } from 'react'

interface PlanField {
  label: string
  v1: string
  v2: string
  changed: boolean
}

const DIFF: PlanField[] = [
  { label: 'Exercise',          v1: 'Seated shoulder raise (R)', v2: 'Seated shoulder raise (R)', changed: false },
  { label: 'Target range',      v1: '68 – 84°',                  v2: '68 – 90°',                  changed: true  },
  { label: 'Prescribed reps',   v1: '8',                         v2: '10',                         changed: true  },
  { label: 'Trunk constraint',  v1: 'Warn > 8°',                 v2: 'Warn > 6°',                  changed: true  },
  { label: 'Coach cue emphasis',v1: 'Default',                   v2: 'Chest-forward priority',     changed: true  },
]

const HISTORY = [
  { version: 1, date: '2025-08-12', note: 'Initial plan', active: false },
]

export function PlanVersioning() {
  const [approved, setApproved] = useState(false)

  return (
    <div className="bg-white border border-[#DDE2E8] rounded-md shadow-sm">
      <div className="px-4 py-3 border-b border-[#DDE2E8] flex items-center justify-between">
        <p className="text-[#5A6472] text-[9px] uppercase tracking-[0.18em]">Plan versioning</p>
        <div className="flex items-center gap-2 text-[10px] text-[#5A6472]">
          <span className="w-1.5 h-1.5 rounded-full bg-[#2E7D32]" />
          v1 active
        </div>
      </div>

      {/* Diff */}
      <div className="p-4">
        <p className="text-[#5A6472] text-[9px] uppercase tracking-[0.18em] mb-3">
          Draft — Plan v2
        </p>
        <div className="border border-[#DDE2E8] rounded-md overflow-hidden mb-4">
          <div className="grid grid-cols-3 bg-[#F4F6F8] px-3 py-1.5 border-b border-[#DDE2E8]">
            <p className="text-[9px] uppercase tracking-widest text-[#5A6472]">Field</p>
            <p className="text-[9px] uppercase tracking-widest text-[#5A6472]">v1 (current)</p>
            <p className="text-[9px] uppercase tracking-widest text-[#1666C0]">v2 (draft)</p>
          </div>
          {DIFF.map(row => (
            <div
              key={row.label}
              className={`grid grid-cols-3 px-3 py-2 border-b border-[#DDE2E8] last:border-0 ${
                row.changed ? 'bg-[#EEF4FC]' : ''
              }`}
            >
              <p className="text-[10px] text-[#5A6472]">{row.label}</p>
              <p className="font-mono text-[10px] text-[#5A6472]">{row.v1}</p>
              <p className={`font-mono text-[10px] ${row.changed ? 'text-[#1666C0]' : 'text-[#5A6472]'}`}>
                {row.changed && <span className="mr-1 text-[#1666C0]">→</span>}
                {row.v2}
              </p>
            </div>
          ))}
        </div>

        {!approved ? (
          <button
            onClick={() => setApproved(true)}
            className="w-full py-2.5 bg-[#1666C0] text-white text-sm font-semibold rounded-sm hover:bg-[#1255A3] transition-colors"
          >
            Approve — create Plan v2
          </button>
        ) : (
          <div className="flex items-center gap-2 px-3 py-2.5 bg-[#2E7D32]/10 border border-[#2E7D32]/30 rounded-sm">
            <span className="w-1.5 h-1.5 rounded-full bg-[#2E7D32]" />
            <p className="text-xs text-[#2E7D32] font-medium">Plan v2 approved — next session will use updated targets</p>
          </div>
        )}
      </div>

      {/* History */}
      <div className="px-4 pb-4">
        <p className="text-[#5A6472] text-[9px] uppercase tracking-[0.18em] mb-2">History</p>
        <div className="flex flex-col gap-1">
          {[...HISTORY, ...(approved ? [{ version: 2, date: new Date().toISOString().slice(0, 10), note: 'Approved this session', active: true }] : [])].map(v => (
            <div key={v.version} className="flex items-center gap-3 px-3 py-1.5 border border-[#DDE2E8] rounded-sm">
              <span className={`text-[10px] font-mono ${v.active ? 'text-[#2E7D32]' : 'text-[#5A6472]'}`}>v{v.version}</span>
              <span className="text-[10px] text-[#5A6472] font-mono">{v.date}</span>
              <span className="text-[10px] text-[#5A6472] flex-1">{v.note}</span>
              {v.active && <span className="w-1.5 h-1.5 rounded-full bg-[#2E7D32]" />}
            </div>
          ))}
        </div>
      </div>
    </div>
  )
}
