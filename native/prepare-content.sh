#!/usr/bin/env bash
# Restore the single asset exceeding GitHub's per-file size limit, without LFS.
set -euo pipefail
here="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd -- "$here/.." && pwd)"
target="$repo/Railway/Sound/L2 verde/SomPlat.wav"
parts="$repo/content-parts/SomPlat.wav"
expected=43410e4277dcb119d9dcd08497dfbb0977dda19874054c8c1d535d76c57d9a3f
if [[ -e "$target" ]]; then
    actual="$(sha256sum -- "$target")"
    [[ "${actual%% *}" == "$expected" ]] && exit 0
    echo "Content differs from the bundled original; preserving existing file: $target" >&2
    echo 'Move it aside before retrying if you want to restore the bundled version.' >&2
    exit 1
fi
mkdir -p -- "$(dirname -- "$target")"
temporary="$(mktemp "$target.restore.XXXXXX")"
trap 'rm -f -- "$temporary"' EXIT
cat -- "$parts"/part-* > "$temporary"
actual="$(sha256sum -- "$temporary")"
[[ "${actual%% *}" == "$expected" ]] || { echo 'Incomplete or damaged content parts; SHA-256 mismatch.' >&2; exit 1; }
mv -- "$temporary" "$target"
printf 'Restored bundled audio: %s\n' "$target"
