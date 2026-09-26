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
    <div className="border-l-4 border-[#C67C1A] bg-white border border-[#DDE2E8] rounded-md p-5 mb-5 shadow-sm" style={{ borderLeftColor: '#C67C1A' }}>

      {/* Header row */}
      <div className="flex items-center justify-between mb-3">
        <div className="flex items-center gap-2">
          <span className="w-2 h-2 rounded-full bg-[#C67C1A]" />
          <p className="text-[#C67C1A] text-[10px] font-semibold uppercase tracking-[0.18em]">
            Functional change since last review
          </p>
        </div>
        <button
          onClick={() => setDismissing(d => !d)}
          className="text-[10px] text-[#5A6472] hover:text-[#1A1D23] transition-colors"
        >
          Dismiss with reason
        </button>
      </div>

      {/* Summary */}
      <p className="text-[#1A1D23] text-sm font-medium mb-4">{trigger.summary}</p>

      {/* Conditions */}
      <div className="flex flex-col gap-1.5 mb-4">
        {trigger.conditions.map(c => (
          <div
            key={c.id}
            className={`px-3 py-2 border rounded-sm font-mono text-xs leading-snug ${
              c.met
                ? 'bg-[#2E7D32]/5 border-[#2E7D32]/25 text-[#2E7D32]'
                : 'bg-[#C62828]/5 border-[#C62828]/25 text-[#C62828]'
            }`}
          >
            {c.displayLine}
          </div>
        ))}
      </div>

      {/* Rule definition */}
      <div className="bg-[#F4F6F8] border border-[#DDE2E8] rounded-sm px-3 py-2 mb-4">
        <span className="font-mono text-[9px] text-[#C67C1A] uppercase tracking-widest">rule  </span>
        <span className="font-mono text-[10px] text-[#5A6472] leading-relaxed">{trigger.ruleText}</span>
      </div>

      {/* Action buttons */}
      <div className="flex gap-2 flex-wrap">
        <button
          onClick={onWatchReplay}
          className="text-xs px-3 py-1.5 border border-[#1666C0]/40 text-[#1666C0] hover:bg-[#1666C0]/8 rounded-sm transition-colors"
        >
          ▶ Watch replay
        </button>
        <button
          onClick={onAskAgent}
          className="text-xs px-3 py-1.5 border border-[#0D9488]/40 text-[#0D9488] hover:bg-[#0D9488]/8 rounded-sm transition-colors"
        >
          Ask agent
        </button>
        <button
          onClick={onDraftPlan}
          className="text-xs px-3 py-1.5 border border-[#5A6472]/30 text-[#5A6472] hover:border-[#1A1D23]/30 hover:text-[#1A1D23] rounded-sm transition-colors"
        >
          Draft plan update
        </button>
      </div>

      {/* Inline dismiss flow */}
      {dismissing && (
        <div className="mt-4 pt-4 border-t border-[#DDE2E8]">
          <p className="text-[9px] text-[#5A6472] uppercase tracking-widest mb-2">
            Reason for dismissal (required · logged to audit)
          </p>
          <textarea
            value={reason}
            onChange={e => setReason(e.target.value)}
            placeholder="e.g. Reviewed with patient — continuing current plan, re-evaluate in 2 sessions"
            rows={2}
            className="w-full bg-[#F4F6F8] border border-[#DDE2E8] text-[#1A1D23] placeholder-[#5A6472]/60 text-xs px-3 py-2 focus:outline-none focus:border-[#1666C0] rounded-sm resize-none transition-colors"
          />
          <div className="flex gap-2 mt-2">
            <button
              onClick={handleDismiss}
              disabled={!reason.trim()}
              className="text-xs px-3 py-1.5 border border-[#C67C1A]/40 text-[#C67C1A] hover:bg-[#C67C1A]/8 rounded-sm transition-colors disabled:opacity-40 disabled:cursor-not-allowed"
            >
              Confirm dismissal
            </button>
            <button
              onClick={() => { setDismissing(false); setReason('') }}
              className="text-xs px-3 py-1.5 text-[#5A6472] hover:text-[#1A1D23] transition-colors"
            >
              Cancel
            </button>
          </div>
        </div>
      )}
    </div>
  )
}
