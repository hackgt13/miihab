import { Link, useParams } from '@tanstack/react-router'
import { PATIENTS } from '../data/patients'
import type { PatientStatus } from '../data/patients'

const statusChip: Record<PatientStatus, { label: string; cls: string }> = {
  alert: { label: 'NEEDS REVIEW', cls: 'bg-[#FECACA] text-[#7F1D1D]' },
  watch: { label: 'ATTENTION',   cls: 'bg-[#FEF3C7] text-[#78350F]' },
  good:  { label: 'ON TRACK',    cls: 'bg-[#D1FAE5] text-[#064E3B]' },
}

export function Sidebar() {
  const params = useParams({ strict: false }) as { patientId?: string }
  const activeId = params.patientId

  return (
    <aside className="w-[220px] shrink-0 h-full bg-[#f1f3f5] border-r border-[#d5dae0] flex flex-col">
      {/* Search */}
      <div className="px-2 py-2 border-b border-[#d5dae0]">
        <div className="flex items-center gap-1.5 bg-white border border-[#c8cdd4] rounded-[2px] px-2 py-[5px]">
          <span className="text-[#9CA3AF] text-[11px] shrink-0">⌕</span>
          <input
            type="text"
            placeholder="Search patients…"
            className="flex-1 text-[11px] text-[#374151] placeholder:text-[#9CA3AF] outline-none bg-transparent"
          />
        </div>
      </div>

      {/* Section header bar */}
      <div className="px-3 py-[4px] bg-[#dde2e8] border-b border-[#c8cdd4] select-none">
        <span className="text-[#4B5563] text-[10px] font-semibold uppercase tracking-[0.18em]">Patients</span>
      </div>

      {/* Patient list */}
      <div className="flex-1 overflow-y-auto">
        <nav className="flex flex-col">
          {PATIENTS.map(patient => {
            const isActive = activeId === patient.id
            const chip = statusChip[patient.status]
            return (
              <Link
                key={patient.id}
                to="/portal/$patientId"
                params={{ patientId: patient.id }}
                className={[
                  'group flex flex-col gap-1 px-3 py-2 border-l-[3px] border-b border-[#dde2e8] transition-colors cursor-pointer',
                  isActive
                    ? 'bg-white border-l-[#1666C0]'
                    : 'border-l-transparent hover:bg-[#e6eaef]',
                ].join(' ')}
              >
                <div className="flex items-center justify-between gap-1">
                  <span className={[
                    'text-[12px] font-medium truncate leading-tight',
                    isActive ? 'text-[#1666C0]' : 'text-[#1F2937] group-hover:text-[#111827]',
                  ].join(' ')}>
                    {patient.name}
                  </span>
                  <span className={`text-[9px] font-semibold px-1.5 py-[2px] rounded-[2px] shrink-0 whitespace-nowrap ${chip.cls}`}>
                    {chip.label}
                  </span>
                </div>
                <span className="text-[10px] text-[#6B7280] truncate leading-none">
                  {patient.condition.split(' ').slice(0, 4).join(' ')}
                </span>
              </Link>
            )
          })}
        </nav>
      </div>
    </aside>
  )
}
