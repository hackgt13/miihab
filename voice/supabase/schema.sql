-- Virtual PT Database Schema
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

-- The backend uses the service_role key which bypasses RLS entirely.
-- These permissive policies are a safety net; tighten them if you ever
-- expose Supabase directly to end-user clients.
create policy "backend full access" on patients
    using (true) with check (true);

-- ── sessions ──────────────────────────────────────────────────────────────────
create table if not exists sessions (
    id            uuid        primary key default gen_random_uuid(),
    patient_id    uuid        not null references patients (id) on delete cascade,
    started_at    timestamptz default now(),
    ended_at      timestamptz,
    pain_start    real,
    pain_end      real,
    notes         text
);

create index if not exists sessions_patient_id_idx on sessions (patient_id);
create index if not exists sessions_started_at_idx on sessions (started_at desc);

alter table sessions enable row level security;

create policy "backend full access" on sessions
    using (true) with check (true);

-- ── exercise_logs ─────────────────────────────────────────────────────────────
create table if not exists exercise_logs (
    id            uuid        primary key default gen_random_uuid(),
    session_id    uuid        not null references sessions (id) on delete cascade,
    patient_id    uuid        not null references patients (id) on delete cascade,
    exercise_name text        not null,
    sets          integer,
    reps          integer,
    duration_sec  integer,
    pain_during   real,
    notes         text,
    logged_at     timestamptz default now()
);

create index if not exists exercise_logs_patient_idx on exercise_logs (patient_id);
create index if not exists exercise_logs_session_idx on exercise_logs (session_id);

alter table exercise_logs enable row level security;

create policy "backend full access" on exercise_logs
    using (true) with check (true);

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

create policy "backend full access" on pain_logs
    using (true) with check (true);

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

create policy "backend full access" on milestones
    using (true) with check (true);
