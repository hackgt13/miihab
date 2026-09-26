import { useState, useEffect } from 'react'
import type { SessionHandoff } from '../data/types'

interface LiveReadingsProps {
  session: SessionHandoff
  wsStatus: string
  baselineDeg: number
}

export function LiveReadings({ session, wsStatus, baselineDeg }: LiveReadingsProps) {
  const [elapsed, setElapsed] = useState(873)

  useEffect(() => {
    const id = setInterval(() => setElapsed(s => s + 1), 1000)
    return () => clearInterval(id)
  }, [])

  function fmtTime(sec: number) {
    const m = Math.floor(sec / 60)
    const s = (sec % 60).toString().padStart(2, '0')
    return `${m}:${s}`
  }

  const isLive    = wsStatus === 'open'
  const trunkWarn = session.trunkDeviation.finalRepsDeg > 8
  const romDelta  = session.medianPeakDeg - baselineDeg

  const metrics = [
    {
      label: 'Peak ROM',
      value: `${session.medianPeakDeg}°`,
      sub: `${romDelta >= 0 ? '+' : ''}${romDelta}° vs baseline`,
      warn: false,
    },
    {
      label: 'Trunk dev.',
      value: `${session.trunkDeviation.finalRepsDeg}°`,
      sub: trunkWarn ? '⚠ exceeds 8°' : 'within limit',
      warn: trunkWarn,
    },
    {
      label: 'Reps',
      value: `${session.reps.valid} / ${session.reps.attempted}`,
      sub: `${session.reps.prescribed} prescribed`,
      warn: false,
    },
    {
      label: 'Session time',
      value: fmtTime(elapsed),
      sub: 'elapsed',
      warn: false,
    },
  ]

  return (
    <div className="flex items-stretch border-b border-[#D1D9E3]">
      {/* Mode badge */}
      <div
        className={`flex items-center gap-2 px-3 shrink-0 border-r border-[#D1D9E3] ${isLive ? 'bg-[#155E30]' : 'bg-[#1A4A80]'}`}
        style={{ minWidth: 106 }}
      >
        <span className={`w-1.5 h-1.5 rounded-full shrink-0 ${isLive ? 'bg-[#4ADE80] animate-pulse' : 'bg-[#FBB040]'}`} />
        <div>
          <p className="text-white text-[9px] font-bold uppercase tracking-wider leading-none">VR Headset</p>
          <p className="text-white/60 text-[9px] leading-none mt-0.5">{isLive ? 'Live stream' : 'Replay mode'}</p>
        </div>
      </div>

      {/* Metric tiles */}
      <div className="flex flex-1 bg-white">
        {metrics.map((m, i) => (
          <div
            key={i}
            className={`flex flex-col justify-center px-4 py-2 border-r border-[#E8EDF2] last:border-r-0 ${m.warn ? 'bg-[#FFFBEB]' : ''}`}
          >
            <p className="text-[9px] text-[#6B7280] uppercase tracking-wider leading-none mb-1 select-none">{m.label}</p>
            <p className={`text-[20px] font-semibold tabular-nums leading-none ${m.warn ? 'text-[#C67C1A]' : 'text-[#111827]'}`}>
              {m.value}
            </p>
            <p className={`text-[9px] mt-1 leading-none ${m.warn ? 'text-[#C67C1A]' : 'text-[#9CA3AF]'}`}>{m.sub}</p>
          </div>
        ))}
      </div>
    </div>
  )
}
