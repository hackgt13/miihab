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

if ! pose_ready; then
  nohup "$node_exe" coordinator/server.ts >local-data/logs/pose-bridge.log 2>&1 </dev/null &
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

case "${1:-club}" in
  club) app="$repo/native/build/Kinesthetic Club Motion.app" ;;
  bowling) app="$repo/native/build/Kinesthetic Bowling Motion.app" ;;
  *) print -u2 'Motion activity must be club or bowling.'; exit 1 ;;
esac
if [[ -d "$app" ]] && golf_ready; then /usr/bin/open -a "$app"; fi
