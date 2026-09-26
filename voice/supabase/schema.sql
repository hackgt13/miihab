-- Virtual PT Database Schema
--
-- Holds only what the patient reports in conversation: profile, pain, milestones, and requests for the
-- physician to review the plan. Reps and range are measured by the coordinator and stay there; the care
-- plan is versioned and approved in the portal. sessions.exercise_ids joins the two records.
--
-- Access: row level security is on with NO policies, so the public (anon) and signed-in (authenticated)
-- keys can read or write nothing. Only the backend's service_role key, which bypasses RLS, has access.
-- Run via:  npm run migrate
-- Or paste into the Supabase SQL Editor and hit Run.

-- ── Extensions ────────────────────────────────────────────────────────────────
create extension if not exists "pgcrypto";

-- ── patients ──────────────────────────────────────────────────────────────────
create table if not exists patients (
    id            uuid        primary key default gen_random_uuid(),
    name          text,
    age           integer,
    condition     text,
    goals         text[]      default '{}',
    precautions   text[]      default '{}',
    created_at    timestamptz default now()
);

alter table patients enable row level security;

-- ── sessions ──────────────────────────────────────────────────────────────────
create table if not exists sessions (
    id            uuid        primary key default gen_random_uuid(),
    patient_id    uuid        not null references patients (id) on delete cascade,
    started_at    timestamptz default now(),
    ended_at      timestamptz,
    pain_start    real,
    pain_end      real,
    notes         text,
    -- Join to the measured record: coordinator exercise ids from this conversation, and the plan version
    exercise_ids  text[]      default '{}',
    plan_version  integer
);

-- Existing databases created before these columns
alter table sessions add column if not exists exercise_ids text[] default '{}';
alter table sessions add column if not exists plan_version integer;

create index if not exists sessions_patient_id_idx on sessions (patient_id);
create index if not exists sessions_started_at_idx on sessions (started_at desc);

alter table sessions enable row level security;

-- ── pain_logs ─────────────────────────────────────────────────────────────────
create table if not exists pain_logs (
    id            uuid        primary key default gen_random_uuid(),
    patient_id    uuid        not null references patients (id) on delete cascade,
    session_id    uuid        references sessions (id) on delete set null,
    level         real        not null check (level >= 0 and level <= 10),
    context       text,
    phase         text        check (phase in ('start', 'during', 'end')),
    logged_at     timestamptz default now()
);

create index if not exists pain_logs_patient_idx   on pain_logs (patient_id);
create index if not exists pain_logs_session_idx   on pain_logs (session_id);
create index if not exists pain_logs_logged_at_idx on pain_logs (logged_at);

alter table pain_logs enable row level security;

-- ── milestones ────────────────────────────────────────────────────────────────
create table if not exists milestones (
    id            uuid        primary key default gen_random_uuid(),
    patient_id    uuid        not null references patients (id) on delete cascade,
    description   text        not null,
    category      text        not null,
    logged_at     timestamptz default now()
);

create index if not exists milestones_patient_idx on milestones (patient_id);

alter table milestones enable row level security;


-- ── plan_review_requests ─────────────────────────────────────────────────────
-- Alex cannot change the care plan. It files a request; the physician decides in the portal.
create table if not exists plan_review_requests (
    id            uuid        primary key default gen_random_uuid(),
    patient_id    uuid        not null references patients (id) on delete cascade,
    session_id    uuid        references sessions (id) on delete set null,
    plan_version  integer,
    reason        text        not null,
    category      text        not null check (category in ('pain', 'too_hard', 'too_easy', 'fatigue', 'other')),
    status        text        not null default 'open' check (status in ('open', 'reviewed')),
    created_at    timestamptz default now()
);

create index if not exists plan_review_requests_patient_idx on plan_review_requests (patient_id);

alter table plan_review_requests enable row level security;

-- ── lock down databases created by the first version of this file ────────────
-- It added "backend full access" policies that applied to every role, which exposed patient data to the
-- public anon key. Remove them and revoke direct table access from the public roles.
drop policy if exists "backend full access" on patients;
drop policy if exists "backend full access" on sessions;
drop policy if exists "backend full access" on pain_logs;
drop policy if exists "backend full access" on milestones;
do $$ begin
    if to_regclass('public.exercise_logs') is not null then
        execute 'drop policy if exists "backend full access" on exercise_logs';
        execute 'revoke all on exercise_logs from anon, authenticated';
    end if;
end $$;
revoke all on patients, sessions, pain_logs, milestones, plan_review_requests from anon, authenticated;
-- exercise_logs (first version) is no longer written: reps come from the coordinator. Drop it when unused.
