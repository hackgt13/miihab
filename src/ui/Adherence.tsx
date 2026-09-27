import type { SessionSummary } from '../data/types'

interface AdherenceProps {
  schedule: { date: string; completed: boolean }[]
  history: SessionSummary[]
}

export function Adherence({ schedule, history }: AdherenceProps) {
  const completed = schedule.filter(s => s.completed).length
  const total = schedule.length
  const pct = Math.round((completed / total) * 100)

  return (
    <div className="bg-white border border-[#DDE2E8] rounded-md p-4 shadow-sm">
      <div className="flex items-center justify-between mb-3">
        <p className="text-[#5A6472] text-[9px] uppercase tracking-[0.18em]">Adherence</p>
        <span className="font-mono text-sm text-[#1A1D23] font-bold">{pct}%</span>
      </div>

      {/* Bar */}
      <div className="h-1.5 bg-[#DDE2E8] rounded-full mb-4">
        <div className="h-full bg-[#2E7D32] rounded-full transition-all" style={{ width: `${pct}%` }} />
      </div>

      {/* Calendar dots */}
      <div className="flex flex-wrap gap-1 mb-3">
        {schedule.map((s, i) => (
          <span
            key={i}
            className="w-3 h-3 rounded-sm"
            style={{ backgroundColor: s.completed ? '#2E7D32' : '#DDE2E8' }}
            title={`${s.date} - ${s.completed ? 'completed' : 'missed'}`}
          />
        ))}
      </div>

      <p className="text-[10px] text-[#5A6472] font-mono">
        {completed} / {total} sessions completed
      </p>

      {/* Recent trend */}
      <div className="mt-3 pt-3 border-t border-[#DDE2E8]">
        <p className="text-[#5A6472] text-[9px] uppercase tracking-widest mb-2">Last 5 sessions</p>
        <div className="flex flex-col gap-1">
          {history.slice(-5).reverse().map(s => (
            <div key={s.session} className="flex items-center justify-between px-2 py-1 bg-[#F4F6F8] rounded-sm">
              <span className="text-[10px] text-[#5A6472] font-mono">{s.date}</span>
              <span className="text-[10px] text-[#5A6472] font-mono">{s.validReps}/{s.prescribedReps} reps</span>
              <span className="text-[10px] font-mono text-[#1666C0]">{s.medianPeakDeg}°</span>
            </div>
          ))}
        </div>
      </div>
    </div>
  )
}
