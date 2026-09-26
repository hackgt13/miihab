# Voice Service Architecture

## Layer map

```
┌──────────────────────────────────────────────────────┐
│                   Unity (client)                     │
│  Mic PCM → WS /voice                                │
│  WS /voice → speaker PCM + events                  │
└─────────────────────┬────────────────────────────────┘
                      │ WebSocket  ws://localhost:8769/voice
┌─────────────────────▼────────────────────────────────┐
│               server.ts  (entry point)               │
│  http.createServer + WebSocketServer                 │
│  Routes /health → healthController                   │
│  Routes /voice  → sessionController                  │
└──────┬─────────────────────────────────┬─────────────┘
       │ controllers/                    │ services/
┌──────▼──────────────┐   ┌─────────────▼─────────────┐
│ sessionController   │   │ PatientService  (Supabase) │
│  – handshake        │   │  patients, sessions        │
│  – audio relay      │   │  pain_logs, milestones     │
│  – session lifecycle│   │  plan_review_requests      │
├─────────────────────┤   ├───────────────────────────┤
│ healthController    │   │ CoordinatorService (read)  │
│  – GET /health      │   │  care plan, measured reps  │
│                     │   ├───────────────────────────┤
│                     │   │ AnalyticsService           │
│                     │   │  pain trend, progression   │
└──────┬──────────────┘   │  streak, session summary   │
       │                  ├───────────────────────────┤
       │                  │ ToolService                │
       │                  │  dispatches ElevenLabs     │
       │                  │  tool calls to the above   │
       │                  └─────────────┬─────────────┘
       │                                │
┌──────▼────────────────────────────────▼─────────────┐
│                 conversation.ts                      │
│  ElevenLabs Conversational AI WebSocket bridge       │
│  Unity audio → ElevenLabs (user_audio_chunk)         │
│  ElevenLabs audio → Unity (audio event)              │
│  Tool calls ↔ ToolService.dispatch()                 │
└──────────────────────────┬───────────────────────────┘
                           │ wss://api.elevenlabs.io/v1/convai/conversation
┌──────────────────────────▼───────────────────────────┐
│              ElevenLabs Conversational AI             │
│  STT → LLM (Alex PT persona) → TTS                  │
│  Client-tool calls → backend executes → Supabase     │
└──────────────────────────────────────────────────────┘
```

## Data ownership

One patient record with three owners. Each fact has one writer, and nothing copies another owner's facts.

| Fact | Owner (only writer) | Store | Alex |
|---|---|---|---|
| Care plan: exercise, side, target range, reps, hold | Physician, approved in the portal | Coordinator `local-data/plans` (immutable versions) | Reads |
| Reps, range, compensation, tracking quality | Measurement engine (pose → `coordinator/exercise/`) | Coordinator `local-data/sessions` | Reads |
| Pain, goals, profile, milestones, plan review requests | Patient, recorded by Alex | Supabase (this service) | Writes |

- **Join key:** `sessions.exercise_ids` holds the coordinator exercise ids measured during the conversation, and `sessions.plan_version` the plan they ran under.
- **Alex never changes the plan.** A pain rise or "too easy" becomes a `plan_review_requests` row; the plan changes only when the physician approves a new version in the portal.
- **Alex never produces rep counts.** It quotes `get_exercise_results`; if the coordinator is unreachable it says nothing numeric.
- **Access:** RLS on with no policies — only the service_role key (this backend) can read or write. The anon key sees nothing.

## Layer responsibilities

| Layer | File(s) | Owns |
|---|---|---|
| Entry | `server.ts` | HTTP + WS server creation, service wiring |
| Config | `backend/config/index.ts` | All `process.env` reads, typed config object |
| Controllers | `backend/controllers/` | Protocol handling, request/session lifecycle |
| Services | `backend/services/` | Business logic, Supabase I/O, tool dispatch |
| Conversation | `backend/conversation.ts` | ElevenLabs WS wire protocol |
| Prompts | `backend/prompts.ts` | PT system prompt + first message |
| Types | `backend/types.ts` | Shared TypeScript interfaces |

## Session lifecycle

```
Unity                        server.ts              ElevenLabs
  │── WS connect ──────────────▶│
  │── session_start ────────────▶│ createSession()
  │                              │── WS connect ──▶│
  │◀── session_started ──────────│◀─ conv open ────│
  │── audio chunks ─────────────▶│── audio ───────▶│ STT→LLM→TTS
  │◀── audio chunks ─────────────│◀─ audio ────────│
  │◀── transcript ───────────────│◀─ transcript ───│
  │                              │◀─ tool_call ────│
  │                              │   dispatch()
  │                              │   Supabase write
  │                              │── tool_result ─▶│
  │◀── milestone / summary ──────│                 │
  │── session_end ──────────────▶│ endSession()
  │── WS close ─────────────────▶│── WS close ────▶│
```

## WebSocket contract (Unity side)

### Unity → server
```jsonc
{ "type": "session_start",  "patient_id": "<uuid>" }
{ "type": "audio",          "data": "<base64 16kHz mono PCM>" }
{ "type": "session_end" }
```

### server → Unity
```jsonc
{ "type": "session_started" }
{ "type": "audio",          "data": "<base64 PCM>" }
{ "type": "transcript",     "role": "agent"|"user", "text": "..." }
{ "type": "interrupt" }
{ "type": "milestone",      "data": { "description": "...", "category": "..." } }
{ "type": "session_summary","data": { "exercises": [...], "pain_delta": -1.2 } }
{ "type": "error",          "message": "..." }
```

## First-time setup

```bash
cd voice
npm ci
cp .env.example .env      # fill in ELEVENLABS_API_KEY, SUPABASE_URL, SUPABASE_SERVICE_KEY; COORDINATOR_URL defaults to the local coordinator
# Run supabase/schema.sql in the Supabase SQL editor
node --env-file=.env backend/agent.ts   # creates the agent, prints ELEVENLABS_AGENT_ID
# Add ELEVENLABS_AGENT_ID to .env
npm test                  # offline: fake coordinator + in-memory store
npm run update-agent      # push the new prompt and tools to the existing ElevenLabs agent
npm start
```
