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
    <div className="bg-[#2A3337] border border-[#3D484E] p-4">
      <p className="text-[#A3B0B6] text-[9px] uppercase tracking-[0.18em] mb-3">
        Replay moments
      </p>

      <div className="flex flex-col gap-1.5 mb-4">
        {moments.map((m, i) => (
          <button
            key={i}
            onClick={() => onOpen?.(m)}
            className="flex items-center justify-between px-3 py-2 bg-[#232B2F] border border-[#3D484E] hover:border-[#6FB8C4]/40 hover:bg-[#323C41] transition-colors text-left group"
          >
            <div className="flex items-center gap-3">
              <span className="font-mono text-[10px] text-[#6FB8C4] w-8">{fmt(m.t)}</span>
              <span className="text-xs text-[#E4E9EB]">{m.label}</span>
            </div>
            <span className="text-[#A3B0B6] text-[10px] group-hover:text-[#6FB8C4] transition-colors">▶</span>
          </button>
        ))}
      </div>

      {invalid.length > 0 && (
        <>
          <p className="text-[#A3B0B6] text-[9px] uppercase tracking-[0.18em] mb-2">Invalid reps</p>
          <div className="flex flex-col gap-1">
            {invalid.map(r => (
              <div key={r.rep} className="flex items-center gap-3 px-3 py-1.5 bg-[#E08585]/5 border border-[#E08585]/20">
                <span className="text-[#E08585] font-mono text-[10px] w-8">Rep {r.rep}</span>
                <span className="text-[10px] text-[#A3B0B6]">{r.reason}</span>
                <span className="ml-auto font-mono text-[10px] text-[#A3B0B6]">{r.trunkDeg}° trunk</span>
              </div>
            ))}
          </div>
        </>
      )}
    </div>
  )
}
