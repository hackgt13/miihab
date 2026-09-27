// ── Unity → RehabMii API payload ──────────────────────────────────────────
//
// Unity sends this as a JSON POST body to:
//   POST http://localhost:3001/api/session
//
// Keep it flat - no nesting - so Unity's JsonUtility.ToJson() works without
// custom serializers. All fields except patientId are optional so Unity can
// send partial updates if needed.
//
// Minimal C# example:
//   [Serializable]
//   class SessionPayload {
//     public string patientId;
//     public string sessionDate;      // "2025-09-26"
//     public string exercise;
//     public int    prescribedReps;
//     public int    repsAttempted;
//     public int    repsValid;
//     public float  peakRomDeg;
//     public float  trunkDeviationDeg;
//     public int    durationSec;
//     public int    painVas;          // 0 = no pain / no report
//   }
//   WebClient.UploadString(url, JsonUtility.ToJson(payload));

export interface UnityPayload {
  patientId:          string   // must match an id in seed.ts  e.g. "marcus-r"
  sessionDate?:       string   // ISO date  "YYYY-MM-DD"
  exercise?:          string
  prescribedReps?:    number
  repsAttempted?:     number
  repsValid?:         number
  peakRomDeg?:        number
  trunkDeviationDeg?: number
  durationSec?:       number
  painVas?:           number   // 0–10; omit or send 0 for no report
}

// Shape the server adds before storing
export interface StoredSession extends UnityPayload {
  receivedAt: string           // ISO timestamp set by the server
}

// What the portal's useLatestSession hook exposes to components
export interface SessionOverride {
  romDeg:         number
  trunkDeg:       number
  repsValid:      number
  repsAttempted:  number
  prescribedReps: number
  painVas:        number | null
}

// Map a stored server response to the override shape LiveReadings expects
export function toSessionOverride(s: StoredSession): SessionOverride {
  return {
    romDeg:         s.peakRomDeg        ?? 0,
    trunkDeg:       s.trunkDeviationDeg ?? 0,
    repsValid:      s.repsValid         ?? 0,
    repsAttempted:  s.repsAttempted     ?? 0,
    prescribedReps: s.prescribedReps    ?? 0,
    painVas:        s.painVas && s.painVas > 0 ? s.painVas : null,
  }
}
