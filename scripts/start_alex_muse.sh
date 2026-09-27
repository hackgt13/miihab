#!/bin/zsh
# Alex's brain on Meta's Muse Spark (voice/backend/museProxy.ts), or back to ElevenLabs' default.
#
#   zsh scripts/start_alex_muse.sh        switch Alex to Muse
#   zsh scripts/start_alex_muse.sh off    switch Alex back to ElevenLabs' default model
#
# ElevenLabs' servers ask Alex's model what to say, so they need a public way in: a Tailscale Funnel onto the
# adapter's port (8771) on this Mac. The adapter answers only POST .../chat/completions carrying MUSE_PROXY_TOKEN,
# and holds the Muse key itself; ElevenLabs holds only that token. Everything else on this Mac stays private.
set -u
repo="${0:A:h:h}"; cd "$repo" || exit 1
ok()  { print -P "  %F{green}✓%f $1"; }
bad() { print -P "  %F{red}✗%f $1"; }
ts=""; for c in /usr/local/bin/tailscale /opt/homebrew/bin/tailscale /Applications/Tailscale.app/Contents/MacOS/Tailscale; do [[ -x $c ]] && { ts=$c; break; }; done
[[ -n $ts ]] || { bad "Tailscale is not installed"; exit 1; }
node_exe=$(command -v node); [[ -x /opt/homebrew/opt/node@24/bin/node ]] && node_exe=/opt/homebrew/opt/node@24/bin/node
set_env() { # set_env KEY VALUE in the repo-root .env, replacing any old line
  /usr/bin/grep -v "^$1=" .env > .env.tmp 2>/dev/null; print -r -- "$1=$2" >> .env.tmp; mv .env.tmp .env; }
restart_voice() {
  local pid=$(lsof -nP -tiTCP:8769 -sTCP:LISTEN 2>/dev/null); [[ -n $pid ]] && kill $pid && sleep 1
  (cd voice && nohup "$node_exe" --env-file=.env --env-file-if-exists=../.env server.ts >>../local-data/logs/voice.log 2>&1 </dev/null &)
  for i in {1..20}; do lsof -nP -iTCP:8769 -sTCP:LISTEN >/dev/null 2>&1 && break; sleep .5; done
}
update_agent() { (cd voice && "$node_exe" --env-file=.env --env-file-if-exists=../.env backend/agent.ts update); }

if [[ ${1:-on} == off ]]; then
  "$ts" funnel --https=443 off >/dev/null 2>&1; ok "Funnel off"
  /usr/bin/grep -v '^MUSE_PROXY_URL=' .env > .env.tmp; mv .env.tmp .env
  update_agent && ok "Alex is back on ElevenLabs' default model"
  restart_voice; exit 0
fi

/usr/bin/grep -q '^MUSE_API_KEY=.' .env || { bad "No MUSE_API_KEY in .env"; exit 1; }
/usr/bin/grep -q '^MUSE_PROXY_TOKEN=.' .env || { set_env MUSE_PROXY_TOKEN "$(/usr/bin/openssl rand -hex 24)"; ok "Made the adapter's password (MUSE_PROXY_TOKEN in .env)"; }
restart_voice
lsof -nP -iTCP:8771 -sTCP:LISTEN >/dev/null 2>&1 && ok "Muse adapter listening on 127.0.0.1:8771" || { bad "The adapter did not start; see local-data/logs/voice.log"; exit 1; }

# Public HTTPS onto the adapter only. Funnel must be allowed for this tailnet once (the Tailscale admin console:
# Access controls → nodeAttrs "funnel"); `tailscale funnel` exits 0 even when it is not, so the address is proven
# from outside before Alex is pointed at it — an unreachable brain would leave the live Alex silent.
"$ts" funnel --bg 8771 2>&1 | tee /dev/stderr | /usr/bin/grep -qi "not enabled" && { bad "Funnel is not enabled on this tailnet. Enable it at https://login.tailscale.com/admin/acls (see TWO_MAC_SETUP.md), then run this again. Alex is unchanged."; exit 1; }
host=$("$ts" status --json | python3 -c 'import sys,json;print(json.load(sys.stdin)["Self"]["DNSName"].rstrip("."))')
url="https://$host/v1"
code=""
for i in {1..20}; do code=$(/usr/bin/curl -s -o /dev/null -w '%{http_code}' --max-time 5 -X POST "$url/chat/completions"); [[ $code == 401 ]] && break; sleep 2; done
[[ $code == 401 ]] || { bad "The public address did not answer (got '$code'); Alex is unchanged."; "$ts" funnel --https=443 off >/dev/null 2>&1; exit 1; }
set_env MUSE_PROXY_URL "$url"; ok "Adapter public at $url, answering and refusing requests without the password"

update_agent && ok "Alex now thinks with Muse Spark. Talk to him in the studio; 'zsh scripts/start_alex_muse.sh off' switches back."
