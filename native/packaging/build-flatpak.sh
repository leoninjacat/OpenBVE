#!/usr/bin/env bash
# Build on Fedora 44; verify the resulting binaries in GNOME Platform 50.
set -euo pipefail
here="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd -- "$here/../.." && pwd)"
native="$repo/native/dotnet10"
app_id=org.openbve.OpenBVE.Native
output="$here/dist"
for command in dotnet flatpak make pkg-config; do
    command -v "$command" >/dev/null || { echo "Build tool missing: $command" >&2; exit 1; }
done
source /etc/os-release
[[ "$ID" == fedora && "$VERSION_ID" == 44 && "$(uname -m)" == x86_64 ]] || {
    echo 'Build this package on Fedora 44 x86_64 (the installed app runs on other Flatpak distributions).' >&2
    exit 1
}
flatpak info org.gnome.Platform//50 >/dev/null
mkdir -p "$output"
stage="$(mktemp -d "$here/.stage.XXXXXX")"
trap 'rm -rf -- "$stage"' EXIT
app="$stage/app"
flatpak build-init "$app" "$app_id" org.gnome.Platform org.gnome.Platform 50
files="$app/files"
engine="$files/lib/openbve/engine"
mkdir -p "$engine/Data/Plugins" "$files/bin" "$files/share/applications" "$files/share/icons/hicolor/scalable/apps"
dotnet publish "$native/OpenBVE/OpenBve.csproj" -c Release -r linux-x64 --self-contained true -o "$engine" -v:minimal
# Optional .NET LTTng tracing provider is unused by the simulator and depends
# on a developer profiling library outside the platform.
rm -f "$engine/libcoreclrtraceptprovider.so"
cp -a "$repo/assets/." "$engine/Data/"
for plugin in Route.CsvRw Train.OpenBve Object.CsvB3d Object.Animated Sound.RiffWave OpenBveAts Texture.Native; do
    dotnet build "$native/$plugin/$plugin.csproj" -c Release -v:minimal
    plugin_dir="$native/$plugin/bin/Release/net10.0"
    for dependency in "$plugin_dir/"*.dll; do
        if [[ "$(basename -- "$dependency")" != "$plugin.dll" ]]; then
            cp -a "$dependency" "$engine/"
        fi
    done
    cp -a "$plugin_dir/$plugin.dll" "$engine/Data/Plugins/"
done
dotnet run --project "$native/CameraTests" -c Release
dotnet run --project "$native/NativeTests" -c Release
dotnet run --project "$native/RepeatTests" -c Release -- "$repo/Railway/Route/Mobitrens/Linha 1/Trens_XML"
viewer="$files/lib/openbve/route-viewer"
dotnet publish "$native/RouteViewer/RouteViewer.csproj" -c Release -r linux-x64 --self-contained true -o "$viewer" -v:minimal
rm -f "$viewer/libcoreclrtraceptprovider.so"
ln -s ../engine/Data "$viewer/Data"
make -C "$repo/native/linux-launcher" build/route-picker build/route-editor
cp "$repo/native/linux-launcher/build/route-picker" "$repo/native/linux-launcher/build/route-editor" "$viewer/"
install -m755 "$here/route-viewer-native" "$files/bin/route-viewer-native"
install -m644 "$here/$app_id.RouteViewer.desktop" "$files/share/applications/"
object_editor="$files/lib/openbve/object-editor"
dotnet publish "$native/ObjectEditor/ObjectEditor.csproj" -c Release -r linux-x64 --self-contained true -o "$object_editor" -v:minimal
rm -f "$object_editor/libcoreclrtraceptprovider.so"
ln -s ../engine/Data "$object_editor/Data"
cp "$repo/native/linux-launcher/build/route-picker" "$repo/native/linux-launcher/build/route-editor" "$object_editor/"
install -m755 "$here/object-editor-native" "$files/bin/object-editor-native"
install -m644 "$here/$app_id.ObjectEditor.desktop" "$files/share/applications/"

make -C "$repo/native/linux-launcher" all check check-paths
cp "$repo/native/linux-launcher/build/openbve-launcher" "$files/lib/openbve/"
# Fedora's XML parser uses SONAME 2; GNOME 50 ships SONAME 16. Bundle the
# matching parser privately rather than substituting incompatible ABIs.
mkdir -p "$files/lib/openbve/lib" "$files/share/licenses/openbve/libxml2"
cp -L /usr/lib64/libxml2.so.2 "$files/lib/openbve/lib/"
cp -a /usr/share/licenses/libxml2/. "$files/share/licenses/openbve/libxml2/"
install -m755 "$here/openbve-native" "$files/bin/openbve-native"
install -m644 "$here/$app_id.desktop" "$files/share/applications/"
cp "$repo/graphics/logo_icon.svg" "$files/share/icons/hicolor/scalable/apps/$app_id.svg"
mkdir -p "$files/share/licenses/openbve"
cp -a "$repo/licenses/." "$files/share/licenses/openbve/"
for package in skiasharp skiasharp.nativeassets.linux.nodependencies; do
    mkdir -p "$files/share/licenses/openbve/$package"
    package_dir="${NUGET_PACKAGES:-$HOME/.nuget/packages}/$package/4.153.1"
    cp "$package_dir/LICENSE.txt" "$files/share/licenses/openbve/$package/"
    if [[ -f "$package_dir/THIRD-PARTY-NOTICES.txt" ]]; then
        cp "$package_dir/THIRD-PARTY-NOTICES.txt" "$files/share/licenses/openbve/$package/"
    fi
done
cp "$repo/Readme.md" "$files/share/licenses/openbve/PROJECT.md"
cp "$here/README.md" "$files/share/licenses/openbve/NATIVE-PACKAGE.md"
# Verify the exact staged plugin, not a DLL from a previous development build.
dotnet build "$native/TextureAudit/TextureAudit.csproj" -c Release -v:minimal
for content in "$engine/Data" "$repo/Railway" "$repo/Train"; do
    dotnet "$native/TextureAudit/bin/Release/net10.0/TextureAudit.dll" \
        "$content" "$stage/texture-audit-$(basename -- "$content").json" \
        "$engine/Data/Plugins/Texture.Native.dll"
done
# Read access lets the user select routes for editing and trains; settings stay in
# Flatpak's private XDG directory. GPU drivers are supplied by Flatpak extensions.
flatpak build-finish --command=openbve-native --share=ipc --socket=x11 \
    --socket=wayland --socket=pulseaudio --device=dri --filesystem=home "$app"
# Exercise the bundled .NET runtime and all ELF dependencies inside the target runtime.
flatpak build "$app" /app/lib/openbve/engine/OpenBve --help
flatpak build "$app" /app/lib/openbve/route-viewer/RouteViewer --help
flatpak build "$app" /app/lib/openbve/object-editor/ObjectEditor --help
flatpak build --env=LD_LIBRARY_PATH=/app/lib/openbve/lib "$app" sh -ec '
    for binary in /app/lib/openbve/openbve-launcher /app/lib/openbve/engine/*.so /app/lib/openbve/route-viewer/*.so /app/lib/openbve/route-viewer/route-picker /app/lib/openbve/route-viewer/route-editor /app/lib/openbve/object-editor/*.so /app/lib/openbve/object-editor/route-picker /app/lib/openbve/object-editor/route-editor; do
        result=$(ldd "$binary" 2>&1) || { echo "$result"; exit 1; }
        case "$result" in *"not found"*) echo "$result"; exit 1;; esac
    done
'
flatpak build-export "$output/repo" "$app" stable
flatpak build-bundle --runtime-repo=https://dl.flathub.org/repo/flathub.flatpakrepo \
    "$output/repo" "$output/OpenBVE-Native-linux-x86_64.flatpak" "$app_id" stable
(cd "$output" && sha256sum OpenBVE-Native-linux-x86_64.flatpak > OpenBVE-Native-linux-x86_64.flatpak.sha256)
printf '\nInstaller ready: %s/OpenBVE-Native-linux-x86_64.flatpak\n' "$output"
