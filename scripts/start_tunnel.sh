#!/bin/zsh
# Venue Wi-Fi keeps devices from talking to each other. This gets the second Mac to this Mac's relay through a
# Tailscale tunnel and the Quest to it over its USB cable, so no venue network is in the path at all.
#
#   zsh scripts/start_tunnel.sh receive             this Mac: tunnel up, relay up, Quest cable wired, print the other end
#   zsh scripts/start_tunnel.sh receive --watch     ...and keep it that way: re-ups the tunnel, re-wires a replugged
#                                                   Quest, reports pairs coming and going. Ctrl-C to stop.
#   zsh scripts/start_tunnel.sh send <ip> <token>   the other Mac (with the repo): tunnel up, wait for the receiver,
#                                                   point Bowling Motion at it
#   zsh scripts/start_tunnel.sh status              tunnel state, tailnet peers, Quest cable, pairs
#
# The other Mac does not need the repo: `receive` writes native/build/Kinesthetic Bowling Motion.zip containing the
# app and a double-clickable "Connect to <this Mac>.command" with this address and token baked in. AirDrop the zip.
#
# Both Macs must be on one tailnet: sign in to Tailscale with the same account on each. Which AirPod is which is
# not decided here or by the apps (AGENTS.md, Sensors); the set decides.
set -u
repo="${0:A:h:h}"; cd "$repo" || exit 1
ok()   { print -P "  %F{green}✓%f $1"; }
warn() { print -P "  %F{yellow}!%f $1"; }
bad()  { print -P "  %F{red}✗%f $1"; }
get() { /usr/bin/curl -fsS --max-time 2 "$@" 2>/dev/null; }
token_file=local-data/pair-token.txt

ts=""
for candidate in /usr/local/bin/tailscale /opt/homebrew/bin/tailscale /Applications/Tailscale.app/Contents/MacOS/Tailscale; do
  [[ -x $candidate ]] && { ts=$candidate; break; }
done
if [[ -z $ts ]]; then bad "Tailscale is not installed. https://tailscale.com/download/mac — install, sign in, rerun."; exit 1; fi
adb=""
for candidate in /Applications/Unity/Hub/Editor/*/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb(N) $(command -v adb 2>/dev/null); do
  [[ -x $candidate ]] && { adb=$candidate; break; }
done

state() { "$ts" status --json 2>/dev/null | python3 -c 'import sys,json;print(json.load(sys.stdin).get("BackendState","?"))' 2>/dev/null || print "?"; }
tailnet_ip() { "$ts" ip -4 2>/dev/null | head -1; }

# Bring the tunnel up and keep quiet about it when it already is. A Mac that has never signed in gets the login
# page; we stop there, because the sign-in is a person's to do.
tunnel_up() {
  [[ $(state) == Running ]] && return 0
  case $(state) in
    NeedsLogin|NoState|"?")
      warn "Tailscale needs a sign-in. Sign in (same account as the other Mac), then rerun."
      /usr/bin/open -a Tailscale 2>/dev/null; "$ts" up 2>&1 | grep -E "https://" | head -1; return 1 ;;
  esac
  "$ts" up >/dev/null 2>&1 || /usr/bin/open -a Tailscale 2>/dev/null   # the App Store build takes it from the app
  for i in {1..60}; do [[ $(state) == Running ]] && break; sleep 0.25; done
  if [[ $(state) != Running ]]; then bad "Tailscale did not come up (state: $(state)). Click its menu bar icon and connect."; return 1; fi
  ok "Tunnel up: $(tailnet_ip)"
}

# The Quest over its cable: the headset tries its own loopback first (QuestHostConfig), which reaches this Mac once
# these ports are reversed. Re-applied every time the headset is plugged in; harmless when it already is.
quest_wire() {
  [[ -n $adb ]] || { warn "No adb on this Mac (install Unity's Android module); the Quest will need Wi-Fi to reach the relay"; return 1; }
  local device=$("$adb" devices 2>/dev/null | awk 'NR>1 && $2=="device"{print $1}' | head -1)
  local unauthorized=$("$adb" devices 2>/dev/null | awk 'NR>1 && $2=="unauthorized"{print $1}' | head -1)
  if [[ -z $device ]]; then
    [[ -n $unauthorized ]] && warn "Quest on USB but not authorized — put it on and tap Allow USB debugging" || warn "No Quest on USB yet; plug it in (this script wires it when --watch is on)"
    return 1
  fi
  local missing=0
  for port in 8767 8766 8769; do
    "$adb" -s "$device" reverse --list 2>/dev/null | grep -q "tcp:$port tcp:$port" || { "$adb" -s "$device" reverse tcp:$port tcp:$port >/dev/null 2>&1 || missing=1; }
  done
  (( missing )) && { bad "Could not reverse the relay ports to the Quest"; return 1; }
  ok "Quest $device wired: relay, coordinator and voice reach it over the cable"
}

# What the relay sees, one line per live pair.
pairs() {
  get http://127.0.0.1:8767/ | python3 -c '
import sys,json
d=json.load(sys.stdin)
for role,key in (("IMU A","samples"),("IMU B","bowlingSamples")):
    for who,s in d.get(key,{}).items():
        print(role, s["sourceId"], "%dms"%s["ageMs"], "live" if s["ageMs"]<1500 else "stale")' 2>/dev/null
}

# A double-clickable connector for a Mac without the repo: Tailscale up, wait for this Mac, aim the app, open it.
write_connector() {
  local ip=$1 token=$2 host=$(scutil --get ComputerName 2>/dev/null || hostname -s)
  # Plain ASCII in the filename: a curly apostrophe from the computer name comes out garbled on the other Mac's unzip.
  local safe=$(print -r -- "$host" | LC_ALL=C tr -c 'A-Za-z0-9 ()._-' '_' | tr -s '_' | sed 's/^_//; s/_$//')
  local file="native/build/Connect to ${safe:-this Mac}.command"
  cat > "$file" <<EOF
#!/bin/zsh
# Connect this Mac's AirPods to ${host}'s relay over Tailscale. Needs Tailscale signed in to the same account.
# Written by scripts/start_tunnel.sh on ${host}; keep it next to "Kinesthetic Bowling Motion.app".
ip="$ip"; token="$token"
here="\$(cd "\$(dirname "\$0")" && pwd)"
ts=""; for c in /usr/local/bin/tailscale /opt/homebrew/bin/tailscale /Applications/Tailscale.app/Contents/MacOS/Tailscale; do [[ -x \$c ]] && { ts=\$c; break; }; done
[[ -n \$ts ]] || { echo "Install Tailscale first: https://tailscale.com/download/mac"; exit 1; }
state() { "\$ts" status --json 2>/dev/null | python3 -c 'import sys,json;print(json.load(sys.stdin).get("BackendState","?"))' 2>/dev/null; }
if [[ \$(state) != Running ]]; then "\$ts" up >/dev/null 2>&1 || open -a Tailscale; for i in {1..60}; do [[ \$(state) == Running ]] && break; sleep 0.5; done; fi
[[ \$(state) == Running ]] || { echo "Tailscale is not connected — sign in (same account as ${host}) and run this again."; exit 1; }
echo "Tunnel up as \$("\$ts" ip -4 | head -1). Waiting for ${host} at \$ip …"
for i in {1..600}; do curl -fsS --max-time 2 "http://\$ip:8767/?token=\$token" >/dev/null 2>&1 && break; sleep 1; done
curl -fsS --max-time 2 "http://\$ip:8767/?token=\$token" >/dev/null 2>&1 || { echo "Cannot reach ${host}'s relay. Is its tunnel up (zsh scripts/start_tunnel.sh receive)?"; exit 1; }
echo "Relay reachable. Pointing Bowling Motion at it."
defaults write org.kinesthetic.bowlingmotion relayHost -string "\$ip"
defaults write org.kinesthetic.bowlingmotion pairToken -string "\$token"
pkill -x ClubMotionBridge 2>/dev/null
open -a "\$here/Kinesthetic Bowling Motion.app" || open -a "Kinesthetic Bowling Motion"
echo "Pair this Mac's AirPods (Bluetooth), allow Motion access when asked. Waiting for motion to reach ${host} …"
for i in {1..300}; do
  if curl -fsS --max-time 2 "http://\$ip:8767/?token=\$token" 2>/dev/null | python3 -c 'import sys,json;d=json.load(sys.stdin);sys.exit(0 if any(s["ageMs"]<1500 for s in d.get("bowlingSamples",{}).values()) else 1)' 2>/dev/null; then
    echo "Streaming. Leave this window open or close it; the app keeps going."; exit 0; fi
  sleep 1
done
echo "No motion after five minutes. Check the app's status line: it names the reporting bud once motion arrives."
EOF
  chmod +x "$file"
  local app="native/build/Kinesthetic Bowling Motion.app" zip="native/build/Kinesthetic Bowling Motion.zip"
  if [[ -d $app ]]; then
    rm -f "$zip"; (cd native/build && /usr/bin/ditto -c -k --keepParent "Kinesthetic Bowling Motion.app" "Kinesthetic Bowling Motion.zip" && /usr/bin/zip -q "Kinesthetic Bowling Motion.zip" "${file:t}")
    ok "AirDrop $zip to the other Mac: unzip, double-click \"${file:t}\""
  fi
}

receive() {
  tunnel_up || exit 1
  local ip=$(tailnet_ip)
  # Relay bound with the token, services, this Mac's motion app, the other Mac's app built — addressed by the tunnel.
  KINESTHETIC_ADVERTISE_IP=$ip /bin/zsh scripts/start_two_airpods.sh receive
  local token=$(<"$token_file")
  write_connector "$ip" "$token"
  print "\nQuest"; quest_wire
  print "\nOther Mac (same Tailscale account), either:"
  print "  unzip the AirDropped zip and double-click the .command"
  print "  or, with the repo:  zsh scripts/start_tunnel.sh send $ip $token"
  [[ " $* " == *" --watch "* ]] || return 0
  print "\nWatching (Ctrl-C to stop): tunnel, Quest cable, pairs."
  local last="" now
  while true; do
    sleep 4
    [[ $(state) == Running ]] || { warn "tunnel dropped, bringing it back"; tunnel_up; }
    [[ -n $adb ]] && { local dev=$("$adb" devices 2>/dev/null | awk 'NR>1 && $2=="device"{print $1}' | head -1)
      [[ -n $dev ]] && ! "$adb" -s "$dev" reverse --list 2>/dev/null | grep -q "tcp:8767 tcp:8767" && quest_wire; }
    now="$(pairs | awk '{print $2, $5}' | sort | tr '\n' ' ')"
    [[ $now == $last ]] && continue
    last=$now
    print -P "  $(date +%H:%M:%S)  IMU A: $( [[ $now == *'A live'* ]] && print -P '%F{green}live%f' || print -P '%F{yellow}none%f')   IMU B: $( [[ $now == *'B live'* ]] && print -P '%F{green}live%f' || print -P '%F{yellow}none%f')"
  done
}

send() {
  local ip=${1:-} token=${2:-}
  if [[ -z $ip || -z $token ]]; then print -u2 "usage: zsh scripts/start_tunnel.sh send <receiver-tailnet-ip> <token>"; exit 1; fi
  tunnel_up || exit 1
  print "  waiting for the receiver at $ip (up to 10 minutes; Ctrl-C to stop)"
  for i in {1..600}; do get "http://$ip:8767/?token=$token" >/dev/null && break; sleep 1; done
  if ! get "http://$ip:8767/?token=$token" >/dev/null; then
    bad "Cannot reach $ip:8767 over the tunnel. Is the receiver's tunnel up, and is it the same tailnet?"; "$ts" status 2>/dev/null | head -6; exit 1
  fi
  ok "Receiver $ip reachable over the tunnel"
  /bin/zsh scripts/start_two_airpods.sh send "$ip" "$token"
}

status() {
  print "Tunnel: $(state)  $(tailnet_ip)"; "$ts" status 2>/dev/null | sed -n '1,8p' | sed 's/^/  /'
  print "Quest:"; quest_wire
  print "Pairs:"; local p=$(pairs); [[ -n $p ]] && print -r -- "$p" | sed 's/^/  /' || print "  none (relay down or no motion)"
}

case "${1:-receive}" in
  receive) shift; receive "$@" ;;
  send) shift; send "$@" ;;
  status) status ;;
  *) print -u2 "usage: zsh scripts/start_tunnel.sh receive [--watch] | send <receiver-tailnet-ip> <token> | status"; exit 1 ;;
esac
