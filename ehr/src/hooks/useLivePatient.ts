import { useEffect, useState } from 'react'
import { fetchLivePatient, LIVE_PATIENT_ID } from '../data/coordinator'
import { getPatientData, PATIENTS_SORTED } from '../data/seed'
import type { Patient, PatientData } from '../data/types'

// One live patient from the coordinator, standing beside the seeded ones.
//
// Polled rather than pushed. The coordinator's websocket carries a live exercise in progress (rep by rep,
// see useSessionSocket) but says nothing when a session *ends*, which is when every number on this page
// changes - so a session finishing on the headset would leave the portal showing the session before it
// until someone reloaded. Ten seconds is under the time it takes a clinician to look up from the headset.
//
// Nothing here throws a patient away: when the coordinator is not running, `offline` goes true and the
// seeded patients are all that is listed, so the portal is still demonstrable on a plane.

const POLL_MS = 10_000

interface Live {
  data: PatientData | null
  offline: boolean
  /** False only until the first answer, so the page does not flash "offline" while it is still asking. */
  settled: boolean
}

export function useLivePatient(): Live {
  const [state, setState] = useState<Live>({ data: null, offline: false, settled: false })

  useEffect(() => {
    let alive = true
    let timer: number | undefined

    async function read() {
      try {
        const data = await fetchLivePatient()
        if (alive) setState({ data, offline: false, settled: true })
      } catch {
        // Keep the last good read on screen rather than blanking the page: a coordinator restart is a
        // few seconds, and a portal that empties itself in the middle of a visit is worse than a stale one.
        if (alive) setState(previous => ({ data: previous.data, offline: true, settled: true }))
      }
      if (alive) timer = window.setTimeout(read, POLL_MS)
    }

    read()
    return () => { alive = false; if (timer) window.clearTimeout(timer) }
  }, [])

  return state
}

/** The sidebar's list: the live patient first when the coordinator answers, then the seeded ones. */
export function usePatients(): { patients: Patient[]; offline: boolean; settled: boolean } {
  const { data, offline, settled } = useLivePatient()
  return {
    patients: data ? [data.patient, ...PATIENTS_SORTED] : PATIENTS_SORTED,
    offline,
    settled,
  }
}

/** Live for the live patient, seed for the seeded ones - the caller never has to know which it asked for. */
export function usePatientData(patientId: string): { data: PatientData | undefined; live: boolean; offline: boolean } {
  const { data, offline } = useLivePatient()
  if (patientId === LIVE_PATIENT_ID) return { data: data ?? undefined, live: true, offline }
  return { data: getPatientData(patientId), live: false, offline: false }
}

export { LIVE_PATIENT_ID }
