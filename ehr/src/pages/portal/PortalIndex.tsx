import { useEffect } from 'react'
import { useNavigate } from '@tanstack/react-router'
import { usePatients } from '../../hooks/useLivePatient'

export function PortalIndex() {
  const navigate = useNavigate()
  const { patients, settled } = usePatients()

  useEffect(() => {
    // The list is sorted by urgency and the live patient sorts above all of them, so this opens on the
    // real one whenever the coordinator is up. Waiting for `settled` keeps it from opening a seeded
    // patient in the half-second before the coordinator answers.
    if (settled && patients.length > 0) {
      void navigate({ to: '/portal/$patientId', params: { patientId: patients[0].id }, replace: true })
    }
  }, [navigate, patients, settled])

  return null
}
