import { useState, useEffect, useCallback } from 'react'
import { toSessionOverride } from '../data/unityProtocol'
import type { StoredSession, SessionOverride } from '../data/unityProtocol'

const API = 'http://localhost:3001'

interface UseLatestSessionResult {
  override: SessionOverride | null  // non-null only when Unity has posted data
  receivedAt: string | null         // ISO timestamp of the latest received session
  error: string | null              // non-null when server is unreachable
  refetch: () => void
}

// Polls GET /api/session/:patientId and returns the latest Unity-submitted session.
// `override` is null until Unity sends something; the portal falls back to seed data.
// Pass pollMs > 0 to enable background polling (e.g. 5000 for every 5 s).
export function useLatestSession(
  patientId: string,
  pollMs = 0,
): UseLatestSessionResult {
  const [override, setOverride]     = useState<SessionOverride | null>(null)
  const [receivedAt, setReceivedAt] = useState<string | null>(null)
  const [error, setError]           = useState<string | null>(null)
  const [tick, setTick]             = useState(0)

  const refetch = useCallback(() => setTick(t => t + 1), [])

  useEffect(() => {
    let cancelled = false

    fetch(`${API}/api/session/${encodeURIComponent(patientId)}`)
      .then(r => {
        if (!r.ok) throw new Error(`Server responded ${r.status}`)
        return r.json() as Promise<{ latest: StoredSession | null }>
      })
      .then(({ latest }) => {
        if (cancelled) return
        if (latest) {
          setOverride(toSessionOverride(latest))
          setReceivedAt(latest.receivedAt)
        }
        setError(null)
      })
      .catch(e => {
        if (!cancelled) setError(e.message)
      })

    return () => { cancelled = true }
  }, [patientId, tick])

  useEffect(() => {
    if (!pollMs) return
    const id = setInterval(refetch, pollMs)
    return () => clearInterval(id)
  }, [pollMs, refetch])

  return { override, receivedAt, error, refetch }
}
