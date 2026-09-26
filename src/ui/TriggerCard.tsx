import { useState } from 'react'
import type { EvaluatedTrigger } from '../data/rules'
import { appendAudit } from '../data/store'

interface TriggerCardProps {
  trigger: EvaluatedTrigger
  patientId: string
  onWatchReplay: () => void
  onAskAgent: () => void
  onDraftPlan: () => void
}

export function TriggerCard({
  trigger,
  patientId,
  onWatchReplay,
  onAskAgent,
  onDraftPlan,
}: TriggerCardProps) {
  const [dismissing, setDismissing] = useState(false)
  const [reason, setReason]         = useState('')
  const [dismissed, setDismissed]   = useState(false)

  if (!trigger.fired || dismissed) return null

  function handleDismiss() {
    const r = reason.trim()
    if (!r) return
    appendAudit({
      patientId,
      action: 'session_reviewed',
      detail: `Trigger dismissed. Reason: ${r}`,
      actor: 'Dr. Chen',
    })
    setDismissed(true)
  }

  return (
    <div className="border border-[#E3A86B]/40 bg-[#E3A86B]/5 p-5 mb-5">

      {/* Header row */}
      <div className="flex items-center justify-between mb-3">
        <div className="flex items-center gap-2">
          <span className="w-2 h-2 rounded-full bg-[#E3A86B]" />
          <p className="text-[#E3A86B] text-[10px] font-semibold uppercase tracking-[0.18em]">
            Functional change since last review
          </p>
        </div>
        <button
          onClick={() => setDismissing(d => !d)}
          className="text-[10px] text-[#A3B0B6] hover:text-[#E4E9EB] transition-colors"
        >
          Dismiss with reason
        </button>
      </div>

      {/* Summary */}
      <p className="text-[#E4E9EB] text-sm font-medium mb-4">{trigger.summary}</p>

      {/* Conditions — one line each with ✓/✗ */}
      <div className="flex flex-col gap-1.5 mb-4">
        {trigger.conditions.map(c => (
          <div
            key={c.id}
            className={`px-3 py-2 border font-mono text-xs leading-snug ${
              c.met
                ? 'bg-[#7CC49A]/5 border-[#7CC49A]/25 text-[#7CC49A]'
                : 'bg-[#E08585]/5 border-[#E08585]/25 text-[#E08585]'
            }`}
          >
            {c.displayLine}
          </div>
        ))}
      </div>

      {/* Rule definition (single source of truth) */}
      <div className="bg-[#232B2F]/60 border border-[#3D484E] px-3 py-2 mb-4">
        <span className="font-mono text-[9px] text-[#E3A86B] uppercase tracking-widest">rule  </span>
        <span className="font-mono text-[10px] text-[#A3B0B6] leading-relaxed">{trigger.ruleText}</span>
      </div>

      {/* Action buttons */}
      <div className="flex gap-2 flex-wrap">
        <button
          onClick={onWatchReplay}
          className="text-xs px-3 py-1.5 border border-[#6FB8C4]/40 text-[#6FB8C4] hover:bg-[#6FB8C4]/8 transition-colors"
        >
          ▶ Watch replay
        </button>
        <button
          onClick={onAskAgent}
          className="text-xs px-3 py-1.5 border border-[#9CC2B5]/40 text-[#9CC2B5] hover:bg-[#9CC2B5]/8 transition-colors"
        >
          Ask agent
        </button>
        <button
          onClick={onDraftPlan}
          className="text-xs px-3 py-1.5 border border-[#A3B0B6]/30 text-[#A3B0B6] hover:border-[#E4E9EB]/30 hover:text-[#E4E9EB] transition-colors"
        >
          Draft plan update
        </button>
      </div>

      {/* Inline dismiss flow */}
      {dismissing && (
        <div className="mt-4 pt-4 border-t border-[#3D484E]">
          <p className="text-[9px] text-[#A3B0B6] uppercase tracking-widest mb-2">
            Reason for dismissal (required · logged to audit)
          </p>
          <textarea
            value={reason}
            onChange={e => setReason(e.target.value)}
            placeholder="e.g. Reviewed with patient — continuing current plan, re-evaluate in 2 sessions"
            rows={2}
            className="w-full bg-[#232B2F] border border-[#3D484E] text-[#E4E9EB] placeholder-[#A3B0B6]/60 text-xs px-3 py-2 focus:outline-none focus:border-[#6FB8C4] resize-none transition-colors"
          />
          <div className="flex gap-2 mt-2">
            <button
              onClick={handleDismiss}
              disabled={!reason.trim()}
              className="text-xs px-3 py-1.5 border border-[#E3A86B]/40 text-[#E3A86B] hover:bg-[#E3A86B]/8 transition-colors disabled:opacity-40 disabled:cursor-not-allowed"
            >
              Confirm dismissal
            </button>
            <button
              onClick={() => { setDismissing(false); setReason('') }}
              className="text-xs px-3 py-1.5 text-[#A3B0B6] hover:text-[#E4E9EB] transition-colors"
            >
              Cancel
            </button>
          </div>
        </div>
      )}
    </div>
  )
}
