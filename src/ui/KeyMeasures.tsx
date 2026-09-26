import { useState } from 'react'
import type { SessionHandoff } from '../data/types'
import { VALIDATION } from '../data/validationConfig'
import type { ValidationEntry } from '../data/validationConfig'

type SourceTag = 'Measured' | 'Patient report' | 'Agent' | 'Clinician'

const sourceStyle: Record<SourceTag, { color: string; bg: string }> = {
  'Measured':       { color: '#1666C0', bg: '#1666C0' },
  'Patient report': { color: '#C67C1A', bg: '#C67C1A' },
  'Agent':          { color: '#0D9488', bg: '#0D9488' },
  'Clinician':      { color: '#2E7D32', bg: '#2E7D32' },
}

function SourceBadge({ source }: { source: SourceTag }) {
  const s = sourceStyle[source]
  return (
    <span
      className="text-[8px] uppercase tracking-widest px-1.5 py-0.5 font-medium rounded-sm"
      style={{ color: s.color, backgroundColor: `${s.bg}14`, border: `1px solid ${s.bg}30` }}
    >
      {source}
    </span>
  )
}

function InfoPopover({
  entry,
  occlusionPct,
  onClose,
}: {
  entry: ValidationEntry
  occlusionPct?: number
  onClose: () => void
}) {
  return (
    <div className="absolute top-full left-0 right-0 z-50 mt-1 bg-white border border-[#DDE2E8] rounded-md p-3 shadow-lg">
      <div className="flex items-center justify-between mb-2">
        <p className="text-[9px] text-[#5A6472] uppercase tracking-widest">Measurement method</p>
        <button onClick={onClose} className="text-[#5A6472] hover:text-[#1A1D23] text-sm leading-none">×</button>
      </div>
      <div className="flex flex-col gap-0.5 text-[10px] font-mono">
        <p className="text-[#1A1D23]">{entry.method}</p>
        <p className="text-[#5A6472]">Version: {entry.algorithmVersion}</p>
        <p className="text-[#5A6472]">Cal ID: {entry.calibrationId}</p>
        {occlusionPct !== undefined && (
          <p className={occlusionPct > 10 ? 'text-[#C67C1A]' : 'text-[#2E7D32]'}>
            Occlusion: {occlusionPct}% of frames
          </p>
        )}
        {entry.goniometerDeltaDeg !== undefined ? (
          <p className="text-[#2E7D32]">
            Validated vs goniometer: ±{entry.goniometerDeltaDeg}° (n={entry.goniometerTrials} trials)
          </p>
        ) : (
          <p className="text-[#C67C1A]">Not yet validated vs goniometer</p>
        )}
      </div>
    </div>
  )
}

interface MeasureProps {
  label: string
  value: string | number
  unit?: string
  subtitle?: string
  warn?: boolean
  source: SourceTag
  validation?: ValidationEntry
  occlusionPct?: number
}

function Measure({ label, value, unit = '', subtitle, warn = false, source, validation, occlusionPct }: MeasureProps) {
  const [showInfo, setShowInfo] = useState(false)

  return (
    <div className="bg-white border border-[#DDE2E8] rounded-md p-4 relative shadow-sm">
      <div className="flex items-center justify-between mb-2 gap-2">
        <p className="text-[#5A6472] text-[9px] uppercase tracking-[0.18em] leading-none">{label}</p>
        <div className="flex items-center gap-1.5 shrink-0">
          <SourceBadge source={source} />
          {validation && (
            <button
              onClick={() => setShowInfo(o => !o)}
              className="text-[#5A6472] hover:text-[#1666C0] transition-colors text-[11px] leading-none"
              title="Measurement method"
            >
              ⓘ
            </button>
          )}
        </div>
      </div>

      <p className={`font-mono text-xl font-bold mb-1 ${warn ? 'text-[#C67C1A]' : 'text-[#1A1D23]'}`}>
        {value}{unit}
      </p>
      {subtitle && (
        <p className="text-[#5A6472] text-[10px] font-mono leading-snug">{subtitle}</p>
      )}

      {showInfo && validation && (
        <InfoPopover
          entry={validation}
          occlusionPct={occlusionPct}
          onClose={() => setShowInfo(false)}
        />
      )}
    </div>
  )
}

interface KeyMeasuresProps {
  session: SessionHandoff
  calibrationBaselineDeg: number
  trunkLimit: number
  occlusionPct?: number
}

export function KeyMeasures({
  session,
  calibrationBaselineDeg,
  trunkLimit,
  occlusionPct = 12,
}: KeyMeasuresProps) {
  const { reps, medianPeakDeg, trunkDeviation, patientReports } = session
  const romDelta = medianPeakDeg - calibrationBaselineDeg

  const worstSymptom = patientReports.length
    ? patientReports.reduce((a, b) => a.severity > b.severity ? a : b)
    : null

  function fmtTime(sec: number) {
    const m = Math.floor(sec / 60)
    const s = Math.floor(sec % 60).toString().padStart(2, '0')
    return `${m}:${s}`
  }

  return (
    <div>
      <p className="text-[#5A6472] text-[10px] uppercase tracking-[0.18em] mb-2">Key Measures</p>
      <div className="grid grid-cols-4 gap-3">
        <Measure
          label="Range of motion"
          value={medianPeakDeg}
          unit="°"
          subtitle={`vs baseline ${calibrationBaselineDeg}°: +${romDelta}°`}
          source="Measured"
          validation={VALIDATION.rom}
          occlusionPct={occlusionPct}
        />
        <Measure
          label="Valid / attempted"
          value={`${reps.valid} / ${reps.attempted}`}
          subtitle={`${reps.valid} valid of ${reps.attempted} attempted · ${reps.prescribed} prescribed`}
          source="Measured"
          validation={VALIDATION.repCount}
        />
        <Measure
          label="Trunk deviation"
          value={trunkDeviation.finalRepsDeg}
          unit="°"
          subtitle={`limit ${trunkLimit}° · session mean ${trunkDeviation.meanDeg}°`}
          warn={trunkDeviation.finalRepsDeg > trunkLimit}
          source="Measured"
          validation={VALIDATION.trunk}
          occlusionPct={occlusionPct}
        />
        <Measure
          label="Patient-reported"
          value={worstSymptom ? `${worstSymptom.severity}/10` : '—'}
          subtitle={
            worstSymptom
              ? `reported at ${fmtTime(worstSymptom.t)} · "${worstSymptom.text}"`
              : undefined
          }
          warn={!!worstSymptom && worstSymptom.severity >= 5}
          source="Patient report"
        />
      </div>
    </div>
  )
}
