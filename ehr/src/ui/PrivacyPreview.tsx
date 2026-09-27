import { useState } from 'react'
import { appendAudit } from '../data/store'

interface PrivacyPreviewProps {
  resourceType: string
  question: string
  conditionCategory: string
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
  { id: 'session_data',   label: 'Session data (ROM, reps)',   note: 'Numeric aggregate - no video or timestamps' },
  { id: 'dob',            label: 'Date of birth',              note: 'Age range included if added' },
  { id: 'video',          label: 'Video / replay clips',       note: 'Raw video - requires written patient consent' },
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
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
      <div className="bg-white border border-[#DDE2E8] rounded-md shadow-xl w-full max-w-md mx-4">

        {/* Header */}
        <div className="px-5 py-4 border-b border-[#DDE2E8] flex items-start justify-between">
          <div>
            <p className="text-[9px] text-[#C67C1A] font-semibold uppercase tracking-widest mb-0.5">
              Simulated - no data leaves this browser
            </p>
            <p className="text-[#1A1D23] text-sm font-medium">{resourceType} · Request preview</p>
          </div>
          <button onClick={onCancel} className="text-[#5A6472] hover:text-[#1A1D23] text-lg leading-none mt-0.5">×</button>
        </div>

        <div className="px-5 py-4">
          {/* Fields that will be sent */}
          <p className="text-[9px] text-[#5A6472] uppercase tracking-widest mb-2">
            Fields that will be sent
          </p>
          <div className="flex flex-col gap-1.5 mb-4">
            {requiredFields.map(f => (
              <div
                key={f.label}
                className="flex items-start gap-3 px-3 py-2 bg-[#F4F6F8] border border-[#2E7D32]/20 rounded-sm"
              >
                <span className="text-[#2E7D32] text-xs shrink-0 mt-0.5">✓</span>
                <div className="min-w-0">
                  <p className="text-[9px] text-[#5A6472] uppercase tracking-wider">{f.label}</p>
                  <p className="text-xs text-[#1A1D23] font-mono break-words">{f.value}</p>
                </div>
              </div>
            ))}
            {added.map(id => {
              const field = OPTIONAL_FIELDS.find(f => f.id === id)!
              return (
                <div
                  key={id}
                  className="flex items-start gap-3 px-3 py-2 bg-[#FFF8F0] border border-[#C67C1A]/20 rounded-sm"
                >
                  <span className="text-[#C67C1A] text-xs shrink-0 mt-0.5">+</span>
                  <div className="flex-1 min-w-0">
                    <p className="text-[9px] text-[#5A6472] uppercase tracking-wider">{field.label}</p>
                    <p className="text-[10px] text-[#5A6472] font-mono">{field.note}</p>
                  </div>
                  <button
                    onClick={() => toggle(id)}
                    className="text-[10px] text-[#5A6472] hover:text-[#C62828] transition-colors shrink-0"
                  >
                    remove
                  </button>
                </div>
              )
            })}
          </div>

          {/* Not included by default */}
          <p className="text-[9px] text-[#5A6472] uppercase tracking-widest mb-2">
            Not included by default
          </p>
          <div className="flex flex-col gap-1 mb-5">
            {OPTIONAL_FIELDS.filter(f => !added.includes(f.id)).map(f => (
              <div
                key={f.id}
                className="flex items-center justify-between px-3 py-1.5 bg-[#F4F6F8] border border-[#DDE2E8] rounded-sm"
              >
                <div className="flex items-center gap-2">
                  <span className="text-[#C62828] text-[10px]">✗</span>
                  <p className="text-[10px] text-[#5A6472]">{f.label}</p>
                </div>
                <button
                  onClick={() => toggle(f.id)}
                  className="text-[9px] text-[#5A6472] border border-[#DDE2E8] rounded-sm px-2 py-0.5 hover:border-[#C67C1A]/40 hover:text-[#C67C1A] transition-colors"
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
              className="flex-1 text-xs py-2 border border-[#1666C0]/40 text-[#1666C0] hover:bg-[#1666C0]/10 rounded-sm transition-colors"
            >
              Send request (Simulated)
            </button>
            <button
              onClick={onCancel}
              className="text-xs px-4 py-2 border border-[#DDE2E8] text-[#5A6472] hover:text-[#1A1D23] rounded-sm transition-colors"
            >
              Cancel
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}
