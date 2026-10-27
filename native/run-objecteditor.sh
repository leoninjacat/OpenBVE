#!/usr/bin/env bash
set -euo pipefail
here="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
editor="$here/dotnet10/objecteditor-runtime/ObjectEditor"
if [[ ! -x "$editor" ]]; then
    bash "$here/dotnet10/build-objecteditor.sh"
fi
# Avoid Mesa GL thread stalls when the viewport is reparented by GTK.
export mesa_glthread=false
exec "$editor" "$@"
