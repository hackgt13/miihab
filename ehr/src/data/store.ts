import { useState, useEffect, useRef, useCallback } from 'react'
import type { SOAPNote, AuditEntry, PlanVersion } from './types'

function load<T>(key: string, fallback: T): T {
  try {
    const raw = localStorage.getItem(key)
    return raw ? (JSON.parse(raw) as T) : fallback
  } catch {
    return fallback
  }
}

function save(key: string, value: unknown): void {
  localStorage.setItem(key, JSON.stringify(value))
  window.dispatchEvent(new CustomEvent('kv:update', { detail: { key } }))
}

function useStoreKey<T>(key: string, fallback: T): [T, (v: T) => void] {
  const fallbackRef = useRef(fallback)
  const [val, setVal] = useState<T>(() => load(key, fallbackRef.current))

  useEffect(() => {
    const handler = (e: Event) => {
      const ev = e as CustomEvent<{ key: string }>
      if (ev.detail?.key === key) {
        setVal(load(key, fallbackRef.current))
      }
    }
    window.addEventListener('kv:update', handler)
    return () => window.removeEventListener('kv:update', handler)
  }, [key])

  const set = useCallback((v: T) => {
    save(key, v)
    setVal(v)
  }, [key])

  return [val, set]
}

// ── SOAP Notes ────────────────────────────────────────────────────────────

export function useSOAPNotes(patientId: string) {
  return useStoreKey<SOAPNote[]>(`notes:${patientId}`, [])
}

// ── Plans (initialized from seed if no stored version exists) ─────────────

export function usePatientPlans(patientId: string, seedPlans: PlanVersion[]) {
  const key = `plans:${patientId}`
  const fallbackRef = useRef(seedPlans)
  const [val, setVal] = useState<PlanVersion[]>(() => load(key, fallbackRef.current))

  useEffect(() => {
    const handler = (e: Event) => {
      const ev = e as CustomEvent<{ key: string }>
      if (ev.detail?.key === key) {
        setVal(load(key, fallbackRef.current))
      }
    }
    window.addEventListener('kv:update', handler)
    return () => window.removeEventListener('kv:update', handler)
  }, [key])

  const set = useCallback((v: PlanVersion[]) => {
    save(key, v)
    setVal(v)
  }, [key])

  return [val, set] as const
}

// ── Coach feed flags (array of flagged event indices) ─────────────────────

export function useCoachFlags(patientId: string) {
  return useStoreKey<number[]>(`flags:${patientId}`, [])
}

// ── RTM review timer (minutes, persisted per patient + month) ─────────────

export function useRTMMinutes(patientId: string, month: string) {
  return useStoreKey<number>(`rtm:${patientId}:${month}`, 0)
}

// ── Audit log ─────────────────────────────────────────────────────────────

export function useAuditLog() {
  return useStoreKey<AuditEntry[]>('audit:log', [])
}

export function appendAudit(entry: Omit<AuditEntry, 'id' | 'timestamp'>): void {
  const current = load<AuditEntry[]>('audit:log', [])
  const next: AuditEntry = {
    ...entry,
    id: `audit_${Date.now()}`,
    timestamp: new Date().toISOString(),
  }
  save('audit:log', [...current, next])
}
