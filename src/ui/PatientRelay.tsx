import type { VisitReply } from '../data/types'

// What the patient said, at the top of the page, before any of the measurements.
//
// Every visit in RehabMii ends on "is there anything you want me to know?", and the answer is stored by
// coordinator/visit.ts. It is the only thing on this page the patient said themselves — everything else is
// a sensor's account of them — and it is the only thing that can mean stop. So it is read first, not found
// six panels down in the visit whiteboard where the rest of the correspondence lives.
//
// Only what a physician should act on is raised here: pain, "too hard", and a written message, inside the
// week. "All good" and "too easy" are still worth having and still sit with the whiteboard; they are not
// worth interrupting a page for.

const TONE: Record<VisitReply['kind'], { border: string; text: string; label: string }> = {
  hurt:    { border: '#C62828', text: '#C62828', label: 'Reported pain' },
  hard:    { border: '#C67C1A', text: '#C67C1A', label: 'Too hard' },
  message: { border: '#1666C0', text: '#1666C0', label: 'Message' },
  easy:    { border: '#1666C0', text: '#1666C0', label: 'Too easy' },
  fine:    { border: '#166534', text: '#166534', label: 'All good' },
}

function when(at: string): string {
  const days = Math.round((Date.now() - Date.parse(at)) / 86_400_000)
  if (days <= 0) return 'today'
  if (days === 1) return 'yesterday'
  return `${days} days ago`
}

export function PatientRelay({ replies }: { replies: VisitReply[] }) {
  if (replies.length === 0) return null

  return (
    <div className="mb-5 space-y-2">
      {replies.map(reply => {
        const tone = TONE[reply.kind]
        return (
          <div
            key={reply.id}
            className="border-l-2 bg-white border border-[#DDE2E8] rounded-md shadow-sm px-4 py-3"
            style={{ borderLeftColor: tone.border }}
          >
            <div className="flex items-baseline justify-between gap-3">
              <p className="text-[9px] uppercase tracking-[0.18em]" style={{ color: tone.text }}>
                {tone.label} · from the patient
              </p>
              <p className="text-[#5A6472] text-[10px] font-mono">
                said at a visit {when(reply.at)}
                {reply.planVersion != null && ` · on plan v${reply.planVersion}`}
              </p>
            </div>
            <p className="text-[#1A1D23] text-sm mt-1">“{reply.text}”</p>
          </div>
        )
      })}
    </div>
  )
}
