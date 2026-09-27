// ── Measurement validation config ─────────────────────────────────────────
// Read by KeyMeasures info popovers.
// Set goniometerDeltaDeg / goniometerTrials to undefined to show "Not yet validated".

export interface ValidationEntry {
  method: string
  algorithmVersion: string
  calibrationId: string
  goniometerDeltaDeg?: number   // ±X° vs goniometer
  goniometerTrials?: number     // n=Y trials
}

export const VALIDATION: {
  rom:      ValidationEntry
  trunk:    ValidationEntry
  repCount: ValidationEntry
} = {
  rom: {
    method: 'MediaPipe Pose 3.0 - bilateral landmark tracking',
    algorithmVersion: 'rehabmii-pose v0.4.1',
    calibrationId: 'CAL-2025-08-12',
    goniometerDeltaDeg: 3,
    goniometerTrials: 24,
  },
  trunk: {
    method: 'MediaPipe Pose 3.0 - torso vector angle (bilateral hip–shoulder)',
    algorithmVersion: 'rehabmii-pose v0.4.1',
    calibrationId: 'CAL-2025-08-12',
    goniometerDeltaDeg: 2,
    goniometerTrials: 18,
  },
  repCount: {
    method: 'Peak detection on ROM signal - contiguous threshold crossing',
    algorithmVersion: 'rehabmii-pose v0.4.1',
    calibrationId: 'CAL-2025-08-12',
    // no goniometer validation for rep count
  },
}
