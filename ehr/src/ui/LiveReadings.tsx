import type { SessionHandoff } from '../data/types'
import type { SessionOverride } from '../data/unityProtocol'

interface LiveReadingsProps {
  session: SessionHandoff        // seed / last-session fallback
  wsStatus: string
  baselineDeg: number
  override?: SessionOverride | null  // data from Unity REST API when available
}

export function LiveReadings({ session, wsStatus, baselineDeg, override }: LiveReadingsProps) {
  const isLive = wsStatus === 'open'

  // Prefer Unity-submitted data; fall back to seed when nothing has been received yet
  const romDeg    = override?.romDeg         ?? session.medianPeakDeg
  const trunkDeg  = override?.trunkDeg       ?? session.trunkDeviation.finalRepsDeg
  const repsValid = override?.repsValid      ?? session.reps.valid
  const repsAtt   = override?.repsAttempted  ?? session.reps.attempted
  const repsPrx   = override?.prescribedReps || session.reps.prescribed
  const worstPain = override?.painVas        ?? (
    session.patientReports.length
      ? Math.max(...session.patientReports.map(r => r.severity))
      : null
  )

  const romDelta  = romDeg - baselineDeg
  const trunkWarn = trunkDeg > 8

  const metrics = [
    {
      label: 'Peak ROM',
      value: `${romDeg}°`,
      sub: `${romDelta >= 0 ? '+' : ''}${romDelta}° vs baseline`,
      warn: false,
    },
    {
      label: 'Trunk dev.',
      value: `${trunkDeg}°`,
      sub: trunkWarn ? '⚠ exceeds 8°' : 'within limit',
      warn: trunkWarn,
    },
    {
      label: 'Reps',
      value: `${repsValid} / ${repsAtt}`,
      sub: `${repsPrx} prescribed`,
      warn: false,
    },
    {
      label: 'Pain (VAS)',
      value: worstPain !== null ? `${worstPain}/10` : '--',
      sub: worstPain !== null ? (worstPain >= 5 ? 'above threshold' : 'within limit') : 'no report',
      warn: worstPain !== null && worstPain >= 5,
    },
  ]

  return (
    <div className="flex items-stretch border-b border-[#D1D9E3]">
      {/* Mode badge */}
      <div
        className={`flex items-center px-3 shrink-0 border-r border-[#D1D9E3] ${isLive ? 'bg-[#155E30]' : 'bg-[#1A4A80]'}`}
        style={{ minWidth: 106 }}
      >
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
