#!/usr/bin/env python3
"""Put a patient two weeks into their program, so the menu has something to show.

A fresh machine has one plan approved a minute ago and no sessions, which is honest and useless to
demo: the plan pane's calendar is 84 blank squares, the board says "no sessions measured yet", and
nothing on either surface can show what a fortnight of work looks like.

This writes the records the coordinator would have written if the patient had been training since:
one `session-<id>.json` activity envelope and one `exercise-<id>.summary.json` per training day, in
`local-data/sessions`, plus it moves the first plan's `approvedAt` back so `programDay` matches
(coordinator/dashboard.ts counts the program from the first approved plan).

Every file it writes carries `"seeded": true`. Nothing in the coordinator reads that field — it is
there so these are distinguishable from a real patient's work, and so `--clean` can take them out
again without touching records that came from a sensor.

    python3 scripts/seed_demo_history.py                 # 15 days, five sessions a week
    python3 scripts/seed_demo_history.py --days 30
    python3 scripts/seed_demo_history.py --every-day     # no rest days: every past day gets a session
    python3 scripts/seed_demo_history.py --back calendar # ...and back to the first day the board's grid shows
    python3 scripts/seed_demo_history.py --busy 0.6      # six days in ten get two to four sessions, not one
    python3 scripts/seed_demo_history.py --clean         # remove what this wrote, restore approvedAt

`--back` seeds further than the program: a number of days, or `calendar` for the board's whole
consistency grid (coordinator/dashboard.ts: CALENDAR_WEEKS weeks, starting on a Sunday). Sessions
before the first plan carry planVersion 1 like the rest; nothing reads a plan for them.

Run it with the coordinator up or down; both read the directory on each request.
"""
import argparse, json, random, re, uuid
from datetime import datetime, timedelta, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

# The measured prescription the demo plan carries, and what a fortnight of it looks like: reach
# climbing from well short of the 45° target to just past it, reps landing more often as it goes.
PRESCRIPTION, KIND, SIDE = "arm-elevation-right", "arm-elevation.v1", "right"
TARGET_DEG, CEILING_DEG, PRESCRIBED = 45.0, 60.0, 8
REACH_FROM, REACH_TO = 31.0, 47.0
VALID_FROM, VALID_TO = 4, 8
REST_WEEKDAYS = {2, 6}          # Wednesday and Sunday off, so the grid has gaps a person recognises
CALENDAR_WEEKS = 16             # coordinator/dashboard.ts — the board's consistency grid


def iso(when: datetime) -> str:
    return when.astimezone(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.") + f"{when.microsecond // 1000:03d}Z"


def seeded(path: Path) -> bool:
    try:
        return json.loads(path.read_text()).get("seeded") is True
    except Exception:
        return False


def clean(sessions: Path, plans: Path) -> int:
    removed = 0
    for path in sorted(sessions.glob("*.json")):
        if not seeded(path):
            continue
        stem = path.name.split("-", 1)[1].removesuffix(".summary.json").removesuffix(".json")
        for sibling in sessions.glob(f"*{stem}*"):
            sibling.unlink()
            removed += 1
    for plan in sorted(plans.glob("plan-v*.json")):
        record = json.loads(plan.read_text())
        if "approvedAtBeforeSeed" in record:
            record["approvedAt"] = record.pop("approvedAtBeforeSeed")
            plan.write_text(json.dumps(record, indent=2) + "\n")
    return removed


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--days", type=int, default=15, help="how far into the program today should be (default 15)")
    parser.add_argument("--dir", default=str(ROOT / "local-data"), help="the coordinator's data directory")
    parser.add_argument("--clean", action="store_true", help="remove seeded records and restore approvedAt")
    parser.add_argument("--seed", type=int, default=13, help="jitter seed, so a rerun writes the same fortnight")
    parser.add_argument("--every-day", action="store_true", help="no rest days: a session on every past day")
    parser.add_argument("--busy", type=float, default=0.0,
                        help="share of seeded days that get 2-4 sessions (a darker square) instead of one")
    parser.add_argument("--back", default=None,
                        help="seed this many past days instead of the program so far, or 'calendar' for the board's grid")
    args = parser.parse_args()

    data = Path(args.dir)
    sessions, plans = data / "sessions", data / "plans"
    sessions.mkdir(parents=True, exist_ok=True)

    removed = clean(sessions, plans)
    if args.clean:
        print(f"removed {removed} seeded file(s); approvedAt restored")
        return

    # Day 1 of the program is `days - 1` days ago, so today is day `days`.
    today = datetime.now().replace(hour=0, minute=0, second=0, microsecond=0)
    start = today - timedelta(days=args.days - 1)

    # The first plan is what the program is counted from; later versions keep their own date. (Every version used to
    # be rewritten to the same timestamp, which scrambled the plan history and its order.)
    versions = sorted(plans.glob("plan-v*.json"), key=lambda p: int(re.search(r"(\d+)", p.stem).group(1)))
    for plan in versions[:1]:
        record = json.loads(plan.read_text())
        record.setdefault("approvedAtBeforeSeed", record["approvedAt"])
        record["approvedAt"] = iso(start.replace(hour=9, minute=12))
        plan.write_text(json.dumps(record, indent=2) + "\n")

    # How far back the sessions go: the program so far, or further when asked.
    if args.back == "calendar":
        first = today - timedelta(days=CALENDAR_WEEKS * 7 - 1)
        first -= timedelta(days=(first.weekday() + 1) % 7)        # back to the Sunday the grid starts on
        back = (today - first).days
    elif args.back is not None:
        back = int(args.back)
    else:
        back = args.days - 1
    seed_start = today - timedelta(days=back)

    rng = random.Random(args.seed)
    written = 0
    # Today is left alone: the board's "today" list, the streak and the day's own square should say what
    # the person has actually done since they woke up, not what a script decided for them.
    for offset in range(back):
        day = seed_start + timedelta(days=offset)
        if not args.every_day and day.weekday() in REST_WEEKDAYS:
            continue
        progress = offset / max(1, back - 1)
        # A busy day is two to four sessions at different hours; the grid shades by count, so it reads darker.
        count = rng.choice((2, 2, 3, 3, 4)) if rng.random() < args.busy else 1
        for hour in sorted(rng.sample((8, 9, 10, 11, 14, 16, 17, 19), count)):
            written += write_session(sessions, rng, day.replace(hour=hour, minute=rng.randrange(0, 58)), progress)

    finish = start + timedelta(days=83)
    print(f"seeded {written} session(s) from {seed_start:%d %b} to {today - timedelta(days=1):%d %b}")
    print(f"today is day {args.days} of 84 · program ends {finish:%d %b %Y}")
    print("today itself is left empty, so the streak and today's square reflect real work")


def write_session(sessions: Path, rng: random.Random, ended: datetime, progress: float) -> int:
    reach = round(REACH_FROM + (REACH_TO - REACH_FROM) * progress + rng.uniform(-1.6, 1.6), 1)
    valid = max(1, min(PRESCRIBED, round(VALID_FROM + (VALID_TO - VALID_FROM) * progress + rng.uniform(-.8, .8))))
    attempted = valid + rng.randrange(0, 3)
    peaks = sorted(round(reach + rng.uniform(-3.5, 3.5), 1) for _ in range(valid))
    identifier = str(uuid.UUID(int=rng.getrandbits(128), version=4))
    duration = 60_000 + rng.randrange(0, 240_000)

    (sessions / f"exercise-{identifier}.summary.json").write_text(json.dumps({
        "seeded": True,
        "exerciseId": identifier, "prescriptionId": PRESCRIPTION, "poseSessionId": None,
        "poseSource": "camera:studio", "simulated": False, "endedAt": iso(ended), "sensor": "camera",
        "exerciseKind": KIND, "algorithmVersion": "arm-elevation.v2", "side": SIDE, "planVersion": 1,
        "params": {"side": SIDE, "targetDeg": TARGET_DEG, "targetMaxDeg": CEILING_DEG,
                   "prescribedReps": PRESCRIBED, "planVersion": 1},
        "calibrated": True, "attempted": attempted, "valid": valid,
        "overshoots": sum(1 for p in peaks if p > CEILING_DEG), "prescribed": PRESCRIBED,
        "completed": valid >= PRESCRIBED, "invalidReasons": {},
        "medianValidPeakDeg": peaks[len(peaks) // 2], "validPeaksDeg": peaks,
        "trunkDeviation": {"meanDeg": round(rng.uniform(2, 7), 1), "maxDuringRepsDeg": round(rng.uniform(7, 12), 1)},
        "trackingLossEvents": 0, "frames": 1800 + rng.randrange(0, 900), "validFrameRatio": 1,
    }, indent=2) + "\n")

    (sessions / f"session-{identifier}.json").write_text(json.dumps({
        "seeded": True,
        "schema": "kinesthetic.activity.v1", "activitySessionId": identifier,
        "activityId": "rehab.studio", "exerciseKinds": [KIND], "venueId": "studio", "patientId": None,
        "planVersion": 1, "startedAt": iso(ended - timedelta(milliseconds=duration)), "endedAt": iso(ended),
        "durationMs": duration, "completed": valid >= PRESCRIBED,
        "subjects": [{"subjectId": "patient", "role": "patient",
                      "dose": {"prescribed": PRESCRIBED, "attempted": attempted, "valid": valid},
                      "primaryMetric": {"name": "medianValidPeak", "value": peaks[len(peaks) // 2], "unit": "deg"}}],
        "trackingQuality": {"validFrameRatio": 1, "lossEvents": 0},
        "flags": [] if valid >= PRESCRIBED else ["not_completed"],
        "payload": {"kind": KIND, "schemaVersion": "1",
                    "data": {"exerciseKind": KIND, "algorithmVersion": "arm-elevation.v2", "side": SIDE,
                             "planVersion": 1, "attempted": attempted, "valid": valid,
                             "medianValidPeakDeg": peaks[len(peaks) // 2], "validPeaksDeg": peaks}},
    }, indent=2) + "\n")
    return 1


if __name__ == "__main__":
    main()
