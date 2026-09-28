#!/bin/zsh
# One command before a demo or rehearsal: start services, check every piece, say exactly what to fix.
#   zsh scripts/demo.sh            start + check
#   zsh scripts/demo.sh --open     also open the physician portal
#   zsh scripts/demo.sh --reset    archive approved plans after v1 and the recorded sessions, for a clean v1 → v2 demo
set -u
repo="${0:A:h:h}"; cd "$repo" || exit 1
adb=/Applications/Unity/Hub/Editor/6000.6.2f1-arm64/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb
[[ -x $adb ]] || adb=/Applications/Unity/Hub/Editor/6000.6.2f1/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb
apk=local-data/builds/MiiHabQuest.apk
ok()   { print -P "  %F{green}✓%f $1"; }
warn() { print -P "  %F{yellow}!%f $1"; }
bad()  { print -P "  %F{red}✗%f $1"; failures=$((failures+1)); }
failures=0

if [[ " $* " == *" --reset "* ]]; then
  stamp=$(date +%Y%m%d-%H%M%S)
  mkdir -p "local-data/archive/$stamp/plans" "local-data/archive/$stamp/sessions"
  for f in local-data/plans/plan-v*.json(N); do [[ ${f:t} == plan-v1.json ]] || mv "$f" "local-data/archive/$stamp/plans/"; done
  mv local-data/sessions/exercise-*(N) "local-data/archive/$stamp/sessions/" 2>/dev/null
  print "Archived plans > v1 and exercise sessions to local-data/archive/$stamp (nothing deleted)."
fi

/bin/zsh scripts/start_demo_services.sh
get() { /usr/bin/curl -fsS --max-time 2 "$@" 2>/dev/null; }

print "\nServices"
if h=$(get http://127.0.0.1:8766/health); then
  ok "Pose bridge :8766 ($(print -r -- $h | python3 -c 'import sys,json;d=json.load(sys.stdin);print(d["captureStatus"])'))"
else bad "Pose bridge :8766 not responding — see local-data/logs/pose-bridge.log"; fi
if g=$(get http://127.0.0.1:8767/); then
  airpod=$(print -r -- $g | python3 -c 'import sys,json;d=json.load(sys.stdin);s=d["samples"];print(", ".join("%s %s (%sms)"%(k,v["sourceId"],v["ageMs"]) for k,v in s.items()) or "none")')
  host=$(print -r -- $g | python3 -c 'import sys,json;d=json.load(sys.stdin);print("Unity host connected" if d.get("stateHost") else "no Unity host yet", "·", d.get("stateClients",0), "headset(s)")')
  ok "Golf relay :8767 · $host"
  [[ $airpod == none ]] && warn "No AirPod motion yet — pair AirPods to this Mac; the Club Motion app starts streaming automatically" || ok "AirPod motion: $airpod"
else bad "Golf relay :8767 not responding — see local-data/logs/golf-relay.log"; fi
if p=$(get http://127.0.0.1:8766/api/plans/active); then
  ok "Portal http://localhost:8766/portal/ · active plan $(print -r -- $p | python3 -c 'import sys,json;d=json.load(sys.stdin);e=d["exercise"];print("v%s (shoulder raise %s–%s°, %s reps)"%(d["version"],e["targetDeg"],e["targetMaxDeg"],e["prescribedReps"]))')"
else bad "Portal API not responding"; fi
pgrep -f "Unity.app/Contents/MacOS/Unity" >/dev/null && ok "Unity editor running" || warn "Unity editor not running — open unity/KinestheticUnity"

print "\nQuest"
config=unity/KinestheticUnity/Assets/StreamingAssets/quest-host.json
ip=$(ipconfig getifaddr en0 2>/dev/null || ipconfig getifaddr en1 2>/dev/null)
# Over the USB cable (start_tunnel.sh reverses the ports) the headset reaches this Mac on its own loopback first, so
# the Wi-Fi address baked into the build only matters without a cable: then a stale one is a warning, not a failure.
cabled=""
[[ -x $adb ]] && $adb reverse --list 2>/dev/null | grep -q "tcp:8767 tcp:8767" && cabled=1
[[ -n $cabled ]] && ok "Headset reaches this Mac over its USB cable (relay, coordinator and voice ports reversed)"
if [[ -f $config ]]; then
  baked=$(python3 -c "import json;print(json.load(open('$config'))['host'])")
  if [[ $baked == $ip ]]; then ok "Headset app points at this Mac ($ip)"
  elif [[ -n $cabled ]]; then warn "Headset app's Wi-Fi fallback is $baked (this Mac is now $ip); fine while it's on the cable"
  else bad "Headset app points at $baked but this Mac is now $ip — in Unity run Kinesthetic ▸ Quest ▸ Write host config, then rebuild the APK"; fi
elif [[ -z $cabled ]]; then warn "No headset config yet (Kinesthetic ▸ Quest ▸ Create headset scene)"; fi
if lsof -nP -iTCP:8767 -sTCP:LISTEN 2>/dev/null | grep -q '\*:8767'; then ok "Relay reachable on Wi-Fi (token required)"
else warn "Relay is local-only — headset can't connect until local-data/pair-token.txt exists and the relay restarts"; fi
[[ -f $apk ]] && ok "APK built: $apk" || warn "No APK built yet (Kinesthetic ▸ Quest ▸ Build combined headset app)"
if [[ -x $adb ]]; then
  device=$($adb devices | awk 'NR>1 && $2=="device"{print $1}' | head -1)
  unauthorized=$($adb devices | awk 'NR>1 && $2=="unauthorized"{print $1}' | head -1)
  if [[ -n $device ]]; then
    ok "Quest connected over USB ($device)"
    packages=$($adb -s $device shell pm list packages 2>/dev/null)
    print -r -- $packages | grep -q com.kinesthetic.miihab && ok "MiiHab installed on the Quest" \
      || warn "App not installed — run: $adb install -r $apk"
    for old in com.kinesthetic.questgolf com.kinesthetic.questbowling; do
      print -r -- $packages | grep -q $old && bad "Old single-game app $old still installed; it boots straight into its game — run: $adb -s $device uninstall $old"
    done
    print -r -- $packages | grep -q com.kinesthetic.rehabmii && bad "Old RehabMii-named app com.kinesthetic.rehabmii still installed — run: $adb -s $device uninstall com.kinesthetic.rehabmii"
  elif [[ -n $unauthorized ]]; then warn "Quest connected but not authorized — put it on and tap Allow USB debugging"
  else warn "No Quest over USB (needed only to install; the game itself runs over Wi-Fi)"; fi
fi

print ""
if (( failures )); then print -P "%F{red}$failures problem(s) above need fixing before the demo.%f"; else print -P "%F{green}Ready.%f"; fi
[[ " $* " == *" --open "* ]] && /usr/bin/open "http://localhost:8766/portal/"
exit $failures
