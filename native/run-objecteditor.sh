#!/usr/bin/env bash
set -euo pipefail
here="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
editor="$here/dotnet10/objecteditor-runtime/ObjectEditor"
if [[ ! -x "$editor" ]]; then
    bash "$here/dotnet10/build-objecteditor.sh"
fi
# Avoid Mesa GL thread stalls when the viewport is reparented by GTK.
export mesa_glthread=false
# GTK reparenting can restore driver vsync and throttle XWayland to 1 FPS.
# The viewer already limits rendering to 60 FPS.
export vblank_mode=0
exec "$editor" "$@"
