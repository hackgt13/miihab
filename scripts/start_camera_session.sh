#!/bin/zsh
set -eu
repo="${0:A:h:h}"
/bin/zsh "$repo/scripts/start_demo_services.sh"
result=$(/usr/bin/curl -fsS --max-time 3 -X POST http://127.0.0.1:8766/capture/start)
if [[ "$result" != *'"sourceConnected":true'* ]]; then
  /usr/bin/open -g -a 'Google Chrome' 'http://localhost:8766/'   # in the background: never over the headset cast
fi
