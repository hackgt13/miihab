#!/bin/zsh
# Two AirPod pairs need two Apple hosts: one Mac reads one CMHeadphoneMotionManager stream.
# This Mac (the receiver) runs Unity, the coordinator and the golf relay, and streams its own pair: Mac 1. The other
# Mac streams its pair to this relay: Mac 2. Either Motion app works on either Mac; the games follow whichever pair is
# moving, and the studio tells them apart once (AGENTS.md, Sensors).
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
club_app="native/build/Kinesthetic Club Motion.app"
plist_for() { [[ $1 == *Bowling* ]] && print -r -- native/BowlingInfo.plist || print -r -- native/Info.plist }
variant_for() { [[ $1 == *Bowling* ]] && print -r -- bowling || print -r -- club }

# A bundle is stale when any source it is built from is newer -- the Info.plist included. Leaving the plist out of
# this comparison is what let an App Transport Security fix sit in the repo while both Macs kept opening the old app.
app_stale() {
  local app=$1 exe="$1/Contents/MacOS/ClubMotionBridge"
  [[ -d $app ]] || return 0
  [[ native/ClubMotionBridge.swift -nt $exe || native/KeepAlive.swift -nt $exe \
     || $(plist_for "$app") -nt "$app/Contents/Info.plist" ]]
}
rebuild_if_stale() {
  local app=$1
  app_stale "$app" || return 0
  if zsh native/build.sh "$(variant_for "$app")" >/dev/null; then ok "Built ${app:t:r} (its sources had changed)"; return 0; fi
  bad "${app:t:r} build failed (needs Xcode or Command Line Tools)"; return 1
}

# Whether the app may speak plain ws:// and http:// to this address at all. App Transport Security counts only
# loopback and the RFC1918 ranges (10/8, 172.16/12, 192.168/16) as local networking. A Tailscale peer is 100.64/10
# (RFC6598) and is not among them, so a bundle carrying NSAllowsLocalNetworking streams happily on a hotspot and
# fails mute over the tunnel: the app opens, no error reaches any log a script can read, and it never makes a
# socket. Checked before the app is opened or handed out, because the symptom gives nothing away.
ats_permits() {
  local app=$1 ip=$2
  case $ip in
    localhost|127.*|10.*|192.168.*|172.1[6-9].*|172.2[0-9].*|172.3[01].*) return 0 ;;
  esac
  [[ $(/usr/bin/plutil -extract NSAppTransportSecurity.NSAllowsArbitraryLoads raw -o - \
        "$app/Contents/Info.plist" 2>/dev/null) == true ]]
}
ats_complain() {
  bad "${1:t:r} cannot reach $2: it was built without NSAllowsArbitraryLoads, and App Transport Security counts
     only loopback and the RFC1918 ranges as local -- not a tailnet address (100.64/10). The app would refuse its
     own relay connection and report nothing. Rebuild it from this checkout: zsh native/build.sh"
}

# The relay's health JSON, one line per pair: "Mac 1 Left 12ms live" / "Mac 2 Right 40ms stale".
pairs() {
  python3 -c '
import sys,json
d=json.load(sys.stdin)
for mac,s in sorted(d.get("macs",{}).items()):
    if "ageMs" in s: print("Mac", mac[-1], s["sourceId"], "%dms"%s["ageMs"], "live" if s["ageMs"]<1500 else "stale")'
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

  # The address the other Mac should use: the Wi-Fi one, unless a tunnel (scripts/start_tunnel.sh) hands one in.
  # Resolved before the app is built, because it decides whether that app is allowed to dial back (ats_permits).
  ip=${KINESTHETIC_ADVERTISE_IP:-$(ipconfig getifaddr en0 2>/dev/null || ipconfig getifaddr en1 2>/dev/null)}
  if [[ -z $ip ]]; then bad "No Wi-Fi address; join the same network as the other Mac, or use scripts/start_tunnel.sh"; exit 1; fi

  # The other Mac needs Bowling Motion. Build only that variant so the running Club Motion app is untouched.
  rebuild_if_stale "$bowling_app"
  if [[ -d $bowling_app ]]; then
    # Refuse to hand out an app that cannot reach the address it is being pointed at.
    if ! ats_permits "$bowling_app" "$ip"; then ats_complain "$bowling_app" "$ip"; exit 1; fi
    rm -f "$bowling_zip"; /usr/bin/ditto -c -k --keepParent "$bowling_app" "$bowling_zip"
    ok "AirDrop $bowling_zip to the other Mac (Apple silicon; an Intel Mac builds its own with zsh native/build.sh bowling)"
  fi
  if get "http://$ip:8767/?token=$token" >/dev/null; then ok "Relay answers on $ip with the token"
  else bad "Relay does not answer on $ip:8767 — see local-data/logs/golf-relay.log"; exit 1; fi
  code=$(/usr/bin/curl -s -o /dev/null -w '%{http_code}' --max-time 2 "http://$ip:8767/")
  [[ $code == 403 ]] && ok "Relay refuses the network without the token" || warn "Relay answered $code without a token"

  print "\nOn the other Mac, in the repo:"
  print "  zsh scripts/start_two_airpods.sh send $ip $token"
  print "or type into Bowling Motion:  Relay Mac IP $ip · Pairing token $token"
  [[ -n ${KINESTHETIC_ADVERTISE_IP:-} ]] || print "The address comes from this network's DHCP; rerun this after changing Wi-Fi."

  print "\nPairs"
  if [[ " $* " == *" --wait "* ]]; then
    print "  waiting for both pairs: Mac 1 (this Mac) and Mac 2 (the other Mac); Ctrl-C to stop"
    while true; do
      live=$(get http://127.0.0.1:8767/ | pairs)
      if [[ $live == *"Mac 1 "*live* && $live == *"Mac 2 "*live* ]]; then break; fi
      sleep 1
    done
  fi
  live=$(get http://127.0.0.1:8767/ | pairs)
  [[ -n $live ]] && print -r -- "$live" | while read -r l; do [[ $l == *live ]] && ok "$l" || warn "$l (last sample long ago)"; done
  [[ $live == *"Mac 1 "*live* ]] || warn "No live Mac 1 — pair AirPods to this Mac; its Motion app shows the reporting bud"
  [[ $live == *"Mac 2 "*live* ]] || warn "No live Mac 2 yet — waiting on the other Mac"
}

send() {
  ip=${1:-}; token=${2:-}
  if [[ -z $ip || -z $token ]]; then print -u2 "usage: zsh scripts/start_two_airpods.sh send <receiver-ip> <token>"; exit 1; fi
  if ! get "http://$ip:8767/?token=$token" >/dev/null; then
    bad "No relay at $ip:8767 with that token — same Wi-Fi as the receiver? did it run 'receive'?"; exit 1
  fi
  ok "Relay at $ip accepts the token"
  # Either motion app will do: the relay registers a second Mac's stream apart from the receiver's own and routes it
  # onto whichever channel the receiver is not using (golf-relay.ts route()). Use what is here; build only if nothing is.
  local app=""
  for candidate in "$bowling_app" "$club_app" "/Applications/Kinesthetic Bowling Motion.app" "/Applications/Kinesthetic Club Motion.app"; do
    [[ -d $candidate ]] && { app=$candidate; break; }
  done
  if [[ -z $app ]]; then
    if zsh native/build.sh bowling >/dev/null; then app=$bowling_app; ok "Built $app"; else bad "Motion app build failed (needs Xcode or Command Line Tools)"; exit 1; fi
  fi
  # A repo-built app that has fallen behind its sources is rebuilt before it is opened, so a fix in this checkout
  # is the one that runs. A copy under /Applications is not ours to rebuild; it only has to pass the check below.
  [[ $app == native/build/* ]] && { rebuild_if_stale "$app" || exit 1 }
  if ! ats_permits "$app" "$ip"; then ats_complain "$app" "$ip"; exit 1; fi
  # Both apps read these from their own defaults domain, so whichever opens is already pointed at the receiver.
  pkill -x ClubMotionBridge 2>/dev/null
  for domain in org.kinesthetic.bowlingmotion org.kinesthetic.clubmotion; do
    defaults write $domain relayHost -string "$ip"; defaults write $domain pairToken -string "$token"
  done
  # Counted with this Mac's app down, so a pair that connects the moment it opens still reads as new:
  # whatever is live now is the receiver's, and one more on either channel is this Mac's.
  local before=$(get "http://$ip:8767/?token=$token" | pairs | grep -c live)
  # An absolute path: `open -a` with a relative one is read as an app *name* and never found.
  if ! /usr/bin/open "${app:A}"; then
    bad "Could not open ${app:A}. Open it from Finder, or rebuild: zsh native/build.sh bowling"; exit 1
  fi
  ok "${app:t:r} opened, pointed at $ip. Pair this Mac's AirPods and allow Motion access when asked."
  print "  waiting for this pair to reach the receiver; Ctrl-C to stop"
  for i in {1..120}; do
    live=$(get "http://$ip:8767/?token=$token" | pairs)
    if (( $(print -r -- "$live" | grep -c live) > before )); then ok "streaming — the relay sees: $(print -r -- "$live" | grep live | tr '\n' ';')"; return 0; fi
    sleep 1
  done
  warn "No samples from this Mac after two minutes. Check the app's status line: it names the reporting bud once motion arrives."
  return 1
}

case "${1:-receive}" in
  receive) shift; receive "$@" ;;
  send) shift; send "$@" ;;
  *) print -u2 "usage: $0 receive [--wait] | send <receiver-ip> <token>"; exit 1 ;;
esac
