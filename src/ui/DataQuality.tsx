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
    <div className="bg-white border border-[#DDE2E8] rounded-md p-4 shadow-sm">
      <p className="text-[#5A6472] text-[9px] uppercase tracking-[0.18em] mb-3">Data quality</p>

      <div className="grid grid-cols-2 gap-3">
        <div>
          <p className="text-[#5A6472] text-[9px] uppercase tracking-widest mb-2">Session flags</p>
          <div className="flex flex-col gap-1">
            <div className="flex items-center justify-between px-3 py-1.5 bg-[#F4F6F8] border border-[#DDE2E8] rounded-sm">
              <span className="text-[10px] text-[#5A6472]">Occlusion rate</span>
              <span className={`font-mono text-[10px] ${occlusionPct > 10 ? 'text-[#C67C1A]' : 'text-[#2E7D32]'}`}>
                {occlusionPct}%
              </span>
            </div>
            {uncertainty.map((u, i) => (
              <div key={i} className="px-3 py-1.5 bg-[#FFF8F0] border border-[#C67C1A]/20 rounded-sm">
                <p className="text-[10px] text-[#C67C1A]">⚠ {u}</p>
              </div>
            ))}
          </div>
        </div>

        <div>
          <p className="text-[#5A6472] text-[9px] uppercase tracking-widest mb-2">Not measured by sensors</p>
          <div className="flex flex-col gap-1">
            {SENSOR_LIMITS.map((l, i) => (
              <div key={i} className="px-3 py-1.5 bg-[#F4F6F8] border border-[#DDE2E8] rounded-sm">
                <p className="text-[10px] text-[#5A6472]">— {l}</p>
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  )
}
