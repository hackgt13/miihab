import type { ReplayMoment, RepEvent } from '../data/types'

function fmt(sec: number) {
  const m = Math.floor(sec / 60)
  const s = Math.floor(sec % 60).toString().padStart(2, '0')
  return `${m}:${s}`
}

interface ReplayMomentsProps {
  moments: ReplayMoment[]
  repEvents: RepEvent[]
  onOpen?: (moment: ReplayMoment) => void
}

export function ReplayMoments({ moments, repEvents, onOpen }: ReplayMomentsProps) {
  const invalid = repEvents.filter(r => !r.valid)

  return (
    <div className="bg-white border border-[#DDE2E8] rounded-md p-4 shadow-sm">
      <p className="text-[#5A6472] text-[9px] uppercase tracking-[0.18em] mb-3">
        Replay moments
      </p>

      <div className="flex flex-col gap-1.5 mb-4">
        {moments.map((m, i) => (
          <button
            key={i}
            onClick={() => onOpen?.(m)}
            className="flex items-center justify-between px-3 py-2 bg-[#F4F6F8] border border-[#DDE2E8] hover:border-[#1666C0]/40 hover:bg-[#EEF4FC] rounded-sm transition-colors text-left group"
          >
            <div className="flex items-center gap-3">
              <span className="font-mono text-[10px] text-[#1666C0] w-8">{fmt(m.t)}</span>
              <span className="text-xs text-[#1A1D23]">{m.label}</span>
            </div>
            <span className="text-[#5A6472] text-[10px] group-hover:text-[#1666C0] transition-colors">▶</span>
          </button>
        ))}
      </div>

      {invalid.length > 0 && (
        <>
          <p className="text-[#5A6472] text-[9px] uppercase tracking-[0.18em] mb-2">Invalid reps</p>
          <div className="flex flex-col gap-1">
            {invalid.map(r => (
              <div key={r.rep} className="flex items-center gap-3 px-3 py-1.5 bg-[#C62828]/5 border border-[#C62828]/20 rounded-sm">
                <span className="text-[#C62828] font-mono text-[10px] w-8">Rep {r.rep}</span>
                <span className="text-[10px] text-[#5A6472]">{r.reason}</span>
                <span className="ml-auto font-mono text-[10px] text-[#5A6472]">{r.trunkDeg}° trunk</span>
              </div>
            ))}
          </div>
        </>
      )}
    </div>
  )
}
