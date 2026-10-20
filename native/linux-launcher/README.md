# Launcher GTK4/libadwaita

Build: `make -C native/linux-launcher`

Run from the repository: `native/linux-launcher/build/openbve-launcher`.
The older `native/linux-launcher/openbve-launcher` binary is not updated by this build.
Requires GTK >= 4.10, libadwaita >= 1.4, libxml2 and pkg-config development packages.

Select a route **file** or a train **directory**. Route filters support CSV/RW;
all files remain accessible for inspection. Cancellation preserves the selection.
Paths can also be edited manually; Enter refreshes their preview. Editing clears
old details. Text metadata is loaded on a worker thread; results from older
selections are discarded. Missing or undecodable images clear the old image and
show a message. Start now launches the native .NET engine; build it first with
`native/dotnet10/build.sh`. The launcher passes paths as separate arguments and
waits asynchronously, preventing duplicate starts while the child is running.

## Original behavior inspected

- `source/OpenBVE/UserInterface/formMain.Start.cs`, ShowRouteInformation / ShowTrain.
- `source/Plugins/Train.OpenBve/Plugin.cs`, GetDescription / GetImage.
- `source/Plugins/Route.CsvRw/Namespaces/NonTrack/Route.cs`.
- `source/Plugins/Route.CsvRw/CsvRwRouteParser.Preprocess.cs` (RW description).

The original readers are C# plugin methods tied to the host, route model and
renderer; they are not directly callable from the C prototype. This increment
reuses their filename priorities and resolution rules, rather than loading the
Mono runtime or modifying the original readers. GLib handles text conversion and
libxml2 handles Train/Description XML. A future .NET preview service should call
the existing plugins and replace preview.c behind its small Preview interface.

## Supported preview and remaining differences from Mono

Train: first nonempty `/openBVE/Train/Description`, then train.txt, readme.txt,
read me.txt; train.png, train.gif, train.bmp. Windows-style path separators and
case-insensitive pack filenames are resolved. Missing train.dat/train.xml warns;
this is not plugin-level train validation. MSTS trains are not supported yet.

Route: literal Route.Comment / Route.Image, With Route with .Comment / .Image,
RW [Route] commands and initial RW description; fallback image with route basename
in PNG, BMP, GIF, TIFF, TIF, JPEG, JPG order. Image paths are relative to the route.
This is a **partial preview**, not equivalent to parsing a route in Mono. Includes,
$Chr/$Sub/$If and other expressions, multiline/quoted commands, multiple CSV
commands on one line, compatibility patches, default train, map, altitude profile,
encoding controls and plugin-specific metadata are not implemented. The UI always
states the route-preview limitation. Images depend on installed GDK decoders;
GIF animation is not played.

UTF-8 (with/without BOM), UTF-16 BOM and Windows-1252 fallback are supported.
Mono's encoding autodetection (including Japanese legacy encodings) is not reused;
non-Western packs without a BOM can need conversion. Large descriptions are
scrollable with the window; image decoding still occurs on the GTK thread.

## Validation

`make -C native/linux-launcher check` runs temporary fixtures for XML priority,
text fallback, Windows-1252, case-insensitive images, literal CSV metadata,
basename-image fallback, RW description and missing paths. Fixtures are removed.
Build and tests passed with GTK 4.22.5 / libadwaita 1.9.4 / libxml2 2.12.10.
A four-second smoke run under an isolated D-Bus session stayed alive until timeout;
the desktop emitted portal, accessibility and theme configuration warnings.
Filepicker interaction and visual layout have not been manually verified.


## Native engine handoff validation

`make -C native/linux-launcher check-ui` requires a graphical display and tests
missing paths, exact argument passing (including spaces and shell characters),
successful exit and nonzero exit against a temporary executable. This does not
replace manual picker interaction. See ../dotnet10/README.md for the real engine
build, driving controls and complete route/train smoke run.

## Instalador Linux

Para instalar launcher e motor juntos, com .NET incluído e entrada no menu, veja
[../packaging/README.md](../packaging/README.md). No pacote o motor é encontrado
em `engine/OpenBve`, ao lado do launcher. Os caminhos de desenvolvimento e a
variável `OPENBVE_NATIVE_ENGINE` continuam disponíveis.

## Caminhos selecionados no Flatpak

O seletor pode retornar `/run/user/UID/doc/ID/arquivo.csv`. O launcher resolve o
caminho original pela API Documents.GetHostPaths antes da prévia e da partida,
para preservar a busca das pastas Railway/Object e Railway/Sound. Usa somente
permissões já concedidas ao sandbox; se o original não estiver acessível, mostra
o erro em vez de iniciar com um CSV isolado. Caminhos digitados passam pela mesma
resolução. `make check-paths` testa o tratamento básico; `build/test-paths` aceita
pares caminho-do-portal/caminho-original para testar grants reais no sandbox.
