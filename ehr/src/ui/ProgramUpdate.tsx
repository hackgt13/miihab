import { useEffect, useState } from 'react'
import { Button } from './Button'

// The physician changes the live patient's program. One signed update (coordinator/visit.ts applyProgramUpdate)
// approves a new plan version and, with a note, puts it on the visit whiteboard. On the headset the menu then
// leads with "Your plan changed", and the therapist visit writes the change up and explains it with this reason.

interface Prescription {
  id: string; activityId: string; exerciseKind: string | null; targetCount: number; note: string
  params: Record<string, number | string>
}
interface Plan { version: number; approvedBy: string; approvedAt: string; rationale: string; origin: string; activities: Prescription[] }

const SIGNED_BY = 'Dr. Chen'
const num = (v: unknown) => (v === '' || v == null ? undefined : Number(v))

export function ProgramUpdate({ onUpdated }: { onUpdated?: () => void }) {
  const [plan, setPlan] = useState<Plan | null>(null)
  const [offline, setOffline] = useState(false)
  const [id, setId] = useState('')
  const [form, setForm] = useState({ targetDeg: '', targetMaxDeg: '', reps: '', holdMs: '', note: '' })
  const [rationale, setRationale] = useState('')
  const [patientNote, setPatientNote] = useState('')
  const [busy, setBusy] = useState(false)
  const [status, setStatus] = useState<{ ok: boolean; text: string } | null>(null)

  const measured = plan?.activities.filter(a => a.exerciseKind) ?? []
  const current = measured.find(a => a.id === id) ?? measured[0]

  async function load() {
    try {
      const r = await fetch('/api/plans/active')
      if (!r.ok) throw Error()
      setPlan(await r.json()); setOffline(false)
    } catch { setOffline(true) }
  }
  useEffect(() => { load() }, [])
  // The form starts from what the patient is doing now.
  useEffect(() => {
    if (!current) return
    setId(current.id)
    setForm({
      targetDeg: String(current.params.targetDeg ?? ''), targetMaxDeg: String(current.params.targetMaxDeg ?? ''),
      reps: String(current.targetCount ?? ''), holdMs: String(current.params.holdMs ?? ''), note: current.note ?? '',
    })
  }, [current?.id, plan?.version])

  const changed = current && (
    num(form.targetDeg) !== num(current.params.targetDeg) || num(form.targetMaxDeg) !== num(current.params.targetMaxDeg) ||
    num(form.reps) !== current.targetCount || num(form.holdMs) !== num(current.params.holdMs ?? '') || form.note !== (current.note ?? ''))

  async function approve() {
    if (!plan || !current) return
    setBusy(true); setStatus(null)
    try {
      const params: Record<string, number> = {}
      for (const key of ['targetDeg', 'targetMaxDeg', 'holdMs'] as const) if (num(form[key]) !== undefined) params[key] = num(form[key])!
      const r = await fetch('/api/visit/program-update', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          changes: { [current.id]: { targetCount: num(form.reps), note: form.note, params } },
          rationale, approvedBy: SIGNED_BY, expectedActiveVersion: plan.version,
          patientNote: patientNote.trim() || undefined,
        }),
      })
      const body = await r.json()
      if (!r.ok) throw Error(body.error ?? 'The update was not saved')
      setStatus({ ok: true, text: `Plan v${body.plan.version} approved. The patient's menu now says their plan changed; the visit explains it.` })
      setRationale(''); setPatientNote(''); await load(); onUpdated?.()
    } catch (e) { setStatus({ ok: false, text: (e as Error).message }) } finally { setBusy(false) }
  }

  const field = (key: keyof typeof form, label: string, unit = '') => (
    <label className="flex flex-col gap-1 text-[10px] text-[#5A6472] uppercase tracking-[0.12em]">
      {label}
      <div className="flex items-center gap-1">
        <input value={form[key]} onChange={e => setForm({ ...form, [key]: e.target.value })} inputMode="numeric"
          className="w-full bg-[#F4F6F8] border border-[#DDE2E8] text-[#1A1D23] text-sm px-2 py-1 font-mono focus:outline-none focus:border-[#1666C0]" />
        {unit && <span className="text-[#5A6472] normal-case tracking-normal">{unit}</span>}
      </div>
    </label>
  )

  return (
    <div id="program-update" className="bg-white border border-[#DDE2E8] rounded-md shadow-sm p-4">
      <div className="flex items-center justify-between mb-3">
        <p className="text-[#5A6472] text-[9px] uppercase tracking-[0.18em]">Program update</p>
        <span className="text-[10px] text-[#5A6472] font-mono">
          {offline ? 'Coordinator offline' : plan ? `Active v${plan.version} · ${plan.approvedBy}` : 'Loading…'}
        </span>
      </div>

      {!offline && current && (
        <>
          {measured.length > 1 && (
            <select value={id} onChange={e => setId(e.target.value)}
              className="mb-3 bg-[#F4F6F8] border border-[#DDE2E8] text-sm px-2 py-1 text-[#1A1D23]">
              {measured.map(a => <option key={a.id} value={a.id}>{a.id}</option>)}
            </select>
          )}
          <div className="grid grid-cols-4 gap-3 mb-3">
            {field('targetDeg', 'Target', '°')}
            {field('targetMaxDeg', 'Safe ceiling', '°')}
            {field('reps', 'Reps')}
            {field('holdMs', 'Hold', 'ms')}
          </div>
          <label className="flex flex-col gap-1 text-[10px] text-[#5A6472] uppercase tracking-[0.12em] mb-3">
            Cue for the patient
            <input value={form.note} onChange={e => setForm({ ...form, note: e.target.value })}
              className="bg-[#F4F6F8] border border-[#DDE2E8] text-[#1A1D23] text-sm px-2 py-1 normal-case tracking-normal focus:outline-none focus:border-[#1666C0]" />
          </label>
          <textarea value={rationale} onChange={e => setRationale(e.target.value.slice(0, 500))} rows={2}
            placeholder="Why (required, saved with the plan and read to the patient), e.g. Reach steady at 85° with good control for two sessions."
            className="w-full bg-[#F4F6F8] border border-[#DDE2E8] text-[#1A1D23] text-sm p-2 mb-2 focus:outline-none focus:border-[#1666C0]" />
          <input value={patientNote} onChange={e => setPatientNote(e.target.value.slice(0, 280))}
            placeholder="Optional note on the visit whiteboard, e.g. Nice work, let's reach a little higher."
            className="w-full bg-[#F4F6F8] border border-[#DDE2E8] text-[#1A1D23] text-sm px-2 py-1 mb-3 focus:outline-none focus:border-[#1666C0]" />
          <div className="flex items-center justify-between">
            <span className="text-[10px] text-[#5A6472] font-mono">Signed {SIGNED_BY} · the patient's next session uses it</span>
            <Button size="sm" disabled={busy || !changed || !rationale.trim()} onClick={approve}>Approve &amp; send to patient</Button>
          </div>
        </>
      )}
      {status && <p className={`text-xs mt-2 ${status.ok ? 'text-[#2E7D32]' : 'text-[#C62828]'}`}>{status.text}</p>}
    </div>
  )
}
