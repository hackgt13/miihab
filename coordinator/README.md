# Local pose bridge

Requires Node 24. On this Mac, prepend `/opt/homebrew/opt/node@24/bin` to PATH when running npm; the older system npm environment otherwise selects Node 23.

```sh
PATH="/opt/homebrew/opt/node@24/bin:$PATH" npm install
PATH="/opt/homebrew/opt/node@24/bin:$PATH" npm start
PATH="/opt/homebrew/opt/node@24/bin:$PATH" npm test
```

Loopback only: `http://localhost:8766/`. Static capture page plus WebSocket `/pose?role=producer|viewer`. One producer; multiple viewers. Browser origins are restricted to the local capture pages. This is the Mac integration spike, not the final phone/LAN pairing service.

The server validates envelope/array shape, rejects duplicate/out-of-order sequence numbers, records raw pose envelopes under ignored `local-data/sessions`, and relays current data. Unity owns per-joint validity and freshness. Coordinates may be null to represent invalid observations; null is never converted into measured zero. The recorder stores pose observations, not video.

## Recorded transport fixture

```sh
node replay-fixture.ts /absolute/path/to/capture.json
```

Uses original source timing and labels packets `recorded-video` even when the export originated from a camera. An optional repeat count (1–10) helps bounded transport inspection; a repeated timeline is not a continuous replay file.

Tests use a temporary recording directory and loopback port 18766. Device clocks, phone JPEG transport, care plans, measurements, clinical storage, and Unity golf-state relay are later increments.

## Shoulder-raise measurement (`measurement.ts`)

Deterministic rep counting from raw MediaPipe world landmarks: calibrate a resting torso reference, then
`rest → rep → target held (holdMs) → back to rest`. Each rep is valid or invalid with a reason
(`tracking_lost`, `did_not_reach_target`, `trunk_compensation`, `too_fast`). Thresholds are engineering
settings for the rehearsed setup, not clinical standards. Angle = shoulder→elbow vs calibrated torso-down axis.

Live on the pose bridge:

```sh
curl -X POST localhost:8766/exercise/start -d '{"side":"right","targetDeg":80,"prescribedReps":8,"planVersion":1}'
# ws://localhost:8766/exercise?role=viewer  → exercise.sample (every frame), exercise.event, exercise.summary
curl localhost:8766/exercise            # running state + summary so far
curl -X POST localhost:8766/exercise/stop  # final summary, saved to local-data/sessions/exercise-<id>.summary.json
```

Exercise messages never go to `/pose` viewers or into the pose recording (Unity's replay accepts only `pose.frame`).
Offline: `node measure-capture.ts <capture.json|session.jsonl> --side right --target 80 [--events]`.
Tests: `measurement.test.ts` (synthetic bodies with known angles), `exercise.test.ts` (server end to end).

## Care plans and clinician portal

`plans.ts` stores immutable plan versions in `local-data/plans/plan-v<n>.json` (v1 is seeded). Approving a change
creates the next version and never edits earlier ones. `/exercise/start {}` pins the active plan; `{planVersion:n}`
pins a specific one. A session's thresholds (target, reps, hold, trunk limit) come from its pinned plan, so a
mid-session approval never changes a running session.

Portal: http://localhost:8766/portal/ — progress vs. the labeled synthetic baseline, session evidence (sessions from
the simulators are marked "simulated input"), the functional-change review flag (explicit rule: reach +10° from
baseline, trunk compensation in the latest session, reported stiffness), and plan approval with a required rationale.

API: `GET /api/plans`, `GET /api/plans/active`, `POST /api/plans {exercise, rationale, coachingNote, expectedActiveVersion}`,
`GET /api/sessions`, `GET /api/history`. Unity's `Rehab.unity` fetches the active plan when a session starts.

## Movement replay (portal)

`GET /api/sessions/<exerciseId>/replay` returns the session's recorded landmarks (~15 fps), per-frame arm elevation and
trunk lean computed by `measurement.ts` with the session's own saved calibration, and its rep boundaries. The portal's
**Replay** button draws the recorded skeleton (measured arm highlighted), the elevation trace against the plan target,
and each rep (not-counted reps shaded and labeled). Deep link: `/portal/?replay=<exerciseId>&rep=<n>`.

## Demo launcher

`zsh scripts/demo.sh` starts the services and checks pose bridge, golf relay (+ AirPod stream, Unity host, headsets),
portal and active plan, Unity, the Quest host address vs. this Mac's current Wi-Fi address, relay LAN exposure, the
APK and a USB-connected Quest. `--open` opens the portal; `--reset` archives plans after v1 and recorded exercise
sessions (moved to `local-data/archive/`, never deleted) for a clean v1 → v2 walkthrough.
