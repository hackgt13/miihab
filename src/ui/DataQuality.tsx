interface DataQualityProps {
  uncertainty: string[]
  occlusionPct?: number
}

const SENSOR_LIMITS = [
  'Spasticity — requires clinical assessment',
  'Muscular strength — not measured',
  'Pain — patient-reported only',
  'Passive ROM — not captured',
]

export function DataQuality({ uncertainty, occlusionPct = 12 }: DataQualityProps) {
  return (
    <div className="bg-[#2A3337] border border-[#3D484E] p-4">
      <p className="text-[#A3B0B6] text-[9px] uppercase tracking-[0.18em] mb-3">Data quality</p>

      <div className="grid grid-cols-2 gap-3">
        <div>
          <p className="text-[#A3B0B6] text-[9px] uppercase tracking-widest mb-2">Session flags</p>
          <div className="flex flex-col gap-1">
            <div className="flex items-center justify-between px-3 py-1.5 bg-[#232B2F] border border-[#3D484E]">
              <span className="text-[10px] text-[#A3B0B6]">Occlusion rate</span>
              <span className={`font-mono text-[10px] ${occlusionPct > 10 ? 'text-[#E3A86B]' : 'text-[#7CC49A]'}`}>
                {occlusionPct}%
              </span>
            </div>
            {uncertainty.map((u, i) => (
              <div key={i} className="px-3 py-1.5 bg-[#232B2F] border border-[#E3A86B]/20">
                <p className="text-[10px] text-[#E3A86B]">⚠ {u}</p>
              </div>
            ))}
          </div>
        </div>

        <div>
          <p className="text-[#A3B0B6] text-[9px] uppercase tracking-widest mb-2">Not measured by sensors</p>
          <div className="flex flex-col gap-1">
            {SENSOR_LIMITS.map((l, i) => (
              <div key={i} className="px-3 py-1.5 bg-[#232B2F] border border-[#3D484E]">
                <p className="text-[10px] text-[#A3B0B6]">— {l}</p>
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  )
}
