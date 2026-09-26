import { useEffect } from 'react'
import { useNavigate } from '@tanstack/react-router'
import { PATIENTS } from '../../data/patients'

export function PortalIndex() {
  const navigate = useNavigate()

  useEffect(() => {
    // PATIENTS is already sorted by urgency; navigate to the most urgent
    if (PATIENTS.length > 0) {
      void navigate({ to: '/portal/$patientId', params: { patientId: PATIENTS[0].id }, replace: true })
    }
  }, [navigate])

  return null
}
