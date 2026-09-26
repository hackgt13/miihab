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

## Shoulder-raise measurement (`exercise/shoulder-raise.ts`)

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

## Measuring with no AirPod attached (`sim-motion.ts`)

The only live measurement path is the IMU (camera measurement is off behind `KINESTHETIC_CAMERA_MEASUREMENT`),
and `sim-rehab.ts` feeds the camera path, so nothing could exercise a real session without hardware. This stands
in for the AirPod at the relay's producer socket, which means golf, bowling and the studio all work from it.

```sh
node sim-motion.ts hand                                    # arrow keys raise and lower, space swings, q quits
node sim-motion.ts reps 8 [--peak 52] [--short] [--fast]   # a deterministic set; --short and --fast force invalid reps
node sim-motion.ts replay ../local-data/golf/patient-*.jsonl [--speed 4] [--loop]
```

`replay` plays back real recorded AirPod samples, so it reproduces genuine dropouts and reconnects rather than a
clean signal. `--channel wrist` targets bowling's channel; the default `club` is what golf and the studio read.

A rotation about the device x-axis by θ places the world vertical at `[0, sin θ, cos θ]` in the device frame, so
the commanded angle *is* the measured angle (see `verticalInDevice()`); `reps --peak 52` lands 52° in the summary.

**Every mode marks its session id `simulated-…`**, because `server.ts` derives the `simulated` flag from it,
`progression.ts` refuses to count simulated sessions, and both the portal and the patient's summary label them.
A simulator that produced records indistinguishable from a patient's would corrupt the clinical history.

**All three modes hold still at 0° before anything else.** Calibration takes the first `calibrationMs` of still
frames as the resting reference, so moving too early anchors rest to a raised limb and every later angle is
measured from the wrong place -- while the summary still reports `calibrated: true`.

## Care plans and clinician portal

`plans.ts` stores immutable plan versions in `local-data/plans/plan-v<n>.json` (v1 is seeded). Approving a change
creates the next version and never edits earlier ones. `/exercise/start {}` pins the active plan; `{planVersion:n}`
pins a specific one. A session's thresholds (target, reps, hold, trunk limit) come from its pinned plan, so a
mid-session approval never changes a running session.

Portal: http://localhost:8766/portal/ — progress vs. the labeled synthetic baseline, session evidence (sessions from
the simulators are marked "simulated input"), the functional-change review flag (explicit rule: reach +10° from
baseline, trunk compensation in the latest session, reported stiffness), and plan approval with a required rationale.

API: `GET /api/plans`, `GET /api/plans/active`, `POST /api/plans {exercise, rationale, coachingNote, expectedActiveVersion}`,
`GET /api/sessions`. Unity's `Rehab.unity` fetches the active plan when a session starts.

## Movement replay (portal)

`GET /api/sessions/<exerciseId>/replay` returns the session's recorded landmarks (~15 fps), per-frame arm elevation and
trunk lean computed by `exercise/shoulder-raise.ts` with the session's own saved calibration, and its rep boundaries. The portal's
**Replay** button draws the recorded skeleton (measured arm highlighted), the elevation trace against the plan target,
and each rep (not-counted reps shaded and labeled). Deep link: `/portal/?replay=<exerciseId>&rep=<n>`.

## Demo launcher

`zsh scripts/demo.sh` starts the services and checks pose bridge, golf relay (+ AirPod stream, Unity host, headsets),
portal and active plan, Unity, the Quest host address vs. this Mac's current Wi-Fi address, relay LAN exposure, the
APK and a USB-connected Quest. `--open` opens the portal; `--reset` archives plans after v1 and recorded exercise
sessions (moved to `local-data/archive/`, never deleted) for a clean v1 → v2 walkthrough.
