# Doctor view — MiiHab clinician portal

A physician's view of a patient on the MiiHab program: the sessions the headset recorded, the plan
behind them, and what changed between plan versions.

## Running it against real data

This portal lives in the MiiHab repo under `ehr/` and is served by the coordinator itself, same origin as
the data: build it once, then open **http://127.0.0.1:8766/ehr/portal**.

    cd ehr && npm install && npm run build       # scripts/start_demo_services.sh does this if it is missing
    cd ../coordinator && npm start               # the coordinator, on 8766, serves ehr/dist at /ehr

For live-reload development `npm run dev` still works (on 5173, with `/api` proxied to the coordinator).

**Program update** (live patient): the physician edits the target, safe ceiling, reps, hold and cue, gives a
reason, and approves. That is one signed `POST /api/visit/program-update`: a new plan version, and a note on the
visit whiteboard. The headset's menu then leads with "Your plan changed", and the therapist visit explains it.

The sidebar then lists one **Live** patient above the seeded ones. That patient is read from the
coordinator every ten seconds — `src/data/coordinator.ts` maps its records into the same `PatientData`
shape `src/data/seed.ts` produces, so every component reads one shape and never has to know which it got.
With no coordinator running the portal still works; the sidebar says so and lists the seeded patients alone.

The patient's replies are four quick answers ("all good", "too easy", "too hard", "something hurt") plus
free text, not a 0–10 scale, so `src/data/coordinator.ts` reads them onto this portal's severity scale. The
trigger rule's third condition looks for the word "stiff", which only a written message will ever contain —
firing it reliably needs either a symptom scale on the headset or a rule that reads the reply kinds.

Requests go through the `/api` proxy in `vite.config.ts`. The coordinator sends no CORS headers, so a
cross-origin fetch from `:5173` is blocked even though that origin is on its allowlist — the proxy makes
the request same-origin. A deployed build should be served by the coordinator itself, the way
`coordinator/portal` already is.

No sessions to look at? `python3 ../rehabmii/scripts/seed_demo_history.py` writes a fortnight of them,
and `--clean` takes them out again. Seeded sessions are marked as such wherever they are shown.

## What is real and what is not

| On the page | Where it comes from |
| --- | --- |
| Sessions, trends, rep detail | `/api/sessions` — the headset's own measurements |
| Adherence strip, RTM days | `/api/dashboard` — the same calendar the patient's board draws |
| Plan versions and their diffs | `/api/plans` — what a clinician (or the progression rules) approved |
| Visit whiteboard notes | `/api/visit/notes` — written back, read on the patient's headset |
| Patient-reported symptoms | `/api/visit/replies` — what the patient relays at the end of a visit |
| Coach feed | `/api/coach/events` — what Alex said and heard, and the studio's rep and set reports he coached from |
| SOAP notes, RTM review minutes | `localStorage` — this portal owns them; the coordinator never sees them |

---

# React + TypeScript + Vite

This template provides a minimal setup to get React working in Vite with HMR and some Oxlint rules.

Currently, two official plugins are available:

- [@vitejs/plugin-react](https://github.com/vitejs/vite-plugin-react/blob/main/packages/plugin-react) uses [Oxc](https://oxc.rs)
- [@vitejs/plugin-react-swc](https://github.com/vitejs/vite-plugin-react/blob/main/packages/plugin-react-swc) uses [SWC](https://swc.rs/)

## React Compiler

The React Compiler is not enabled on this template because of its impact on dev & build performances. To add it, see [this documentation](https://react.dev/learn/react-compiler/installation).

## Expanding the Oxlint configuration

If you are developing a production application, we recommend enabling type-aware lint rules by installing `oxlint-tsgolint` and editing `.oxlintrc.json`:

```json
{
  "$schema": "./node_modules/oxlint/configuration_schema.json",
  "plugins": ["react", "typescript", "oxc"],
  "options": {
    "typeAware": true
  },
  "rules": {
    "react/rules-of-hooks": "error",
    "react/only-export-components": ["warn", { "allowConstantExport": true }]
  }
}
```

See the [Oxlint rules documentation](https://oxc.rs/docs/guide/usage/linter/rules) for the full list of rules and categories.
