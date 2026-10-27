#!/usr/bin/env bash
# Install the published self-contained app; routes/trains stay in the checkout.
set -euo pipefail
here="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd -- "$here/.." && pwd)"
if [[ "${1:-}" == --help ]]; then
    echo 'Usage: native/install-linux.sh [--build]'
    echo 'Default: download the pinned GitHub release and install the user Flatpak.'
    echo '--build: generate the Flatpak locally (Fedora 44 x86_64 build dependencies required).'
    exit 0
fi
[[ $# == 0 || ( $# == 1 && "$1" == --build ) ]] || { echo 'Use --help for usage.' >&2; exit 1; }
[[ "$(uname -m)" == x86_64 ]] || { echo 'This release requires Linux x86_64.' >&2; exit 1; }
for tool in flatpak sha256sum; do
    command -v "$tool" >/dev/null || { echo "Install the required tool: $tool" >&2; exit 1; }
done
bash "$here/prepare-content.sh"
bundle=OpenBVE-Native-linux-x86_64.flatpak
output="$here/packaging/dist"
mkdir -p "$output"
if [[ "${1:-}" == --build ]]; then
    bash "$here/packaging/build-flatpak.sh"
else
    command -v curl >/dev/null || { echo 'Install curl to download the release.' >&2; exit 1; }
    version="$(cat "$here/packaging/release-version")"
    url="https://github.com/leoninjacat/OpenBVE/releases/download/$version"
    download="$(mktemp -d "$output/.download.XXXXXX")"
    trap 'rm -rf -- "$download"' EXIT
    curl --fail --location --retry 3 "$url/$bundle.sha256" -o "$download/$bundle.sha256"
    if ! (cd "$output" && sha256sum --check --status "$download/$bundle.sha256" 2>/dev/null); then
        curl --fail --location --retry 3 "$url/$bundle" -o "$download/$bundle"
        (cd "$download" && sha256sum --check "$bundle.sha256")
        mv -- "$download/$bundle" "$output/$bundle"
    fi
    cp -- "$download/$bundle.sha256" "$output/$bundle.sha256"
fi
(cd "$output" && sha256sum --check "$bundle.sha256")
# Supplies GNOME Platform 50 automatically when installing the bundle.
flatpak remote-add --user --if-not-exists flathub https://dl.flathub.org/repo/flathub.flatpakrepo
flatpak install --user --reinstall --noninteractive -y "$output/$bundle"
printf '\nInstalled OpenBVE, Route Viewer / Editor and Object Editor (local builds). Test the included content:\n'
printf '  bash %q viewer\n  bash %q train\n' "$here/run-demo.sh" "$here/run-demo.sh"
