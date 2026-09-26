#!/bin/zsh
set -eu
repo="${0:A:h:h}"
/bin/zsh "$repo/scripts/start_demo_services.sh" bowling
