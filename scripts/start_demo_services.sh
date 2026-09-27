#!/bin/zsh
set -u

repo="${0:A:h:h}"
cd "$repo" || exit 1
mkdir -p local-data/logs

node_exe=""
for candidate in /opt/homebrew/opt/node@24/bin/node /opt/homebrew/bin/node /usr/local/bin/node; do
  if [[ -x "$candidate" ]]; then node_exe="$candidate"; break; fi
done
if [[ -z "$node_exe" ]]; then node_exe="$(command -v node 2>/dev/null)"; fi
if [[ -z "$node_exe" ]]; then print -u2 'Kinesthetic: Node.js is unavailable.'; exit 1; fi

pose_ready() { /usr/bin/curl -fsS --max-time 1 http://127.0.0.1:8766/health >/dev/null 2>&1; }
golf_ready() { /usr/bin/curl -fsS --max-time 1 http://127.0.0.1:8767/ >/dev/null 2>&1; }
voice_ready() { /usr/bin/curl -fsS --max-time 1 http://127.0.0.1:8769/health >/dev/null 2>&1; }

if ! pose_ready; then
  # --env-file-if-exists so a repo-root .env (ANTHROPIC_API_KEY for the friends
  # AI) is read whatever shell started this, and a missing file is not an error.
  # The clinician's EHR portal, served by the coordinator at http://127.0.0.1:8766/ehr/portal: build it once.
  if [[ ! -f ehr/dist/index.html && -f ehr/package.json ]] && command -v npm >/dev/null; then
    (cd ehr && npm install --no-audit --no-fund >/dev/null 2>&1 && npm run build >/dev/null 2>&1) || print -u2 -- "EHR portal build failed; run npm install && npm run build in ehr/"
  fi
  nohup "$node_exe" --env-file-if-exists=.env coordinator/server.ts >local-data/logs/pose-bridge.log 2>&1 </dev/null &
  disown
fi
if ! golf_ready; then
  # With a pairing token (written by Kinesthetic/Quest/Write host config), expose only the game-state
  # channel to the LAN so the Quest can connect; sensor channels stay loopback-only.
  if [[ -s local-data/pair-token.txt ]]; then
    KINESTHETIC_GOLF_HOST=0.0.0.0 KINESTHETIC_PAIR_TOKEN="$(<local-data/pair-token.txt)" \
      nohup "$node_exe" coordinator/golf-relay.ts >local-data/logs/golf-relay.log 2>&1 </dev/null &
  else
    nohup "$node_exe" coordinator/golf-relay.ts >local-data/logs/golf-relay.log 2>&1 </dev/null &
  fi
  disown
fi

for attempt in {1..30}; do
  if pose_ready && golf_ready; then break; fi
  sleep 0.1
done

# This Mac's AirPods (Mac 1). Either Motion app feeds the relay the same way (golf-relay.ts files a local stream as
# Mac 1 whichever app it comes from), so the activity's name no longer picks the app:
#   - one already running is left alone. `open -a` on a running app "reopens" it, and the app answers a reopen by
#     pulling its window in front of everything (ClubMotionBridge.swift), which covered the headset cast on every
#     set; and opening Bowling Motion beside Club Motion made Club Motion pause, so Mac 1 went silent for bowling.
#   - none running: start Club Motion in the background (-g), streaming but never in front of the cast.
case "${1:-club}" in club|bowling) ;; *) print -u2 'Motion activity must be club or bowling.'; exit 1 ;; esac
app="$repo/native/build/Kinesthetic Club Motion.app"
if [[ -d "$app" ]] && golf_ready && ! /usr/bin/pgrep -x ClubMotionBridge >/dev/null; then /usr/bin/open -g -a "$app"; fi

# Alex, the ElevenLabs voice PT behind the studio coach. Needs voice/.env (ElevenLabs + Supabase keys).
if [[ -f voice/.env ]] && ! voice_ready; then
  (cd voice && nohup "$node_exe" --env-file=.env --env-file-if-exists=../.env server.ts >../local-data/logs/voice.log 2>&1 </dev/null &)
fi
