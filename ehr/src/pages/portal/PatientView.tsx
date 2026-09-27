import { useState } from 'react'
import { useParams } from '@tanstack/react-router'
import { usePatientData } from '../../hooks/useLivePatient'
import { concerning } from '../../data/coordinator'
import { evaluateTrigger, computeNextSession } from '../../data/rules'
import { useLiveExercise } from '../../hooks/useLiveExercise'
import { LiveReadings } from '../../ui/LiveReadings'
import { SessionTrendChart } from '../../ui/SessionTrendChart'
import { VisitNotes } from '../../ui/VisitNotes'
import { ProgramUpdate } from '../../ui/ProgramUpdate'
import { CoachFeed } from '../../ui/CoachFeed'
import { ProgramTimelapse } from '../../ui/ProgramTimelapse'
import { PatientRelay } from '../../ui/PatientRelay'
import type { PatientStatus } from '../../data/types'

// ── Status badge styles ───────────────────────────────────────────────────────
const statusBadge: Record<PatientStatus, { label: string; cls: string }> = {
  alert: { label: 'Needs Review', cls: 'bg-[#C62828] text-white' },
  watch: { label: 'Monitor',      cls: 'bg-[#FEF3C7] text-[#92400E] border border-[#FDE68A]' },
  good:  { label: 'On Track',     cls: 'bg-[#D1FAE5] text-[#065F46] border border-[#A7F3D0]' },
}

// ── Per-patient goals & precautions ──────────────────────────────────────────
const GOALS: Record<string, { short: string; shortTarget: string; long: string; longTarget: string }> = {
  'marcus-r': {
    short:       'Peak ROM ≥ 90° with trunk deviation < 8°',
    shortTarget: 'Session 18 · 09/28/25',
    long:        'Functional ROM ≥ 110°, resume independent ADLs',
    longTarget:  'Wk 10 · 10/22/25',
  },
  'james-t': {
    short:       'Pain-free ROM ≥ 80° at shoulder abduction',
    shortTarget: 'Session 10 · 10/01/25',
    long:        'Return to overhead activity, strength symmetry ≥ 85%',
    longTarget:  'Wk 12 · 11/05/25',
  },
  'sarah-k': {
    short:       'Full knee extension (0–120°), trunk compensation < 2°',
    shortTarget: 'Session 12 · 09/29/25',
    long:        'Return to sport, hop test limb symmetry ≥ 90%',
    longTarget:  'Wk 6 · 10/13/25',
  },
  'elena-v': {
    short:       'Tandem stance hold ≥ 12 s, sway < 5°',
    shortTarget: 'Session 25 · 10/06/25',
    long:        'Reduce fall risk to low, community ambulation independent',
    longTarget:  'Wk 20 · 11/19/25',
  },
}

const PRECAUTIONS: Record<string, string[]> = {
  'marcus-r': [
    'Monitor trunk deviation - stop if > 10° sustained',
    'No overhead lifting outside supervised sessions',
    'Skin integrity check required prior to each session',
  ],
  'james-t': [
    'No external rotation beyond 30° until wk 8',
    'Pain > 5/10 → reduce ROM target, notify physician',
    'Ice 15 min post-session',
  ],
  'sarah-k': [
    'No cutting or pivoting until cleared by physician',
    'Avoid full squat depth until wk 10',
    'Report locking or giving-way immediately',
  ],
  'elena-v': [
    'Fall precaution - balance tasks near support surface only',
    'Notify caregiver of session schedule',
    'Monitor medication timing relative to session start',
  ],
}

const RTM_DAYS_THRESHOLD    = 16
const RTM_MINUTES_THRESHOLD = 20

// ── Helpers ───────────────────────────────────────────────────────────────────
function calcAge(dob: string): number {
  const today = new Date()
  const birth = new Date(dob)
  let age = today.getFullYear() - birth.getFullYear()
  if (today.getMonth() - birth.getMonth() < 0 ||
     (today.getMonth() === birth.getMonth() && today.getDate() < birth.getDate())) age--
  return age
}

function fmtDob(dob: string): string {
  const [y, m, d] = dob.split('-')
  return `${m}/${d}/${y.slice(2)}`
}

function fmtTs(iso: string): string {
  const d = new Date(iso)
  const mo = String(d.getMonth() + 1).padStart(2, '0')
  const dy = String(d.getDate()).padStart(2, '0')
  const yr = String(d.getFullYear()).slice(2)
  const hr = String(d.getHours()).padStart(2, '0')
  const mn = String(d.getMinutes()).padStart(2, '0')
  return `${mo}/${dy}/${yr} ${hr}:${mn}`
}

// ── Shared UI primitives ──────────────────────────────────────────────────────
function SectionLabel({ label }: { label: string }) {
  return (
    <div className="px-3 pt-3 pb-[5px] border-b border-[#e8e0d8]">
      <span
        className="text-[10px] font-bold uppercase tracking-[0.18em] select-none"
        style={{ color: '#a0522d' }}
      >
        {label}
      </span>
    </div>
  )
}

function RailLabel({ label }: { label: string }) {
  return (
    <div className="px-3 pt-2 pb-[4px]">
      <span
        className="text-[9px] font-bold uppercase tracking-[0.18em] select-none"
        style={{ color: '#a0522d' }}
      >
        {label}
      </span>
    </div>
  )
}

function ProgressBar({ value, max, color = '#1666C0' }: { value: number; max: number; color?: string }) {
  const pct = Math.min(100, Math.round((value / max) * 100))
  return (
    <div className="h-[3px] bg-[#E2E8F0] rounded-full overflow-hidden mt-1">
      <div className="h-full rounded-full" style={{ width: `${pct}%`, backgroundColor: color }} />
    </div>
  )
}

const TABS       = ['Summary', 'Flowsheet', 'Session Log', 'Expert', 'Plan']
const RIGHT_TABS = ['Overview', 'Goals', 'RTM']

// ── Main component ────────────────────────────────────────────────────────────
export function PatientView() {
  const { patientId } = useParams({ from: '/portal/$patientId' })
  // Live for the live patient, seed for the seeded ones. Every component below this line reads the same
  // PatientData shape either way - src/data/coordinator.ts is what makes the real records look like it.
  const { data, live, offline } = usePatientData(patientId)
  // The set happening right now on the headset, rep by rep (coordinator /exercise): the live patient's readings
  // update as each rep lands, and the badge goes LIVE.
  const liveSet = useLiveExercise()
  const wsStatus = live && liveSet.running ? 'open' : 'closed'
  const [activeTab, setActiveTab] = useState('Summary')
  const [rightTab, setRightTab]   = useState('Overview')

  if (!data) {
    return (
      <div className="flex items-center justify-center h-full min-h-screen bg-[#E8EDF2]">
        <p className="text-[#6B7280] text-sm">
          {live
            ? 'Reading this patient from the coordinator…'
            : 'Patient not found.'}
        </p>
      </div>
    )
  }

  const { patient, sessions, latestHandoff: session, plans, rtm } = data
  const trigger     = evaluateTrigger(sessions, session)
  const nextSession = computeNextSession(sessions)
  const isLive      = wsStatus === 'open'
  const badge       = statusBadge[patient.status]
  const age         = calcAge(patient.dob)
  const goals       = GOALS[patient.id]
  const precautions = PRECAUTIONS[patient.id] ?? []
  const baselineDeg = sessions[0]?.medianPeakDeg ?? 0
  const concerningReplies = concerning(data.replies)

  const trunkLimit = (() => {
    const s = plans[0]?.settings.find(p => p.setting === 'Trunk limit')
    return typeof s?.current === 'number' ? s.current : 8
  })()

  const targetBand = (() => {
    const s = plans[0]?.settings.find(p => p.setting === 'Target ROM band')
    if (typeof s?.current === 'string') {
      const [lo, hi] = s.current.replace(/°/g, '').split('–').map(Number)
      return { low: lo ?? 68, high: hi ?? 90 }
    }
    return { low: 68, high: 90 }
  })()

  const worstSymptom = session.patientReports.length
    ? session.patientReports.reduce((a, b) => a.severity > b.severity ? a : b)
    : null

  const measureRows = [
    {
      measure: 'Range of motion',
      value:   `${session.medianPeakDeg}°`,
      ref:     `Target ${targetBand.low}–${targetBand.high}°`,
      src:     'Measured · VR',
      warn:    false,
    },
    {
      measure: 'Valid / attempted reps',
      value:   `${session.reps.valid} / ${session.reps.attempted}`,
      ref:     `${session.reps.prescribed} prescribed`,
      src:     'Measured · VR',
      warn:    false,
    },
    {
      measure: 'Trunk deviation, final reps',
      value:   `${session.trunkDeviation.finalRepsDeg > trunkLimit ? '⚠ ' : ''}${session.trunkDeviation.finalRepsDeg}°`,
      ref:     `Limit ${trunkLimit}° (plan v${session.planVersion})`,
      src:     'Measured · VR',
      warn:    session.trunkDeviation.finalRepsDeg > trunkLimit,
    },
    ...(worstSymptom ? [{
      measure: 'Patient-reported symptom',
      value:   `${worstSymptom.severity}/10`,
      ref:     `"${worstSymptom.text}"`,
      src:     'Patient report',
      warn:    worstSymptom.severity >= 5,
    }] : []),
  ]

  return (
    <div className="min-h-screen bg-[#E8EDF2] flex flex-col">

      {/* ── Patient Banner ──────────────────────────────────────────── */}
      <div className="bg-white border-b border-[#D1D9E3] px-4 py-3 flex items-start gap-3 shrink-0">

        {/* Square photo box */}
        <div className="w-16 h-16 rounded-[3px] overflow-hidden border border-[#C8D4E0] shrink-0 bg-[#D4E2EE] flex items-center justify-center">
          {patient.photoUrl
            ? <img src={patient.photoUrl} alt={patient.name} className="w-full h-full object-cover" />
            : <span className="text-[#3A6A98] font-bold text-[18px] select-none">
                {patient.name.split(' ').map(n => n[0]).join('').slice(0, 2).toUpperCase()}
              </span>
          }
        </div>

        {/* Identity block */}
        <div className="flex-1 min-w-0">
          {/* Name + status row */}
          <div className="flex items-center gap-2 mb-2">
            <span className="text-[#111827] text-[17px] font-bold leading-none">{patient.name}</span>
            <span className={`text-[10px] font-semibold px-2 py-[2px] rounded-[2px] ${badge.cls}`}>
              {badge.label}
            </span>
            {/* Where these numbers come from. Three separate facts, and the page says which it is showing:
                seeded patients are invented, a live patient is read from the coordinator every few seconds,
                and the websocket is only up while an exercise is actually running on the headset. */}
            <span className={`flex items-center gap-1 text-[10px] ${
              !live ? 'text-[#6B7280]'
                : offline ? 'text-[#92400E]'
                : isLive ? 'text-[#166534]'
                : 'text-[#0E7490]'
            }`}>
              <span className={`w-1.5 h-1.5 rounded-full ${
                !live ? 'bg-[#9CA3AF]'
                  : offline ? 'bg-[#FBB040]'
                  : isLive ? 'bg-[#4ADE80] animate-pulse'
                  : 'bg-[#22B8CF]'
              }`} />
              {!live ? 'Demo patient'
                : offline ? 'Coordinator offline'
                : isLive ? 'Live · session in progress'
                : 'Live · from coordinator'}
            </span>
          </div>

          {/* Tinted info tiles */}
          <div className="flex flex-wrap gap-1.5">
            {[
              { label: 'DOB',    value: patient.dob ? `${fmtDob(patient.dob)} · ${age} y/o ${patient.sex}` : 'Not on file', bg: '#EEF4FC', fg: '#1A3A6A' },
              { label: 'MRN',    value: patient.mrn,                   bg: '#F0F4F8', fg: '#374151' },
              { label: 'ICD-10', value: patient.icd10,                 bg: '#FEF9F0', fg: '#854D0E' },
              { label: 'Ref.',   value: patient.referringPhysician,    bg: '#F0F4F8', fg: '#374151' },
              { label: 'Injury', value: patient.dateOfInjury,          bg: '#F0F4F8', fg: '#374151' },
              { label: 'Plan',   value: `v${patient.activePlanVersion} · ${patient.weeksActive} wks`, bg: '#EEF4FC', fg: '#1A3A6A' },
            ].map(tile => (
              <div
                key={tile.label}
                className="flex items-center gap-1 px-2 py-[3px] rounded-[2px]"
                style={{ backgroundColor: tile.bg }}
              >
                <span className="text-[9px] font-semibold uppercase tracking-wider text-[#6B7280]">{tile.label}</span>
                <span className="text-[11px] font-medium" style={{ color: tile.fg }}>{tile.value}</span>
              </div>
            ))}
          </div>
        </div>
      </div>

      {/* ── Tab strip ───────────────────────────────────────────────── */}
      <div className="bg-[#1a56a0] flex items-center shrink-0">
        {TABS.map(tab => (
          <button
            key={tab}
            onClick={() => setActiveTab(tab)}
            className={[
              'px-5 py-[6px] text-[12px] font-medium border-r border-white/10 transition-colors cursor-pointer shrink-0',
              activeTab === tab ? 'bg-white text-[#1a56a0]' : 'text-[#b8d0ec] hover:bg-white/10 hover:text-white',
            ].join(' ')}
          >
            {tab}
          </button>
        ))}
        <div className="flex-1" />
        <span className="text-[#7BA3C8] text-[10px] px-4 shrink-0">
          Last reviewed Dr. Chen · {fmtTs('2025-09-24T16:05:00Z')} · {session.sessionId}
        </span>
      </div>

      {/* ── Toolbar ─────────────────────────────────────────────────── */}
      <div className="bg-[#f1f3f5] border-b border-[#d5dae0] flex items-center px-3 py-1.5 gap-2 shrink-0">
        <span className="text-[11px] text-[#4B5563] font-medium">{session.exercise}</span>
        <div className="flex-1" />
        {[
          { icon: '▶', label: 'Watch Replay'  },
          { icon: '◎', label: 'Ask Agent'     },
          { icon: '✦', label: 'Draft Plan'    },
          { icon: '≡', label: 'Audit'         },
        ].map(btn => (
          <button
            key={btn.label}
            onClick={btn.label === 'Draft Plan' ? () => document.getElementById('program-update')?.scrollIntoView({ behavior: 'smooth', block: 'center' }) : undefined}
            className="flex items-center gap-1 px-2.5 py-[4px] border border-[#c8cdd4] bg-white text-[11px] text-[#374151] rounded-[2px] hover:bg-[#e6eaef] hover:text-[#1666C0] transition-colors cursor-pointer"
          >
            <span className="text-[#1666C0] text-[10px]">{btn.icon}</span>
            {btn.label}
          </button>
        ))}
      </div>

      {/* ── Body ────────────────────────────────────────────────────── */}
      <div className="flex flex-1">

        {/* ── Center column ──────────────────────────────────────────── */}
        <div className="flex-1 min-w-0 bg-white border-r border-[#D1D9E3]">

          {/* What the patient said. Above the clinical alert on purpose: the rule below is a machine reading
              sensor output, and this is the patient's own account. When they disagree, the physician should
              have read this first. */}
          {concerningReplies.length > 0 && (
            <>
              <SectionLabel label="From the Patient" />
              <div className="px-3 pt-2">
                <PatientRelay replies={concerningReplies} />
              </div>
            </>
          )}

          {/* Live VR readings */}
          <SectionLabel label="Current Session" />
          <LiveReadings session={session} wsStatus={wsStatus} baselineDeg={baselineDeg}
            override={live && liveSet.running ? liveSet.override : null}
            now={live && liveSet.running ? [liveSet.angleDeg != null ? `arm ${liveSet.angleDeg}°` : null, liveSet.lastRep].filter(Boolean).join(' · ') : null} />

          {/* The program so far, replayed: reach per session, the target at each plan version, what the patient said. */}
          <SectionLabel label="Program Timelapse" />
          <div className="px-3 py-3">
            <ProgramTimelapse sessions={sessions} plans={plans} replies={data.replies} />
          </div>

          {/* Clinical alert */}
          {trigger.fired && (
            <>
              <SectionLabel label="Clinical Alert" />
              <div className="mx-3 my-2 border-l-4 border-[#C67C1A] bg-[#FFFBEB] px-3 py-2 rounded-r-[2px]">
                <p className="text-[12px] font-semibold text-[#111827] mb-1.5">
                  Functional change since last review.
                </p>
                <div className="flex flex-col gap-1 mb-1.5">
                  {trigger.conditions.map(c => (
                    <p key={c.id} className={`text-[11px] leading-snug ${c.met ? 'text-[#166534]' : 'text-[#991B1B]'}`}>
                      {c.displayLine}
                    </p>
                  ))}
                </div>
                <p className="text-[10px] text-[#6B7280] italic">{trigger.summary}</p>
              </div>
            </>
          )}

          {/* Key measures */}
          <SectionLabel label={`Key Measures · ${fmtTs(session.timestamp)}`} />
          <table className="w-full border-collapse">
            <thead>
              <tr style={{ backgroundColor: '#f1f3f5' }}>
                {['Measure', 'Value', 'Reference', 'Source'].map(h => (
                  <th
                    key={h}
                    className="text-left text-[10px] text-[#6B7280] uppercase tracking-wider px-3 py-[4px] border-b border-r border-[#D1D9E3] last:border-r-0 font-semibold"
                  >
                    {h}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {measureRows.map((row, i) => (
                <tr
                  key={i}
                  className="border-b border-[#E8EDF2] hover:bg-[#F5F8FC]"
                  style={{ backgroundColor: i % 2 === 0 ? '#ffffff' : '#f7f9fb' }}
                >
                  <td className="px-3 py-[5px] text-[11px] text-[#374151] border-r border-[#E8EDF2]">{row.measure}</td>
                  <td className={`px-3 py-[5px] text-[12px] font-semibold border-r border-[#E8EDF2] tabular-nums ${row.warn ? 'text-[#C67C1A]' : 'text-[#111827]'}`}>
                    {row.value}
                  </td>
                  <td className="px-3 py-[5px] text-[11px] text-[#6B7280] border-r border-[#E8EDF2]">{row.ref}</td>
                  <td className="px-3 py-[5px] text-[11px] text-[#6B7280]">{row.src}</td>
                </tr>
              ))}
            </tbody>
          </table>

          {/* ROM + trunk trend chart */}
          <SectionLabel label="ROM & Trunk Deviation - All Sessions" />
          <div className="px-3 pt-2 pb-4">
            <SessionTrendChart
              data={sessions}
              targetLow={targetBand.low}
              targetHigh={targetBand.high}
              trunkLimit={trunkLimit}
            />
          </div>

          {/* Session history */}
          <SectionLabel label="Session History" />
          <table className="w-full border-collapse">
            <thead>
              <tr style={{ backgroundColor: '#f1f3f5' }}>
                {['#', 'Date', 'Valid / Att.', 'Peak ROM', 'Trunk (mean)', 'Source'].map(h => (
                  <th
                    key={h}
                    className="text-left text-[10px] text-[#6B7280] uppercase tracking-wider px-3 py-[4px] border-b border-r border-[#D1D9E3] last:border-r-0 font-semibold whitespace-nowrap"
                  >
                    {h}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {[...sessions].reverse().slice(0, 8).map((s, i) => (
                <tr
                  key={s.session}
                  className="border-b border-[#E8EDF2] hover:bg-[#F5F8FC]"
                  style={{ backgroundColor: i % 2 === 0 ? '#ffffff' : '#f7f9fb' }}
                >
                  <td className="px-3 py-[4px] text-[11px] text-[#6B7280] border-r border-[#E8EDF2] tabular-nums">{s.session}</td>
                  <td className="px-3 py-[4px] text-[11px] text-[#374151] border-r border-[#E8EDF2] tabular-nums whitespace-nowrap">{s.date}</td>
                  <td className="px-3 py-[4px] text-[11px] font-medium text-[#111827] border-r border-[#E8EDF2] tabular-nums">{s.validReps} / {s.prescribedReps}</td>
                  <td className="px-3 py-[4px] text-[11px] font-medium text-[#111827] border-r border-[#E8EDF2] tabular-nums">{s.medianPeakDeg}°</td>
                  <td className={`px-3 py-[4px] text-[11px] font-medium border-r border-[#E8EDF2] tabular-nums ${s.trunkMeanDeg > trunkLimit ? 'text-[#C67C1A]' : 'text-[#111827]'}`}>
                    {s.trunkMeanDeg > trunkLimit && '⚠ '}{s.trunkMeanDeg}°
                  </td>
                  <td className="px-3 py-[4px] text-[11px] text-[#6B7280]">
                    {s.completed ? (s.synthetic ? 'VR · Simulated' : 'VR · Live') : 'Missed'}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>

          {/* What the coach told the patient, beside the measurements it coached from. */}
          <SectionLabel label="Coach Feed" />
          <div className="px-3 py-3">
            <CoachFeed log={session.coachLog} />
          </div>

          {/* The physician changes the live patient's program: a signed plan version the headset picks up. */}
          {live && (<>
            <SectionLabel label="Program Update" />
            <div className="px-3 py-3">
              <ProgramUpdate />
            </div>
          </>)}

          {/* Visit whiteboard: notes for the patient's therapist visit */}
          <SectionLabel label="Visit Whiteboard" />
          <div className="px-3 py-3">
            <VisitNotes />
          </div>
        </div>

        {/* ── Right Reference Panel ───────────────────────────────────── */}
        <div className="shrink-0 bg-[#f7f8fa] flex flex-col border-l border-[#D1D9E3]" style={{ width: 220 }}>

          {/* Mini tab strip */}
          <div className="flex border-b border-[#d5dae0] bg-[#e6eaef] shrink-0">
            {RIGHT_TABS.map(t => (
              <button
                key={t}
                onClick={() => setRightTab(t)}
                className={[
                  'flex-1 text-[11px] py-[5px] font-medium cursor-pointer transition-colors border-r border-[#d5dae0] last:border-r-0',
                  rightTab === t
                    ? 'bg-white text-[#1666C0] shadow-[inset_0_-2px_0_#1666C0]'
                    : 'text-[#6B7280] hover:text-[#374151] hover:bg-[#dde2e8]',
                ].join(' ')}
              >
                {t}
              </button>
            ))}
          </div>

          {/* Overview tab */}
          {rightTab === 'Overview' && (
            <div className="flex-1 overflow-y-auto">
              <RailLabel label="Patient Info" />
              {[
                { label: 'Condition',   value: patient.condition },
                { label: 'Next session', value: nextSession },
                { label: 'Sessions',    value: String(patient.sessionCount) },
                { label: 'Weeks active', value: String(patient.weeksActive) },
              ].map(row => (
                <div key={row.label} className="flex border-b border-[#e0e4e9] px-3" style={{ minHeight: 26 }}>
                  <span className="text-[10px] text-[#6B7280] shrink-0 py-[4px]" style={{ width: 84 }}>{row.label}</span>
                  <span className="text-[11px] font-medium text-[#111827] py-[4px] truncate">{row.value}</span>
                </div>
              ))}

              {precautions.length > 0 && (
                <>
                  <RailLabel label="Precautions" />
                  <div className="px-3 pb-2">
                    {precautions.map((p, i) => (
                      <div
                        key={i}
                        className={`flex gap-1.5 text-[11px] text-[#374151] leading-snug ${i > 0 ? 'mt-2 pt-2 border-t border-[#e0e4e9]' : ''}`}
                      >
                        <span className="text-[#C67C1A] shrink-0">⚠</span>
                        {p}
                      </div>
                    ))}
                  </div>
                </>
              )}
            </div>
          )}

          {/* Goals tab */}
          {rightTab === 'Goals' && goals && (
            <div className="flex-1 overflow-y-auto">
              <RailLabel label="Short-term Goal" />
              <div className="px-3 pb-3 border-b border-[#e0e4e9]">
                <p className="text-[11px] text-[#111827] leading-snug mb-1.5">{goals.short}</p>
                <ProgressBar value={session.medianPeakDeg} max={targetBand.high} color="#1666C0" />
                <p className="text-[10px] text-[#9CA3AF] mt-1">{goals.shortTarget}</p>
              </div>
              <RailLabel label="Long-term Goal" />
              <div className="px-3 pb-3">
                <p className="text-[11px] text-[#111827] leading-snug mb-1.5">{goals.long}</p>
                <ProgressBar value={session.medianPeakDeg} max={targetBand.high + 15} color="#1666C0" />
                <p className="text-[10px] text-[#9CA3AF] mt-1">{goals.longTarget}</p>
              </div>
            </div>
          )}

          {/* RTM tab */}
          {rightTab === 'RTM' && (
            <div className="flex-1 overflow-y-auto">
              <RailLabel label="RTM Monitoring (CPT 98975-98977)" />
              <div className="px-3 py-2 border-b border-[#e0e4e9]">
                <div className="flex justify-between items-baseline mb-1">
                  <span className="text-[10px] text-[#6B7280] leading-snug pr-1">Days with data</span>
                  <span className="text-[11px] font-semibold tabular-nums text-[#111827] shrink-0">
                    {rtm.daysWithData} / {RTM_DAYS_THRESHOLD}
                  </span>
                </div>
                <ProgressBar
                  value={rtm.daysWithData}
                  max={RTM_DAYS_THRESHOLD}
                  color={rtm.daysWithData >= RTM_DAYS_THRESHOLD ? '#166534' : '#1666C0'}
                />
                <p className="text-[9px] text-[#9CA3AF] mt-0.5">Threshold: {RTM_DAYS_THRESHOLD} days / 30-day period</p>
              </div>
              <div className="px-3 py-2 border-b border-[#e0e4e9]">
                <div className="flex justify-between items-baseline mb-1">
                  <span className="text-[10px] text-[#6B7280] leading-snug pr-1">Clinician review time</span>
                  <span className="text-[11px] font-semibold tabular-nums text-[#111827] shrink-0">
                    {rtm.reviewMinutes} / {RTM_MINUTES_THRESHOLD} min
                  </span>
                </div>
                <ProgressBar
                  value={rtm.reviewMinutes}
                  max={RTM_MINUTES_THRESHOLD}
                  color={rtm.reviewMinutes >= RTM_MINUTES_THRESHOLD ? '#166534' : '#1666C0'}
                />
                <p className="text-[9px] text-[#9CA3AF] mt-0.5">Threshold: {RTM_MINUTES_THRESHOLD} min / month</p>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
