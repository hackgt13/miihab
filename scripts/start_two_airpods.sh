#!/bin/zsh
# Two AirPod pairs need two Apple hosts: one Mac reads one CMHeadphoneMotionManager stream.
# This Mac (the receiver) runs Unity, the coordinator and the golf relay, and streams its own pair on the relay's
# club channel. The other Mac streams its pair to this relay on the wrist channel. The channel names are transport
# only: which pair measures the limb is settled at the start of each set (AGENTS.md, Sensors).
#
#   zsh scripts/start_two_airpods.sh receive            this Mac: relay on the network, Club Motion, print what to type
#   zsh scripts/start_two_airpods.sh receive --wait     ...and wait until both pairs are streaming
#   zsh scripts/start_two_airpods.sh send <ip> <token>  the other Mac: point Bowling Motion at the receiver and open it
set -u
repo="${0:A:h:h}"; cd "$repo" || exit 1
ok()   { print -P "  %F{green}✓%f $1"; }
warn() { print -P "  %F{yellow}!%f $1"; }
bad()  { print -P "  %F{red}✗%f $1"; }
get() { /usr/bin/curl -fsS --max-time 2 "$@" 2>/dev/null; }
token_file=local-data/pair-token.txt
bowling_app="native/build/Kinesthetic Bowling Motion.app"
bowling_zip="native/build/Kinesthetic Bowling Motion.zip"

# The relay's health JSON, one line per live pair: "club Left 12ms" / "wrist Right 40ms".
pairs() {
  python3 -c '
import sys,json
d=json.load(sys.stdin)
for role,key in (("club","samples"),("wrist","bowlingSamples")):
    for who,s in d.get(key,{}).items():
        print(role, s["sourceId"], "%dms"%s["ageMs"], "live" if s["ageMs"]<1500 else "stale")'
}

receive() {
  mkdir -p local-data
  if [[ ! -s $token_file ]]; then
    # Same shape the Unity host-config writer produces: 32 hex characters.
    /usr/bin/openssl rand -hex 16 > "$token_file"
    ok "Wrote a new pairing token to $token_file"
  fi
  token=$(<"$token_file")

  # The relay must be bound to every interface with the current token. A relay started before the token
  # existed is loopback-only; one started under an older token rejects the other Mac. Either way, restart.
  pid=$(lsof -nP -iTCP:8767 -sTCP:LISTEN -t 2>/dev/null | head -1)
  if [[ -n $pid ]]; then
    env=$(ps eww -o command= -p "$pid" 2>/dev/null)
    if [[ $env != *"KINESTHETIC_GOLF_HOST=0.0.0.0"* || $env != *"KINESTHETIC_PAIR_TOKEN=$token"* ]]; then
      warn "Relay is local-only or holds an old token; restarting it (Unity reconnects on its own)"
      kill "$pid"; for i in {1..30}; do lsof -nP -iTCP:8767 -sTCP:LISTEN >/dev/null 2>&1 || break; sleep 0.1; done
    fi
  fi
  # Starts anything not running (server, relay with the token, voice) and opens Club Motion for this Mac's pair.
  /bin/zsh scripts/start_demo_services.sh club

  # The other Mac needs Bowling Motion. Build only that variant so the running Club Motion app is untouched.
  if [[ ! -d $bowling_app || native/ClubMotionBridge.swift -nt "$bowling_app/Contents/MacOS/ClubMotionBridge" \
        || native/KeepAlive.swift -nt "$bowling_app/Contents/MacOS/ClubMotionBridge" ]]; then
    if zsh native/build.sh bowling >/dev/null; then ok "Built $bowling_app"; else bad "Bowling Motion build failed (needs Xcode or Command Line Tools)"; fi
  fi
  if [[ -d $bowling_app ]]; then
    rm -f "$bowling_zip"; /usr/bin/ditto -c -k --keepParent "$bowling_app" "$bowling_zip"
    ok "AirDrop $bowling_zip to the other Mac (Apple silicon; an Intel Mac builds its own with zsh native/build.sh bowling)"
  fi

  ip=$(ipconfig getifaddr en0 2>/dev/null || ipconfig getifaddr en1 2>/dev/null)
  if [[ -z $ip ]]; then bad "No Wi-Fi address; join the same network as the other Mac"; exit 1; fi
  if get "http://$ip:8767/?token=$token" >/dev/null; then ok "Relay answers on $ip with the token"
  else bad "Relay does not answer on $ip:8767 — see local-data/logs/golf-relay.log"; exit 1; fi
  code=$(/usr/bin/curl -s -o /dev/null -w '%{http_code}' --max-time 2 "http://$ip:8767/")
  [[ $code == 403 ]] && ok "Relay refuses the network without the token" || warn "Relay answered $code without a token"

  print "\nOn the other Mac, in the repo:"
  print "  zsh scripts/start_two_airpods.sh send $ip $token"
  print "or type into Bowling Motion:  Relay Mac IP $ip · Pairing token $token"
  print "The address comes from this network's DHCP; rerun this after changing Wi-Fi."

  print "\nPairs"
  if [[ " $* " == *" --wait "* ]]; then
    print "  waiting for a live pair on each channel: club (this Mac) and wrist (the other Mac); Ctrl-C to stop"
    while true; do
      live=$(get http://127.0.0.1:8767/ | pairs)
      if [[ $live == *"club "*live* && $live == *"wrist "*live* ]]; then break; fi
      sleep 1
    done
  fi
  live=$(get http://127.0.0.1:8767/ | pairs)
  [[ -n $live ]] && print -r -- "$live" | while read -r l; do [[ $l == *live ]] && ok "$l" || warn "$l (last sample long ago)"; done
  [[ $live == *"club "*live* ]]  || warn "No live club pair — pair AirPods to this Mac; Club Motion shows the reporting bud"
  [[ $live == *"wrist "*live* ]] || warn "No live wrist pair yet — waiting on the other Mac"
}

send() {
  ip=${1:-}; token=${2:-}
  if [[ -z $ip || -z $token ]]; then print -u2 "usage: zsh scripts/start_two_airpods.sh send <receiver-ip> <token>"; exit 1; fi
  if ! get "http://$ip:8767/?token=$token" >/dev/null; then
    bad "No relay at $ip:8767 with that token — same Wi-Fi as the receiver? did it run 'receive'?"; exit 1
  fi
  ok "Relay at $ip accepts the token"
  if [[ ! -d $bowling_app ]]; then
    if zsh native/build.sh bowling >/dev/null; then ok "Built $bowling_app"; else bad "Bowling Motion build failed (needs Xcode or Command Line Tools)"; exit 1; fi
  fi
  # The app reads these from its own defaults domain, so it opens already pointed at the receiver.
  pkill -x ClubMotionBridge 2>/dev/null
  defaults write org.kinesthetic.bowlingmotion relayHost -string "$ip"
  defaults write org.kinesthetic.bowlingmotion pairToken -string "$token"
  /usr/bin/open -a "$bowling_app"
  ok "Bowling Motion opened, pointed at $ip. Pair this Mac's AirPods and allow Motion access when asked."
  print "  waiting for this pair to reach the receiver; Ctrl-C to stop"
  for i in {1..120}; do
    live=$(get "http://$ip:8767/?token=$token" | pairs)
    if [[ $live == *"wrist "*live* ]]; then ok "${${(f)live}[(r)wrist *]} — streaming"; return 0; fi
    sleep 1
  done
  warn "No wrist samples after two minutes. Check the app's status line: it names the reporting bud once motion arrives."
  return 1
}

case "${1:-receive}" in
  receive) shift; receive "$@" ;;
  send) shift; send "$@" ;;
  *) print -u2 "usage: $0 receive [--wait] | send <receiver-ip> <token>"; exit 1 ;;
esac
