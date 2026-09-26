import { useState } from 'react'
import { appendAudit } from '../data/store'

interface PrivacyPreviewProps {
  resourceType: string    // 'MSL' | 'Patient Support' | 'Medical Information' | 'Access/Coverage'
  question: string
  conditionCategory: string   // de-identified condition label
  patientId: string
  onSend: () => void
  onCancel: () => void
}

interface OptionalField {
  id: string
  label: string
  note: string
}

const OPTIONAL_FIELDS: OptionalField[] = [
  { id: 'patient_name',   label: 'Patient name',              note: 'Requires explicit de-identification waiver' },
  { id: 'session_data',   label: 'Session data (ROM, reps)',   note: 'Numeric aggregate — no video or timestamps' },
  { id: 'dob',            label: 'Date of birth',              note: 'Age range included if added' },
  { id: 'video',          label: 'Video / replay clips',       note: 'Raw video — requires written patient consent' },
]

export function PrivacyPreview({
  resourceType,
  question,
  conditionCategory,
  patientId,
  onSend,
  onCancel,
}: PrivacyPreviewProps) {
  const [added, setAdded] = useState<string[]>([])

  function toggle(id: string) {
    setAdded(prev => prev.includes(id) ? prev.filter(x => x !== id) : [...prev, id])
  }

  function handleSend() {
    const addedLabels = added.map(id => OPTIONAL_FIELDS.find(f => f.id === id)!.label)
    appendAudit({
      patientId,
      action: 'resource_drafted',
      detail: `${resourceType} request. Payload: condition_category, question, clinician_contact${
        addedLabels.length > 0 ? `, ${addedLabels.join(', ')}` : ''
      }`,
      actor: 'Dr. Chen',
    })
    onSend()
  }

  const requiredFields = [
    { label: 'Condition category', value: conditionCategory },
    { label: 'Question text',       value: question },
    { label: 'Clinician contact',   value: 'Dr. Chen · KIN-001' },
  ]

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/65">
      <div className="bg-[#2A3337] border border-[#3D484E] w-full max-w-md mx-4">

        {/* Header */}
        <div className="px-5 py-4 border-b border-[#3D484E] flex items-start justify-between">
          <div>
            <p className="text-[9px] text-[#E3A86B] font-semibold uppercase tracking-widest mb-0.5">
              Simulated — no data leaves this browser
            </p>
            <p className="text-[#E4E9EB] text-sm font-medium">{resourceType} · Request preview</p>
          </div>
          <button onClick={onCancel} className="text-[#A3B0B6] hover:text-[#E4E9EB] text-lg leading-none mt-0.5">×</button>
        </div>

        <div className="px-5 py-4">
          {/* Fields that will be sent */}
          <p className="text-[9px] text-[#A3B0B6] uppercase tracking-widest mb-2">
            Fields that will be sent
          </p>
          <div className="flex flex-col gap-1.5 mb-4">
            {requiredFields.map(f => (
              <div
                key={f.label}
                className="flex items-start gap-3 px-3 py-2 bg-[#232B2F] border border-[#7CC49A]/20"
              >
                <span className="text-[#7CC49A] text-xs shrink-0 mt-0.5">✓</span>
                <div className="min-w-0">
                  <p className="text-[9px] text-[#A3B0B6] uppercase tracking-wider">{f.label}</p>
                  <p className="text-xs text-[#E4E9EB] font-mono break-words">{f.value}</p>
                </div>
              </div>
            ))}
            {added.map(id => {
              const field = OPTIONAL_FIELDS.find(f => f.id === id)!
              return (
                <div
                  key={id}
                  className="flex items-start gap-3 px-3 py-2 bg-[#232B2F] border border-[#E3A86B]/20"
                >
                  <span className="text-[#E3A86B] text-xs shrink-0 mt-0.5">+</span>
                  <div className="flex-1 min-w-0">
                    <p className="text-[9px] text-[#A3B0B6] uppercase tracking-wider">{field.label}</p>
                    <p className="text-[10px] text-[#A3B0B6] font-mono">{field.note}</p>
                  </div>
                  <button
                    onClick={() => toggle(id)}
                    className="text-[10px] text-[#A3B0B6] hover:text-[#E08585] transition-colors shrink-0"
                  >
                    remove
                  </button>
                </div>
              )
            })}
          </div>

          {/* Not included by default */}
          <p className="text-[9px] text-[#A3B0B6] uppercase tracking-widest mb-2">
            Not included by default
          </p>
          <div className="flex flex-col gap-1 mb-5">
            {OPTIONAL_FIELDS.filter(f => !added.includes(f.id)).map(f => (
              <div
                key={f.id}
                className="flex items-center justify-between px-3 py-1.5 bg-[#232B2F] border border-[#3D484E]"
              >
                <div className="flex items-center gap-2">
                  <span className="text-[#E08585] text-[10px]">✗</span>
                  <p className="text-[10px] text-[#A3B0B6]">{f.label}</p>
                </div>
                <button
                  onClick={() => toggle(f.id)}
                  className="text-[9px] text-[#A3B0B6] border border-[#3D484E] px-2 py-0.5 hover:border-[#E3A86B]/40 hover:text-[#E3A86B] transition-colors"
                >
                  + Add
                </button>
              </div>
            ))}
          </div>

          {/* Actions */}
          <div className="flex gap-2">
            <button
              onClick={handleSend}
              className="flex-1 text-xs py-2 border border-[#6FB8C4]/40 text-[#6FB8C4] hover:bg-[#6FB8C4]/10 transition-colors"
            >
              Send request (Simulated)
            </button>
            <button
              onClick={onCancel}
              className="text-xs px-4 py-2 border border-[#3D484E] text-[#A3B0B6] hover:text-[#E4E9EB] transition-colors"
            >
              Cancel
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}
