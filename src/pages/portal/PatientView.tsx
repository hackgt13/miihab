import { useRef, useState } from 'react'
import { useParams } from '@tanstack/react-router'
import { getPatientData } from '../../data/seed'
import { evaluateTrigger, computeNextSession } from '../../data/rules'
import { useSessionSocket } from '../../hooks/useSessionSocket'
import { TriggerCard } from '../../ui/TriggerCard'
import { KeyMeasures } from '../../ui/KeyMeasures'
import { ROMChart, CompensationChart } from '../../ui/TrendChart'
import { ReplayMoments } from '../../ui/ReplayMoments'
import { CoachFeed } from '../../ui/CoachFeed'
import { ExpertChat } from '../../ui/ExpertChat'
import { PlanVersioning } from '../../ui/PlanVersioning'
import { DataQuality } from '../../ui/DataQuality'
import { Adherence } from '../../ui/Adherence'
import { VisitNotes } from '../../ui/VisitNotes'
import type { PatientStatus } from '../../data/types'

const statusColor: Record<PatientStatus, string> = {
  alert: '#E08585',
  watch: '#E3A86B',
  good:  '#7CC49A',
}
const statusLabel: Record<PatientStatus, string> = {
  alert: 'Needs review', watch: 'Monitor', good: 'On track',
}

export function PatientView() {
  const { patientId } = useParams({ from: '/portal/$patientId' })
  const data = getPatientData(patientId)

  const { status: wsStatus } = useSessionSocket({ url: 'ws://localhost:8766', enabled: true })

  // Scroll refs for trigger action buttons
  const replayRef  = useRef<HTMLDivElement>(null)
  const expertRef  = useRef<HTMLDivElement>(null)
  const planRef    = useRef<HTMLDivElement>(null)

  // Expert panel preload context (set when "Ask agent" fires from trigger card)
  const [expertContext, setExpertContext] = useState<string | null>(null)

  if (!data) {
    return (
      <div className="flex items-center justify-center h-full min-h-screen">
        <p className="text-[#A3B0B6] text-sm">Patient not found.</p>
      </div>
    )
  }

  const { patient, sessions, latestHandoff: session, schedule, plans, rtm } = data

  // ── Computed values ──────────────────────────────────────────────────────
  // Single source of truth: rule evaluated from actual data
  const trigger = evaluateTrigger(sessions, session)

  // Baseline = session 1 calibration peak (not the trigger comparison window)
  const calibrationBaselineDeg = sessions[0]?.medianPeakDeg ?? 0

  // Trunk limit from the active plan
  const trunkLimit = (() => {
    const s = plans[0]?.settings.find(p => p.setting === 'Trunk limit')
    return typeof s?.current === 'number' ? s.current : 8
  })()

  // Target band from active plan (for chart reference lines)
  const targetBand = (() => {
    const s = plans[0]?.settings.find(p => p.setting === 'Target ROM band')
    if (typeof s?.current === 'string') {
      const [lo, hi] = s.current.replace(/[°]/g, '').split('–').map(Number)
      return { low: lo ?? 68, high: hi ?? 90 }
    }
    return { low: 68, high: 90 }
  })()

  // Next session projected forward from today
  const nextSession = computeNextSession(sessions)

  // ── Trigger action handlers ──────────────────────────────────────────────
  function handleWatchReplay() {
    replayRef.current?.scrollIntoView({ behavior: 'smooth' })
  }

  function handleAskAgent() {
    const contextLine = trigger.conditions.map(c => c.displayLine).join('\n')
    setExpertContext(contextLine)
    setTimeout(() => expertRef.current?.scrollIntoView({ behavior: 'smooth' }), 80)
  }

  function handleDraftPlan() {
    planRef.current?.scrollIntoView({ behavior: 'smooth' })
  }

  return (
    <div className="p-6 min-h-screen">

      {/* ── Patient header ───────────────────────────────────── */}
      <div className="flex items-start justify-between mb-5 pb-5 border-b border-[#3D484E]">
        <div>
          <p className="text-[#A3B0B6] text-[9px] uppercase tracking-[0.2em] mb-1.5">
            {patient.condition}
          </p>
          <h1 className="text-[#E4E9EB] text-2xl font-bold tracking-tight leading-none mb-2">
            {patient.name}
          </h1>
          <p className="text-[#A3B0B6] text-xs font-mono">
            {patient.sessionCount} sessions
            {' · '}Last active {patient.lastSession}
            {' · '}{patient.weeksActive}w active
            {' · '}Plan v{patient.activePlanVersion}
          </p>
          <p className="text-[#A3B0B6] text-xs font-mono mt-0.5">
            Next: {nextSession}
            {' · '}RTM {rtm.daysWithData}/{rtm.daysScheduled} days this month
            {' · '}{rtm.reviewMinutes} min reviewed
          </p>
        </div>
        <div className="flex items-center gap-3">
          {/* Live / Replay mode indicator */}
          <div className="flex items-center gap-1.5 text-[10px] text-[#A3B0B6]">
            <span className={`w-1.5 h-1.5 rounded-full ${
              wsStatus === 'open' ? 'bg-[#7CC49A] animate-pulse' : 'bg-[#3D484E]'
            }`} />
            {wsStatus === 'open' ? 'Live' : 'Replay mode'}
          </div>
          {/* Status badge */}
          <div
            className="flex items-center gap-2 px-3 py-1.5 border text-xs font-medium"
            style={{
              borderColor: `${statusColor[patient.status]}55`,
              color: statusColor[patient.status],
              backgroundColor: `${statusColor[patient.status]}14`,
            }}
          >
            <span className="w-1.5 h-1.5 rounded-full" style={{ backgroundColor: statusColor[patient.status] }} />
            {statusLabel[patient.status]}
          </div>
        </div>
      </div>

      {/* ── 1. Trigger card ───────────────────────────────────── */}
      <TriggerCard
        trigger={trigger}
        patientId={patient.id}
        onWatchReplay={handleWatchReplay}
        onAskAgent={handleAskAgent}
        onDraftPlan={handleDraftPlan}
      />

      {/* ── 2. Key measures ──────────────────────────────────── */}
      <div className="mb-5">
        <KeyMeasures
          session={session}
          calibrationBaselineDeg={calibrationBaselineDeg}
          trunkLimit={trunkLimit}
          occlusionPct={12}
        />
      </div>

      {/* ── 3. Trend charts (target band from plan) ───────────── */}
      <div className="grid grid-cols-2 gap-3 mb-5">
        <ROMChart
          data={sessions}
          targetLow={targetBand.low}
          targetHigh={targetBand.high}
        />
        <CompensationChart data={sessions} limit={trunkLimit} />
      </div>

      {/* ── 4. Replay + Adherence ────────────────────────────── */}
      <div ref={replayRef} className="grid grid-cols-2 gap-3 mb-5">
        <ReplayMoments
          moments={session.replayMoments}
          repEvents={session.repEvents}
        />
        <Adherence schedule={schedule} history={sessions} />
      </div>

      {/* ── 5. Coach feed ────────────────────────────────────── */}
      <div className="mb-5">
        <CoachFeed log={session.coachLog} />
      </div>

      {/* ── 6. Expert agent panel ────────────────────────────── */}
      <div ref={expertRef} className="mb-5">
        <ExpertChat
          preloadedContext={expertContext}
          patientId={patient.id}
          conditionCategory={patient.condition}
        />
      </div>

      {/* ── 7. Plan versioning ───────────────────────────────── */}
      <div ref={planRef} className="mb-5">
        <PlanVersioning />
      </div>

      {/* ── 7b. Visit whiteboard: notes for the patient's therapist visit ── */}
      <div className="mb-5">
        <VisitNotes />
      </div>

      {/* ── 8. Data quality ──────────────────────────────────── */}
      <DataQuality
        uncertainty={session.uncertainty}
        occlusionPct={12}
      />
    </div>
  )
}
