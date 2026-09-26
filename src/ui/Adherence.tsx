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
    <div className="bg-[#2A3337] border border-[#3D484E] p-4">
      <div className="flex items-center justify-between mb-3">
        <p className="text-[#A3B0B6] text-[9px] uppercase tracking-[0.18em]">Adherence</p>
        <span className="font-mono text-sm text-[#E4E9EB] font-bold">{pct}%</span>
      </div>

      {/* Bar */}
      <div className="h-1 bg-[#3D484E] mb-4">
        <div className="h-full bg-[#7CC49A] transition-all" style={{ width: `${pct}%` }} />
      </div>

      {/* Calendar dots */}
      <div className="flex flex-wrap gap-1 mb-3">
        {schedule.map((s, i) => (
          <span
            key={i}
            className="w-3 h-3"
            style={{ backgroundColor: s.completed ? '#7CC49A' : '#3D484E' }}
            title={`${s.date} — ${s.completed ? 'completed' : 'missed'}`}
          />
        ))}
      </div>

      <p className="text-[10px] text-[#A3B0B6] font-mono">
        {completed} / {total} sessions completed
      </p>

      {/* Recent trend */}
      <div className="mt-3 pt-3 border-t border-[#3D484E]">
        <p className="text-[#A3B0B6] text-[9px] uppercase tracking-widest mb-2">Last 5 sessions</p>
        <div className="flex flex-col gap-1">
          {history.slice(-5).reverse().map(s => (
            <div key={s.session} className="flex items-center justify-between px-2 py-1 bg-[#232B2F]">
              <span className="text-[10px] text-[#A3B0B6] font-mono">{s.date}</span>
              <span className="text-[10px] text-[#A3B0B6] font-mono">{s.validReps}/{s.prescribedReps} reps</span>
              <span className="text-[10px] font-mono text-[#6FB8C4]">{s.medianPeakDeg}°</span>
            </div>
          ))}
        </div>
      </div>
    </div>
  )
}
