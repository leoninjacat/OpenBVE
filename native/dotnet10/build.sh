#!/usr/bin/env bash
set -euo pipefail
native_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_dir="$(cd -- "$native_dir/../.." && pwd)"
runtime_dir="$native_dir/runtime"
mkdir -p "$native_dir/artifacts"
stage_dir="$(mktemp -d "$native_dir/artifacts/runtime-stage.XXXXXX")"
trap 'rm -rf -- "$stage_dir"' EXIT
mkdir -p "$stage_dir/Data/Plugins"
dotnet build "$native_dir/OpenBVE/OpenBve.csproj" -v:minimal
cp -a "$native_dir/OpenBVE/bin/Debug/net10.0/." "$stage_dir/"
cp -a "$repo_dir/assets/." "$stage_dir/Data/"
for plugin in Route.CsvRw Train.OpenBve Object.CsvB3d Object.Animated Sound.RiffWave OpenBveAts Texture.Native; do
    dotnet build "$native_dir/$plugin/$plugin.csproj" -v:minimal
    plugin_dir="$native_dir/$plugin/bin/Debug/net10.0"
    # Common dependencies share the executable's default assembly load context.
    for dependency in "$plugin_dir/"*.dll; do
        if [[ "$(basename -- "$dependency")" != "$plugin.dll" ]]; then
            cp -a "$dependency" "$stage_dir/"
        fi
    done
    cp -a "$plugin_dir/$plugin.dll" "$stage_dir/Data/Plugins/"
done
# Do not update a working runtime until every build has passed. Preserve UserData.
mkdir -p "$runtime_dir"
cp -a "$stage_dir/." "$runtime_dir/"
printf 'Native engine ready: %s/OpenBve\n' "$runtime_dir"
