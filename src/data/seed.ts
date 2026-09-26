import type {
  PatientData, Patient, SessionSummary, SessionHandoff,
  PlanVersion, RTMRecord,
} from './types'

// ── Marcus R. — Spinal Cord Injury (C5) ──────────────────────────────────

const MARCUS_SESSIONS: SessionSummary[] = [
  { session: 1,  date: '2025-08-12', medianPeakDeg: 48, trunkMeanDeg: 3,  validReps: 5, prescribedReps: 8, completed: true,  synthetic: true  },
  { session: 2,  date: '2025-08-14', medianPeakDeg: 52, trunkMeanDeg: 4,  validReps: 6, prescribedReps: 8, completed: true,  synthetic: true  },
  { session: 3,  date: '2025-08-16', medianPeakDeg: 55, trunkMeanDeg: 4,  validReps: 7, prescribedReps: 8, completed: true,  synthetic: true  },
  { session: 4,  date: '2025-08-18', medianPeakDeg: 58, trunkMeanDeg: 3,  validReps: 8, prescribedReps: 8, completed: true,  synthetic: true  },
  { session: 5,  date: '2025-08-21', medianPeakDeg: 61, trunkMeanDeg: 5,  validReps: 7, prescribedReps: 8, completed: true,  synthetic: true  },
  { session: 6,  date: '2025-08-23', medianPeakDeg: 63, trunkMeanDeg: 5,  validReps: 7, prescribedReps: 8, completed: true,  synthetic: true  },
  { session: 7,  date: '2025-08-26', medianPeakDeg: 67, trunkMeanDeg: 6,  validReps: 8, prescribedReps: 8, completed: true,  synthetic: true  },
  { session: 8,  date: '2025-08-28', medianPeakDeg: 68, trunkMeanDeg: 6,  validReps: 6, prescribedReps: 8, completed: true,  synthetic: true  },
  { session: 9,  date: '2025-09-01', medianPeakDeg: 71, trunkMeanDeg: 7,  validReps: 7, prescribedReps: 8, completed: true,  synthetic: true  },
  { session: 10, date: '2025-09-03', medianPeakDeg: 74, trunkMeanDeg: 8,  validReps: 7, prescribedReps: 8, completed: true,  synthetic: true  },
  { session: 11, date: '2025-09-05', medianPeakDeg: 77, trunkMeanDeg: 9,  validReps: 8, prescribedReps: 8, completed: true,  synthetic: true  },
  { session: 12, date: '2025-09-08', medianPeakDeg: 79, trunkMeanDeg: 9,  validReps: 6, prescribedReps: 8, completed: true,  synthetic: true  },
  { session: 13, date: '2025-09-10', medianPeakDeg: 81, trunkMeanDeg: 10, validReps: 7, prescribedReps: 8, completed: true,  synthetic: true  },
  { session: 14, date: '2025-09-12', medianPeakDeg: 84, trunkMeanDeg: 11, validReps: 7, prescribedReps: 9, completed: true,  synthetic: false },
]

const MARCUS_SCHEDULE = [
  { date: '2025-08-12', completed: true  },
  { date: '2025-08-14', completed: true  },
  { date: '2025-08-16', completed: true  },
  { date: '2025-08-18', completed: true  },
  { date: '2025-08-20', completed: false },
  { date: '2025-08-21', completed: true  },
  { date: '2025-08-23', completed: true  },
  { date: '2025-08-25', completed: false },
  { date: '2025-08-26', completed: true  },
  { date: '2025-08-28', completed: true  },
  { date: '2025-08-30', completed: false },
  { date: '2025-09-01', completed: true  },
  { date: '2025-09-03', completed: true  },
  { date: '2025-09-05', completed: true  },
  { date: '2025-09-08', completed: true  },
  { date: '2025-09-10', completed: true  },
  { date: '2025-09-12', completed: true  },
]

const MARCUS_HANDOFF: SessionHandoff = {
  sessionId: 's_014',
  patientId: 'marcus-r',
  planVersion: 1,
  exercise: 'Seated shoulder raise (R)',
  reps: { valid: 7, attempted: 9, prescribed: 8 },
  medianPeakDeg: 84,
  baselineMedianPeakDeg: 68,
  trunkDeviation: { meanDeg: 6, finalRepsDeg: 11 },
  durationSec: 412,
  timestamp: '2025-09-12T14:38:00Z',
  patientReports: [
    { t: 312.4, text: 'Feels stiff', severity: 5 },
    { t: 390.0, text: 'Slight ache at end range', severity: 3 },
  ],
  replayMoments: [
    { t: 178.2, label: 'Trunk lean — rep 5', repIndex: 5 },
    { t: 298.1, label: 'Trunk lean — rep 7 (peak)', repIndex: 7 },
    { t: 341.0, label: 'Elbow drop on return', repIndex: 8 },
  ],
  uncertainty: [
    'Left elbow occluded 12% of frames',
    'Seated torso baseline drifted ≈ 2° mid-session',
  ],
  repEvents: [
    { rep: 1, peakDeg: 81, trunkDeg: 4,  valid: true  },
    { rep: 2, peakDeg: 83, trunkDeg: 5,  valid: true  },
    { rep: 3, peakDeg: 85, trunkDeg: 6,  valid: true  },
    { rep: 4, peakDeg: 82, trunkDeg: 5,  valid: true  },
    { rep: 5, peakDeg: 86, trunkDeg: 10, valid: false, reason: 'Trunk deviation > 8°' },
    { rep: 6, peakDeg: 84, trunkDeg: 7,  valid: true  },
    { rep: 7, peakDeg: 88, trunkDeg: 11, valid: false, reason: 'Trunk deviation > 8°' },
    { rep: 8, peakDeg: 83, trunkDeg: 6,  valid: true  },
    { rep: 9, peakDeg: 80, trunkDeg: 5,  valid: true  },
  ],
  coachLog: [
    { t: 0,   source: 'coach',   kind: 'cue_template', content: 'Starting session. Raise your right arm to shoulder height.' },
    { t: 45,  source: 'coach',   kind: 'speak',        content: 'Good. Keep that pace.' },
    { t: 178, source: 'coach',   kind: 'cue_template', content: 'Hold. Chest forward.' },
    { t: 180, source: 'engine',  kind: 'tool_call',    content: 'set_target', tool: 'set_target', args: { angle_band: [68, 90] } },
    { t: 220, source: 'coach',   kind: 'llm_reply',    content: "You're doing the shoulder raise to rebuild rotator cuff strength and restore range after your injury. Each rep trains your brain and muscle to relearn the movement pattern." },
    { t: 298, source: 'coach',   kind: 'cue_template', content: "Hold. Chest facing forward — you're leaning." },
    { t: 312, source: 'patient', kind: 'report',       content: 'Patient report: Feels stiff (5/10)' },
    { t: 360, source: 'coach',   kind: 'speak',        content: 'Last two reps. Strong finish.' },
    { t: 390, source: 'patient', kind: 'report',       content: 'Patient report: Slight ache at end range (3/10)' },
    { t: 412, source: 'engine',  kind: 'tool_call',    content: 'end_session', tool: 'end_session', args: {} },
  ],
  synthetic: false,
}

const MARCUS_PLANS: PlanVersion[] = [
  {
    version: 1,
    date: '2025-08-12',
    exercise: 'Seated shoulder raise (R)',
    settings: [
      { setting: 'Target ROM band',   current: '68–90°', proposed: '75–95°', unit: '°',    min: 50,  max: 110, changed: true  },
      { setting: 'Reps prescribed',   current: 8,        proposed: 10,       unit: 'reps', min: 4,   max: 12,  changed: true  },
      { setting: 'Trunk limit',        current: 8,        proposed: 8,        unit: '°',    min: 3,   max: 10,  changed: false },
      { setting: 'Rest between sets',  current: '60s',    proposed: '45s',                            changed: true  },
    ],
    notes: 'Initial plan. Baseline ROM 48°.',
    approvedBy: 'Dr. Chen',
    approvedAt: '2025-08-12T09:00:00Z',
  },
]

const MARCUS_RTM: RTMRecord = {
  patientId: 'marcus-r',
  month: '2025-09',
  daysWithData: 11,
  daysScheduled: 16,
  reviewMinutes: 14,
  sessionCount: 14,
}

const MARCUS: PatientData = {
  patient: {
    id: 'marcus-r',
    name: 'Marcus R.',
    condition: 'Spinal Cord Injury (C5)',
    status: 'alert',
    urgency: 0,
    flagDetail: 'Trunk compensation rising — review needed',
    lastSession: '2 hours ago',
    sessionCount: 14,
    weeksActive: 5,
    nextScheduled: '2025-09-15',
    activePlanVersion: 1,
  },
  sessions: MARCUS_SESSIONS,
  latestHandoff: MARCUS_HANDOFF,
  schedule: MARCUS_SCHEDULE,
  plans: MARCUS_PLANS,
  rtm: MARCUS_RTM,
}

// ── James T. — Rotator Cuff Repair ───────────────────────────────────────

const JAMES_SESSIONS: SessionSummary[] = [
  { session: 1, date: '2025-09-02', medianPeakDeg: 55, trunkMeanDeg: 5, validReps: 5, prescribedReps: 8, completed: true,  synthetic: true  },
  { session: 2, date: '2025-09-04', medianPeakDeg: 58, trunkMeanDeg: 5, validReps: 6, prescribedReps: 8, completed: true,  synthetic: true  },
  { session: 3, date: '2025-09-08', medianPeakDeg: 60, trunkMeanDeg: 6, validReps: 6, prescribedReps: 8, completed: true,  synthetic: true  },
  { session: 4, date: '2025-09-12', medianPeakDeg: 62, trunkMeanDeg: 6, validReps: 7, prescribedReps: 8, completed: true,  synthetic: true  },
  { session: 5, date: '2025-09-16', medianPeakDeg: 64, trunkMeanDeg: 7, validReps: 6, prescribedReps: 8, completed: true,  synthetic: true  },
  { session: 6, date: '2025-09-22', medianPeakDeg: 67, trunkMeanDeg: 7, validReps: 6, prescribedReps: 8, completed: true,  synthetic: false },
]

const JAMES_SCHEDULE = [
  { date: '2025-09-02', completed: true  },
  { date: '2025-09-04', completed: true  },
  { date: '2025-09-06', completed: false },
  { date: '2025-09-08', completed: true  },
  { date: '2025-09-10', completed: false },
  { date: '2025-09-12', completed: true  },
  { date: '2025-09-14', completed: false },
  { date: '2025-09-16', completed: true  },
  { date: '2025-09-18', completed: false },
  { date: '2025-09-20', completed: false },
  { date: '2025-09-22', completed: true  },
]

const JAMES_HANDOFF: SessionHandoff = {
  sessionId: 's_006',
  patientId: 'james-t',
  planVersion: 1,
  exercise: 'Shoulder abduction (R)',
  reps: { valid: 6, attempted: 8, prescribed: 8 },
  medianPeakDeg: 67,
  baselineMedianPeakDeg: 55,
  trunkDeviation: { meanDeg: 7, finalRepsDeg: 9 },
  durationSec: 340,
  timestamp: '2025-09-22T11:20:00Z',
  patientReports: [
    { t: 180.0, text: 'Sharp pain at 60°', severity: 6 },
    { t: 300.0, text: 'Fatigue in shoulder', severity: 4 },
  ],
  replayMoments: [
    { t: 165.0, label: 'Pain event — rep 4', repIndex: 4 },
    { t: 290.0, label: 'Trunk lean — rep 7', repIndex: 7 },
  ],
  uncertainty: [
    'Shoulder marker lost 8% of frames (clothing occlusion)',
    'Patient seated off-center — hip baseline shifted',
  ],
  repEvents: [
    { rep: 1, peakDeg: 62, trunkDeg: 5, valid: true  },
    { rep: 2, peakDeg: 65, trunkDeg: 6, valid: true  },
    { rep: 3, peakDeg: 67, trunkDeg: 6, valid: true  },
    { rep: 4, peakDeg: 60, trunkDeg: 7, valid: false, reason: 'Patient report: pain 6/10' },
    { rep: 5, peakDeg: 68, trunkDeg: 6, valid: true  },
    { rep: 6, peakDeg: 70, trunkDeg: 7, valid: true  },
    { rep: 7, peakDeg: 66, trunkDeg: 9, valid: false, reason: 'Trunk deviation > 8°' },
    { rep: 8, peakDeg: 65, trunkDeg: 8, valid: true  },
  ],
  coachLog: [
    { t: 0,   source: 'coach',   kind: 'cue_template', content: 'Raise your right arm out to the side. Stop at first discomfort.' },
    { t: 90,  source: 'coach',   kind: 'speak',        content: 'Good pace. Keep the elbow soft.' },
    { t: 165, source: 'coach',   kind: 'cue_template', content: 'Lower slowly. Pause — did you feel discomfort?' },
    { t: 180, source: 'patient', kind: 'report',       content: 'Patient report: Sharp pain at 60° (6/10)' },
    { t: 200, source: 'coach',   kind: 'llm_reply',    content: "Pain above 5/10 at arc — reducing target by 5°. Continuing with modified range." },
    { t: 300, source: 'patient', kind: 'report',       content: 'Patient report: Fatigue in shoulder (4/10)' },
    { t: 340, source: 'engine',  kind: 'tool_call',    content: 'end_session', tool: 'end_session', args: {} },
  ],
  synthetic: false,
}

const JAMES_PLANS: PlanVersion[] = [
  {
    version: 1,
    date: '2025-09-02',
    exercise: 'Shoulder abduction (R)',
    settings: [
      { setting: 'Target ROM band',     current: '55–80°', proposed: '55–75°', unit: '°',    min: 40, max: 110, changed: true  },
      { setting: 'Reps prescribed',     current: 8,        proposed: 6,        unit: 'reps', min: 4,  max: 12,  changed: true  },
      { setting: 'Trunk limit',          current: 8,        proposed: 7,        unit: '°',    min: 3,  max: 10,  changed: true  },
      { setting: 'Pain stop threshold',  current: '7/10',   proposed: '5/10',                          changed: true  },
    ],
    notes: 'Post-rotator cuff repair, 6 weeks post-op. Patient reports pain at arc.',
    approvedBy: 'Dr. Chen',
    approvedAt: '2025-09-02T09:30:00Z',
  },
]

const JAMES_RTM: RTMRecord = {
  patientId: 'james-t',
  month: '2025-09',
  daysWithData: 6,
  daysScheduled: 11,
  reviewMinutes: 6,
  sessionCount: 6,
}

const JAMES: PatientData = {
  patient: {
    id: 'james-t',
    name: 'James T.',
    condition: 'Rotator Cuff Repair',
    status: 'watch',
    urgency: 1,
    flagDetail: '5 missed sessions · pain reported 6/10',
    lastSession: '3 days ago',
    sessionCount: 6,
    weeksActive: 3,
    nextScheduled: '2025-09-25',
    activePlanVersion: 1,
  },
  sessions: JAMES_SESSIONS,
  latestHandoff: JAMES_HANDOFF,
  schedule: JAMES_SCHEDULE,
  plans: JAMES_PLANS,
  rtm: JAMES_RTM,
}

// ── Sarah K. — ACL Reconstruction ────────────────────────────────────────

const SARAH_SESSIONS: SessionSummary[] = [
  { session: 1, date: '2025-09-01', medianPeakDeg: 75,  trunkMeanDeg: 2, validReps: 8,  prescribedReps: 10, completed: true, synthetic: true  },
  { session: 2, date: '2025-09-03', medianPeakDeg: 82,  trunkMeanDeg: 2, validReps: 9,  prescribedReps: 10, completed: true, synthetic: true  },
  { session: 3, date: '2025-09-05', medianPeakDeg: 88,  trunkMeanDeg: 3, validReps: 10, prescribedReps: 10, completed: true, synthetic: true  },
  { session: 4, date: '2025-09-08', medianPeakDeg: 93,  trunkMeanDeg: 2, validReps: 10, prescribedReps: 10, completed: true, synthetic: true  },
  { session: 5, date: '2025-09-10', medianPeakDeg: 98,  trunkMeanDeg: 2, validReps: 10, prescribedReps: 10, completed: true, synthetic: true  },
  { session: 6, date: '2025-09-12', medianPeakDeg: 104, trunkMeanDeg: 3, validReps: 9,  prescribedReps: 10, completed: true, synthetic: true  },
  { session: 7, date: '2025-09-15', medianPeakDeg: 108, trunkMeanDeg: 2, validReps: 10, prescribedReps: 10, completed: true, synthetic: true  },
  { session: 8, date: '2025-09-17', medianPeakDeg: 113, trunkMeanDeg: 3, validReps: 10, prescribedReps: 10, completed: true, synthetic: true  },
  { session: 9, date: '2025-09-19', medianPeakDeg: 118, trunkMeanDeg: 3, validReps: 9,  prescribedReps: 10, completed: true, synthetic: false },
]

const SARAH_SCHEDULE = [
  { date: '2025-09-01', completed: true  },
  { date: '2025-09-03', completed: true  },
  { date: '2025-09-05', completed: true  },
  { date: '2025-09-08', completed: true  },
  { date: '2025-09-10', completed: true  },
  { date: '2025-09-12', completed: true  },
  { date: '2025-09-15', completed: true  },
  { date: '2025-09-17', completed: true  },
  { date: '2025-09-19', completed: true  },
  { date: '2025-09-22', completed: false },
]

const SARAH_HANDOFF: SessionHandoff = {
  sessionId: 's_009',
  patientId: 'sarah-k',
  planVersion: 1,
  exercise: 'Knee extension (L)',
  reps: { valid: 9, attempted: 10, prescribed: 10 },
  medianPeakDeg: 118,
  baselineMedianPeakDeg: 75,
  trunkDeviation: { meanDeg: 3, finalRepsDeg: 3 },
  durationSec: 380,
  timestamp: '2025-09-19T10:15:00Z',
  patientReports: [
    { t: 200.0, text: 'Mild soreness in quad', severity: 2 },
  ],
  replayMoments: [
    { t: 120.5, label: 'Full extension — rep 4', repIndex: 4 },
    { t: 310.0, label: 'Near-max ROM — rep 9',   repIndex: 9 },
  ],
  uncertainty: [
    'Knee marker partially obscured frames 400–450',
  ],
  repEvents: [
    { rep: 1,  peakDeg: 112, trunkDeg: 3, valid: true  },
    { rep: 2,  peakDeg: 115, trunkDeg: 2, valid: true  },
    { rep: 3,  peakDeg: 116, trunkDeg: 3, valid: true  },
    { rep: 4,  peakDeg: 118, trunkDeg: 2, valid: true  },
    { rep: 5,  peakDeg: 119, trunkDeg: 3, valid: true  },
    { rep: 6,  peakDeg: 120, trunkDeg: 3, valid: true  },
    { rep: 7,  peakDeg: 118, trunkDeg: 3, valid: true  },
    { rep: 8,  peakDeg: 117, trunkDeg: 4, valid: true  },
    { rep: 9,  peakDeg: 116, trunkDeg: 5, valid: false, reason: 'Trunk deviation > 4°' },
    { rep: 10, peakDeg: 115, trunkDeg: 3, valid: true  },
  ],
  coachLog: [
    { t: 0,   source: 'coach',   kind: 'cue_template', content: 'Extend your left leg to full range.' },
    { t: 60,  source: 'coach',   kind: 'speak',        content: 'Great extension. Hold at the top.' },
    { t: 180, source: 'engine',  kind: 'tool_call',    content: 'set_target', tool: 'set_target', args: { angle_band: [100, 120] } },
    { t: 200, source: 'patient', kind: 'report',       content: 'Patient report: Mild soreness in quad (2/10)' },
    { t: 300, source: 'coach',   kind: 'llm_reply',    content: "Excellent progress — you've gained 43° since your first session. This is well within expected ACL recovery trajectory." },
    { t: 380, source: 'engine',  kind: 'tool_call',    content: 'end_session', tool: 'end_session', args: {} },
  ],
  synthetic: false,
}

const SARAH_PLANS: PlanVersion[] = [
  {
    version: 1,
    date: '2025-09-01',
    exercise: 'Knee extension (L)',
    settings: [
      { setting: 'Target ROM band', current: '100–120°', proposed: '100–120°', unit: '°',    min: 50, max: 130, changed: false },
      { setting: 'Reps prescribed',  current: 10,         proposed: 10,         unit: 'reps', min: 4,  max: 12,  changed: false },
      { setting: 'Trunk limit',       current: 4,          proposed: 4,          unit: '°',    min: 3,  max: 10,  changed: false },
    ],
    notes: 'Post-ACL reconstruction, 8 weeks post-op.',
    approvedBy: 'Dr. Chen',
    approvedAt: '2025-09-01T08:00:00Z',
  },
]

const SARAH_RTM: RTMRecord = {
  patientId: 'sarah-k',
  month: '2025-09',
  daysWithData: 9,
  daysScheduled: 10,
  reviewMinutes: 8,
  sessionCount: 9,
}

const SARAH: PatientData = {
  patient: {
    id: 'sarah-k',
    name: 'Sarah K.',
    condition: 'ACL Reconstruction',
    status: 'good',
    urgency: 3,
    flagDetail: 'On track · 9/10 sessions completed',
    lastSession: 'Yesterday',
    sessionCount: 9,
    weeksActive: 3,
    nextScheduled: '2025-09-22',
    activePlanVersion: 1,
  },
  sessions: SARAH_SESSIONS,
  latestHandoff: SARAH_HANDOFF,
  schedule: SARAH_SCHEDULE,
  plans: SARAH_PLANS,
  rtm: SARAH_RTM,
}

// ── Elena V. — Parkinson's Disease ───────────────────────────────────────

const ELENA_SESSIONS: SessionSummary[] = [
  { session: 9,  date: '2025-08-05', medianPeakDeg: 62, trunkMeanDeg: 4, validReps: 9,  prescribedReps: 12, completed: true, synthetic: true  },
  { session: 10, date: '2025-08-07', medianPeakDeg: 63, trunkMeanDeg: 4, validReps: 10, prescribedReps: 12, completed: true, synthetic: true  },
  { session: 11, date: '2025-08-11', medianPeakDeg: 64, trunkMeanDeg: 4, validReps: 11, prescribedReps: 12, completed: true, synthetic: true  },
  { session: 12, date: '2025-08-13', medianPeakDeg: 65, trunkMeanDeg: 4, validReps: 10, prescribedReps: 12, completed: true, synthetic: true  },
  { session: 13, date: '2025-08-16', medianPeakDeg: 65, trunkMeanDeg: 3, validReps: 11, prescribedReps: 12, completed: true, synthetic: true  },
  { session: 14, date: '2025-08-19', medianPeakDeg: 66, trunkMeanDeg: 3, validReps: 12, prescribedReps: 12, completed: true, synthetic: true  },
  { session: 15, date: '2025-08-22', medianPeakDeg: 67, trunkMeanDeg: 3, validReps: 11, prescribedReps: 12, completed: true, synthetic: true  },
  { session: 16, date: '2025-08-25', medianPeakDeg: 67, trunkMeanDeg: 4, validReps: 12, prescribedReps: 12, completed: true, synthetic: true  },
  { session: 17, date: '2025-08-28', medianPeakDeg: 68, trunkMeanDeg: 3, validReps: 11, prescribedReps: 12, completed: true, synthetic: true  },
  { session: 18, date: '2025-09-01', medianPeakDeg: 69, trunkMeanDeg: 3, validReps: 12, prescribedReps: 12, completed: true, synthetic: true  },
  { session: 19, date: '2025-09-05', medianPeakDeg: 70, trunkMeanDeg: 4, validReps: 11, prescribedReps: 12, completed: true, synthetic: true  },
  { session: 20, date: '2025-09-09', medianPeakDeg: 70, trunkMeanDeg: 3, validReps: 12, prescribedReps: 12, completed: true, synthetic: true  },
  { session: 21, date: '2025-09-15', medianPeakDeg: 71, trunkMeanDeg: 4, validReps: 11, prescribedReps: 12, completed: true, synthetic: true  },
  { session: 22, date: '2025-09-22', medianPeakDeg: 72, trunkMeanDeg: 3, validReps: 12, prescribedReps: 12, completed: true, synthetic: false },
]

const ELENA_SCHEDULE = [
  { date: '2025-09-01', completed: true  },
  { date: '2025-09-03', completed: false },
  { date: '2025-09-05', completed: true  },
  { date: '2025-09-07', completed: false },
  { date: '2025-09-09', completed: true  },
  { date: '2025-09-11', completed: false },
  { date: '2025-09-13', completed: false },
  { date: '2025-09-15', completed: true  },
  { date: '2025-09-17', completed: false },
  { date: '2025-09-19', completed: false },
  { date: '2025-09-22', completed: true  },
]

const ELENA_HANDOFF: SessionHandoff = {
  sessionId: 's_022',
  patientId: 'elena-v',
  planVersion: 1,
  exercise: 'Standing balance — tandem stance',
  reps: { valid: 11, attempted: 12, prescribed: 12 },
  medianPeakDeg: 72,
  baselineMedianPeakDeg: 62,
  trunkDeviation: { meanDeg: 3, finalRepsDeg: 4 },
  durationSec: 520,
  timestamp: '2025-09-22T09:45:00Z',
  patientReports: [
    { t: 260.0, text: 'Feeling steady today', severity: 1 },
  ],
  replayMoments: [
    { t: 155.0, label: 'Best balance hold — 12s', repIndex: 5  },
    { t: 445.0, label: 'Near-fall recovered',      repIndex: 11 },
  ],
  uncertainty: [
    'Lighting variation caused ankle tracking error (6% frames)',
  ],
  repEvents: [
    { rep: 1,  peakDeg: 70, trunkDeg: 3, valid: true  },
    { rep: 2,  peakDeg: 71, trunkDeg: 3, valid: true  },
    { rep: 3,  peakDeg: 72, trunkDeg: 4, valid: true  },
    { rep: 4,  peakDeg: 70, trunkDeg: 3, valid: true  },
    { rep: 5,  peakDeg: 73, trunkDeg: 3, valid: true  },
    { rep: 6,  peakDeg: 71, trunkDeg: 4, valid: true  },
    { rep: 7,  peakDeg: 72, trunkDeg: 3, valid: true  },
    { rep: 8,  peakDeg: 70, trunkDeg: 3, valid: true  },
    { rep: 9,  peakDeg: 71, trunkDeg: 4, valid: true  },
    { rep: 10, peakDeg: 73, trunkDeg: 3, valid: true  },
    { rep: 11, peakDeg: 68, trunkDeg: 7, valid: false, reason: 'Sway > threshold' },
    { rep: 12, peakDeg: 72, trunkDeg: 3, valid: true  },
  ],
  coachLog: [
    { t: 0,   source: 'coach',   kind: 'cue_template', content: 'Stand with your right foot in front. Arms at sides.' },
    { t: 90,  source: 'coach',   kind: 'speak',        content: 'Hold that position. Eyes forward.' },
    { t: 200, source: 'engine',  kind: 'tool_call',    content: 'set_target', tool: 'set_target', args: { hold_duration: 12, sway_limit: 5 } },
    { t: 260, source: 'patient', kind: 'report',       content: 'Patient report: Feeling steady today (1/10)' },
    { t: 310, source: 'coach',   kind: 'llm_reply',    content: "Great stability. Your sway has reduced 40% since session 9. This is consistent with expected outcomes for Parkinson's gait training." },
    { t: 520, source: 'engine',  kind: 'tool_call',    content: 'end_session', tool: 'end_session', args: {} },
  ],
  synthetic: false,
}

const ELENA_PLANS: PlanVersion[] = [
  {
    version: 1,
    date: '2025-07-01',
    exercise: 'Standing balance — tandem stance',
    settings: [
      { setting: 'Hold duration target', current: '10s', proposed: '15s', unit: 's',    min: 5,  max: 30, changed: true  },
      { setting: 'Reps prescribed',       current: 12,    proposed: 12,    unit: 'reps', min: 4,  max: 20, changed: false },
      { setting: 'Sway limit',             current: 5,     proposed: 5,     unit: '°',    min: 3,  max: 10, changed: false },
    ],
    notes: "Parkinson's gait — 12 weeks into program. Progressing to longer holds.",
    approvedBy: 'Dr. Chen',
    approvedAt: '2025-07-01T10:00:00Z',
  },
]

const ELENA_RTM: RTMRecord = {
  patientId: 'elena-v',
  month: '2025-09',
  daysWithData: 5,
  daysScheduled: 11,
  reviewMinutes: 9,
  sessionCount: 22,
}

const ELENA: PatientData = {
  patient: {
    id: 'elena-v',
    name: 'Elena V.',
    condition: "Parkinson's Disease",
    status: 'good',
    urgency: 2,
    flagDetail: 'Stable · 22 sessions, sway improving',
    lastSession: 'Today',
    sessionCount: 22,
    weeksActive: 12,
    nextScheduled: '2025-09-25',
    activePlanVersion: 1,
  },
  sessions: ELENA_SESSIONS,
  latestHandoff: ELENA_HANDOFF,
  schedule: ELENA_SCHEDULE,
  plans: ELENA_PLANS,
  rtm: ELENA_RTM,
}

// ── Exports ───────────────────────────────────────────────────────────────

const ALL: PatientData[] = [MARCUS, JAMES, SARAH, ELENA]  // pre-sorted by urgency

export const PATIENTS_SORTED: Patient[] = ALL.map(d => d.patient)

export function getPatientData(id: string): PatientData | undefined {
  return ALL.find(d => d.patient.id === id)
}

export { ALL as ALL_PATIENT_DATA }
