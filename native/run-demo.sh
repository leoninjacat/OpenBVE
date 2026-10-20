#!/usr/bin/env bash
set -euo pipefail
here="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd -- "$here/.." && pwd)"
if [[ "${1:-}" == --help ]]; then
    echo 'Usage: native/run-demo.sh [viewer|train] [route.csv] [train-directory]'
    exit 0
fi
mode="${1:-viewer}"
[[ "$mode" == viewer || "$mode" == train ]] || { echo 'Choose viewer or train.' >&2; exit 1; }
[[ $# -le 3 ]] || { echo 'Use --help for usage.' >&2; exit 1; }
bash "$here/prepare-content.sh"
route="${2:-$repo/Railway/Route/Mobitrens/Mobitrens Linha 1 - Gatos-Cachorros.csv}"
train="${3:-$repo/Train/Serie 1000 Cofebra}"
[[ -f "$route" && -d "$train" ]] || { echo 'Route/train not found. Run native/install-linux.sh first.' >&2; exit 1; }
train_file="$(find "$train" -maxdepth 1 -type f -iname train.dat -print -quit)"
[[ -n "$train_file" ]] || { echo 'The train directory has no train.dat.' >&2; exit 1; }
if [[ "$mode" == viewer ]]; then
    exec flatpak run --command=route-viewer-native org.openbve.OpenBVE.Native "$route"
else
    exec flatpak run --command=/app/lib/openbve/engine/OpenBve org.openbve.OpenBVE.Native "/route=$route" "/train=$train"
fi
