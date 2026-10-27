[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')][string]$Configuration='Release',
    [switch]$Run,
    [string]$Route,
    [string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$viewer=if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory)}else{Join-Path $PSScriptRoot 'routeviewer-windows'}
$dotnet=Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
if(-not $dotnet -and (Test-Path "$env:ProgramFiles/dotnet/dotnet.exe")){$dotnet="$env:ProgramFiles/dotnet/dotnet.exe"}
if (-not $dotnet) {
    throw 'Instale o SDK .NET 10 x64 e abra novamente o terminal: https://dotnet.microsoft.com/download/dotnet/10.0'
}
function Publish-Project([string]$Project,[string]$Destination) {
    & $dotnet publish $Project -c $Configuration -r win-x64 --self-contained true -o $Destination -m:1 /nodeReuse:false /p:UseSharedCompilation=false /p:NuGetAudit=false --ignore-failed-sources -v minimal
    if($LASTEXITCODE -ne 0){throw "Falha ao compilar: $Project"}
}
Publish-Project (Join-Path $PSScriptRoot 'RouteViewer/RouteViewer.csproj') $viewer
Publish-Project (Join-Path $repo 'native/windows-desktop/RouteViewer.Desktop.csproj') $viewer
$data=Join-Path $viewer 'Data'
New-Item -ItemType Directory -Force (Join-Path $data 'Plugins') | Out-Null
Copy-Item (Join-Path $repo 'assets/*') $data -Recurse -Force
foreach($plugin in @('Route.CsvRw','Object.CsvB3d','Object.Animated','Sound.RiffWave','Texture.Native')) {
    $stage=Join-Path $PSScriptRoot "artifacts/windows/$plugin"
    Publish-Project (Join-Path $PSScriptRoot "$plugin/$plugin.csproj") $stage
    # Runtime dependencies live beside RouteViewer; only entry assemblies go in Plugins.
    Get-ChildItem $stage -Filter '*.dll' | Where-Object Name -ne "$plugin.dll" | Copy-Item -Destination $viewer -Force
    Copy-Item (Join-Path $stage "$plugin.dll") (Join-Path $data 'Plugins') -Force
}
Copy-Item (Join-Path $repo 'dependencies/x64/*') $viewer -Recurse -Force
New-Item -ItemType File -Force (Join-Path $viewer 'portable.flag') | Out-Null
Write-Host "Build pronto: $viewer"
if($Run) {
    $executable=Join-Path $viewer 'RouteViewer.exe'
    if($Route){& $executable ([IO.Path]::GetFullPath($Route))}else{& $executable}
    if($LASTEXITCODE -ne 0){throw "Route Viewer encerrou com código $LASTEXITCODE"}
}
