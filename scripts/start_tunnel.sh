#!/bin/zsh
# Venue Wi-Fi keeps devices from talking to each other, so the second Mac reaches this Mac's relay through a
# Tailscale tunnel instead: both Macs join one tailnet and the relay is addressed by its tailnet IP. Everything
# else (token, apps, which AirPod is which) is scripts/start_two_airpods.sh, which this wraps.
#
#   zsh scripts/start_tunnel.sh receive             this Mac: tunnel up, relay up, print what to run on the other Mac
#   zsh scripts/start_tunnel.sh receive --wait      ...and wait until both pairs are streaming
#   zsh scripts/start_tunnel.sh send <ip> <token>   the other Mac: tunnel up, then point Bowling Motion at the receiver
#   zsh scripts/start_tunnel.sh status              is the tunnel up, and who is on it
#
# Both Macs must be on the same tailnet: sign in with the same Tailscale account on each (or share the node).
# The Quest is not on the tailnet; it still needs the same Wi-Fi as this Mac, or a phone hotspot.
set -u
repo="${0:A:h:h}"; cd "$repo" || exit 1
ok()   { print -P "  %F{green}✓%f $1"; }
warn() { print -P "  %F{yellow}!%f $1"; }
bad()  { print -P "  %F{red}✗%f $1"; }

ts=""
for candidate in /usr/local/bin/tailscale /opt/homebrew/bin/tailscale /Applications/Tailscale.app/Contents/MacOS/Tailscale; do
  [[ -x $candidate ]] && { ts=$candidate; break; }
done
if [[ -z $ts ]]; then bad "Tailscale is not installed. Get it from https://tailscale.com/download/mac, sign in, then rerun."; exit 1; fi

state() { "$ts" status --json 2>/dev/null | python3 -c 'import sys,json;print(json.load(sys.stdin).get("BackendState","?"))' 2>/dev/null || print "?"; }

# Bring the tunnel up. A machine that has never signed in gets a login URL from `tailscale up`; we print it and stop.
tunnel_up() {
  local s=$(state)
  case $s in
    Running) ;;
    NeedsLogin|NoState|"?")
      warn "Tailscale needs a sign-in. Opening the Tailscale app: sign in, then rerun this."
      /usr/bin/open -a Tailscale 2>/dev/null
      "$ts" up 2>&1 | grep -E "https://" | head -1
      return 1 ;;
    *)
      # Stopped: signed in, switched off. Turn it on.
      if ! "$ts" up 2>/dev/null; then
        # The GUI app owns the connection on the App Store build; ask it instead.
        /usr/bin/open -a Tailscale 2>/dev/null
        for i in {1..40}; do [[ $(state) == Running ]] && break; sleep 0.25; done
      fi ;;
  esac
  for i in {1..40}; do [[ $(state) == Running ]] && break; sleep 0.25; done
  if [[ $(state) != Running ]]; then bad "Tailscale did not come up (state: $(state)). Click the Tailscale menu bar icon and connect, then rerun."; return 1; fi
  ok "Tunnel up: this Mac is $("$ts" ip -4 2>/dev/null | head -1) on the tailnet"
}

receive() {
  tunnel_up || exit 1
  local ip=$("$ts" ip -4 2>/dev/null | head -1)
  # The relay, the token, the apps and the printed instructions; addressed by the tunnel, not the venue Wi-Fi.
  KINESTHETIC_ADVERTISE_IP=$ip /bin/zsh scripts/start_two_airpods.sh receive "$@"
  print "\nThe other Mac must be signed in to the same Tailscale account. On it:"
  print "  zsh scripts/start_tunnel.sh send $ip $(<local-data/pair-token.txt)"
}

send() {
  local ip=${1:-} token=${2:-}
  if [[ -z $ip || -z $token ]]; then print -u2 "usage: zsh scripts/start_tunnel.sh send <receiver-tailnet-ip> <token>"; exit 1; fi
  tunnel_up || exit 1
  if ! "$ts" ping -c 3 --timeout 2s "$ip" >/dev/null 2>&1; then
    bad "Cannot reach $ip over the tunnel. Is the receiver signed in to the same tailnet and is its tunnel up?"
    "$ts" status 2>/dev/null | head -6; exit 1
  fi
  ok "Receiver $ip reachable over the tunnel"
  /bin/zsh scripts/start_two_airpods.sh send "$ip" "$token"
}

case "${1:-receive}" in
  receive) shift; receive "$@" ;;
  send) shift; send "$@" ;;
  status) print "state: $(state)"; "$ts" status 2>&1 | head -12 ;;
  *) print -u2 "usage: zsh scripts/start_tunnel.sh receive [--wait] | send <receiver-tailnet-ip> <token> | status"; exit 1 ;;
esac
