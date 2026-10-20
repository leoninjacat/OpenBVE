#!/usr/bin/env bash
set -euo pipefail
native="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd -- "$native/../.." && pwd)"
dotnet build "$native/RouteViewer/RouteViewer.csproj" -v:minimal
make -C "$repo/native/linux-launcher" build/route-picker build/route-editor
viewer="$native/routeviewer-runtime"
mkdir -p "$viewer"
cp -a "$native/RouteViewer/bin/Debug/net10.0/." "$viewer/"
cp "$repo/native/linux-launcher/build/route-picker" "$repo/native/linux-launcher/build/route-editor" "$viewer/"
[[ ! -L "$viewer/Data" ]] || unlink "$viewer/Data"
mkdir -p "$viewer/Data/Plugins"
cp -a "$repo/assets/." "$viewer/Data/"
for plugin in Route.CsvRw Object.CsvB3d Object.Animated Sound.RiffWave Texture.Native; do
    dotnet build "$native/$plugin/$plugin.csproj" -v:minimal
    plugin_dir="$native/$plugin/bin/Debug/net10.0"
    for dependency in "$plugin_dir/"*.dll; do
        if [[ "$(basename -- "$dependency")" != "$plugin.dll" ]]; then
            cp -a "$dependency" "$viewer/"
        fi
    done
    cp -a "$plugin_dir/$plugin.dll" "$viewer/Data/Plugins/"
done
