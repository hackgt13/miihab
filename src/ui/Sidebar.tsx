import { Link, useParams } from '@tanstack/react-router'
import { usePatients, LIVE_PATIENT_ID } from '../hooks/useLivePatient'
import type { PatientStatus } from '../data/patients'

const statusColor: Record<PatientStatus, string> = {
  alert: '#E08585',
  watch: '#E3A86B',
  good:  '#7CC49A',
}

export function Sidebar() {
  const params = useParams({ strict: false }) as { patientId?: string }
  const activeId = params.patientId
  // The live patient is read from the coordinator and sorts above the seeded ones; when the coordinator
  // is not running the list is the seeded ones alone, said out loud rather than left to be guessed at.
  const { patients, offline, settled } = usePatients()

  return (
    <aside className="w-[192px] shrink-0 h-full bg-[#2A3337] border-r border-[#3D484E] flex flex-col">
      {/* Header */}
      <div className="px-4 pt-5 pb-4 border-b border-[#3D484E]">
        <span className="text-[#E4E9EB] font-semibold text-sm">Kinesthetic</span>
        <p className="text-[#A3B0B6] text-[9px] tracking-[0.15em] uppercase mt-0.5">
          Physician Portal
        </p>
      </div>

      {/* Patient list — sorted by urgency (pre-sorted in seed.ts) */}
      <div className="flex-1 overflow-y-auto py-2">
        <p className="px-4 pt-1 pb-1.5 text-[#A3B0B6] text-[9px] font-medium uppercase tracking-[0.18em]">
          Patients
        </p>
        {settled && offline && (
          <p className="px-4 pb-1.5 text-[#E3A86B] text-[9px]">Coordinator offline · seeded patients only</p>
        )}
        <nav className="flex flex-col">
          {patients.map(patient => {
            const isActive = activeId === patient.id
            return (
              <Link
                key={patient.id}
                to="/portal/$patientId"
                params={{ patientId: patient.id }}
                className={[
                  'group flex items-start justify-between gap-2 px-4 py-2 border-l-2 transition-colors cursor-pointer',
                  isActive
                    ? 'bg-[#323C41] border-[#6FB8C4]'
                    : 'border-transparent hover:bg-[#323C41] hover:border-[#3D484E]',
                ].join(' ')}
              >
                <div className="flex flex-col gap-0.5 min-w-0">
                  {patient.id === LIVE_PATIENT_ID && (
                    <span className="text-[#6FB8C4] text-[8px] uppercase tracking-[0.18em] leading-none">Live</span>
                  )}
                  <span
                    className={[
                      'text-xs font-medium truncate leading-none',
                      isActive ? 'text-[#E4E9EB]' : 'text-[#A3B0B6] group-hover:text-[#E4E9EB]',
                    ].join(' ')}
                  >
                    {patient.name}
                  </span>
                  <span className="text-[10px] text-[#A3B0B6] truncate leading-none opacity-70">
                    {patient.condition.split(' ').slice(0, 3).join(' ')}
                  </span>
                  <span
                    className="text-[9px] truncate leading-tight mt-0.5"
                    style={{ color: statusColor[patient.status], opacity: 0.75 }}
                  >
                    {patient.flagDetail}
                  </span>
                </div>
                <span
                  className="w-1.5 h-1.5 rounded-full shrink-0 mt-0.5"
                  style={{ backgroundColor: statusColor[patient.status] }}
                />
              </Link>
            )
          })}
        </nav>
      </div>

      {/* Footer */}
      <div className="px-4 py-3 border-t border-[#3D484E]">
        <p className="text-[#E4E9EB] text-xs font-medium">Dr. Chen</p>
        <button className="text-[#A3B0B6] text-[10px] hover:text-[#6FB8C4] transition-colors mt-0.5">
          Sign out
        </button>
      </div>
    </aside>
  )
}
