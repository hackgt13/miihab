# Adding an activity

The contract both halves of the app agree on. Read this before adding a game, an exercise or a scene.

## Three nouns, and which is which

- **Exercise** — the *measured* unit. Server-side TypeScript, versioned, deterministic, replayable from
  recorded frames. Owns signal extraction, calibration, the rep rule, validity and the summary. Has no
  visuals. `shoulder-raise.v1`. **This is what a physician prescribes.**
- **Venue** — the *place*. Currently just a string on an activity; there is no venue registry yet.
- **Activity** — the *atomic unit*, and the only thing a patient picks. Binds one venue, N exercises and
  a presentation. Has **no scoring authority**: it renders and emits events.

The test for which is which: *would changing this require the physician to re-approve the plan?*
Yes → Exercise. No → Activity or Venue.

Activity → Venue is N:1. Activity → Exercise is N:M. Venue and Exercise never reference each other.

---

## To add an activity

**1. Add an entry to `coordinator/activities.json`.** That file is the single source of truth; Unity
reads a generated copy. Every field is required and validated at load:

```json
{
  "id": "bowling.adaptive",          // stable id, "family.name". Written into approved plans.
  "displayName": "Bowling",
  "tagline": "Take your turn. Knock them down.",
  "category": "play",                 // therapy | play
  "scene": "Bowling",                 // a real Unity scene name
  "venue": "bowling-alley",
  "exerciseKinds": [],                // [] means it measures nothing clinical
  "requires": ["pose", "imu"],        // gallery gating only, NOT the in-activity readiness check
  "subjects": 2,                      // golf and bowling record two people; therapy records one
  "prescribable": false,
  "loadingMessage": "Heading to the lanes…",
  "music": null,                      // Resources path, or null
  "help": { "title": "…", "steps": [ {"step": "…", "copy": "…"}, … ] }   // exactly 3 steps
}
```

**`id` is immutable in practice.** It is written into approved plans, and `PlanStore` writes with
`flag: 'wx'` — plan versions never change once approved. Renaming an id is a migration, not an edit.

**2. Sync it into Unity.** `Kinesthetic/Activities/Sync catalog from coordinator` in the editor menu.
Generated, never hand-edited; the next sync overwrites it.

**3. Add the scene to Build Settings.** Otherwise `LoadActivity` reports it unavailable. Keep
`MainMenu` at index 0 — per `AGENTS.md`, scene index 0 is what a build boots.

**4. Implement `IActivity` on the scene's main behaviour.**

```csharp
public interface IActivity
{
    string ActivityId { get; }                 // must resolve in ActivityCatalog
    bool IsRunning { get; }                    // a session or round is in progress
    bool IsBusy { get; }                       // a start/stop is in flight; do not disturb
    event Action<string> Completed;            // fires once, carries the recorded session id
    IEnumerator RequestExit(Action<bool> ok);  // MAY FAIL — false means "still here, don't unload me"
}
```

`RequestExit` is a coroutine that can fail for a reason: a therapy session round-trips
`/exercise/stop` and must refuse to leave if the measurement service does not answer, or the set is
silently lost. A game with nothing server-side to close still must not be unloaded mid-POST.

**5. Emit one session record when the activity reaches its terminal condition.**
POST the envelope to `http://127.0.0.1:8766/activity/session`. The coordinator validates and stores
it; `GET /api/activity-sessions` serves them back. See `coordinator/activity.ts` for the shape.

What belongs in the envelope: **dose** (prescribed/attempted/valid), **tracking quality**, a
**normalised flag** vocabulary, and a `primaryMetric`. What does not: anything scored. A stroke count
and a rep count are not comparable numbers, so activity-specific figures go in
`payload: {kind, schemaVersion, data}` behind a versioned discriminant, and nothing upstream parses
them.

**Do not write session records to `Application.persistentDataPath`.** It differs between the Editor
and a built Player, and the coordinator cannot read either, so it can never be the clinical record.

**6. Take sensors from the hub, never construct a client.** `SensorHub.Ensure()` in `Start()`, then
read through a property so a reconnect cannot leave you holding a dead socket:

```csharp
GolfMotionClient client => SensorHub.Instance?.MotionFor(motionUrl);
```

One connection per channel lives for the app's lifetime, so navigating between activities does not
tear down the camera or the AirPods. The hub also counts reconnects, so report tracking quality as the
delta across your own session rather than by owning the socket.

---

## To add an exercise

**1. Write an `ExerciseKind` in `coordinator/exercise/`.** It supplies geometry only — the rep state
machine, calibration, validity rules and summary all come from `kind.ts` and must not need editing:

```ts
export interface ExerciseKind<R extends string = string> {
  readonly id: string;                 // 'shoulder-raise.v1' — version it
  readonly algorithmVersion: string;
  readonly requires: readonly ChannelId[];   // 'pose' | 'imu'
  readonly compensationReason: R;      // this exercise's own name for cheating
  readonly compensationKey: string;    // its summary key, e.g. 'trunkDeviation'
  readonly defaults: Partial<RepParams>;
  readonly limits: Readonly<Record<string, readonly [number, number]>>;
  readonly measurementNote: string;    // state the real accuracy, not the optimistic one
  landmarks(params): readonly number[];
  observe(input, params, reference): Observation | null;
  calibration(reference): Record<string, unknown>;
}
```

**If you have to edit `kind.ts` to add an exercise, the interface is wrong — fix the interface.**

**2. Register it** in `coordinator/exercise/registry.ts`: one import, one array entry.

**3. Declare it on an activity** in `activities.json`, or nothing can run it — the session envelope is
rejected at save time if an activity claims an exercise it does not declare.

**4. Decide its normative comparison** in `norms.ts` `EXERCISE_MOVEMENT`. `null` is a valid, honest
answer meaning "no normative table gathered". **Absence is not** — it silently drops the comparison.

### The division of labour between sensors

From `agents/activity-plan.md`: **IMU owns "how high". Camera owns "which direction, and did you
cheat."** Inclination from gravity is yaw-free and essentially exact; a monocular pose estimate of the
same angle is neither. But inclination cannot tell flexion from abduction, and cannot see someone
leaning to buy height. There is no fusion beyond the shared clock in `hostclock.ts`.

Measure magnitude relative to the **calibrated resting reference**, not an absolute frame. That makes
the measurement independent of device axis convention: a wrong guess costs sensitivity, never
correctness.

### Scoring is server-side. Written rule.

`measurement.ts` is pure, versioned and replayable, so a recorded session can be re-scored when an
algorithm version changes. Client-side scoring cannot be replayed, and IL2CPP strips the reflection
that C#-side JSON deserialisation depends on, so it is the code most likely to silently return null on
the headset. The one carve-out: a game may *predict* anything needing sub-100ms response — golf's ball
must launch on the swing — but that prediction is discardable and never the recorded number.

---

## Invariants the tests enforce

`coordinator/invariants.test.ts` asserts the seams, because these are cross-references nothing owns:

- every registry exercise is offered by some activity
- every registry exercise has an explicit normative decision
- every movement mapped to has a table in `NORMS`
- every activity names a real exercise
- every activity's scene exists on disk
- ids are stable identifiers, not display text

If you add a thing in one place and forget the other, these fail at the source rather than at a
patient's session.

---

## Things not to do

- **Do not hardcode an activity id or scene name outside the catalog.** `ActivityNavigation` names no
  activity; keep it that way. Per-activity copy — help text, loading line, music — lives in the
  catalog so a new activity brings its own instead of adding a branch.
- **Do not put `requires[]` in the readiness path.** It is a gallery affordance for greying out a card.
  Real readiness is a content predicate — golf's gate is pose geometry plus a two-stage timed
  handshake — and each activity owns its own `IsReady`.
- **Do not hand-edit generated assets.** Scene wiring lives in `Assets/Editor/*SceneSetup.cs`; hand
  wiring is destroyed on the next `Create()`.
- **Do not invent clinical numbers.** If a normative table has not been gathered, say `null` and mark
  it provisional. See the honesty note at the top of `norms.ts`.
