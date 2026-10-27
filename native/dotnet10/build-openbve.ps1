[CmdletBinding()]
param([string]$OutputDirectory, [switch]$Run, [ValidateSet('normal','cco')][string]$Mode='normal', [string]$Route, [string]$Train)
$ErrorActionPreference='Stop'
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$output=if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory)}else{Join-Path $PSScriptRoot 'openbve-windows-beta'}
function Publish([string]$Project,[string]$Destination) {
    dotnet publish $Project -c Release -r win-x64 --self-contained true -o $Destination -m:1 /nodeReuse:false /p:UseSharedCompilation=false /p:NuGetAudit=false --ignore-failed-sources -v minimal
    if($LASTEXITCODE -ne 0){throw "Falha ao compilar $Project"}
}
Publish (Join-Path $PSScriptRoot 'OpenBVE/OpenBve.csproj') $output
Publish (Join-Path $PSScriptRoot 'TrafficInstaller/TrafficInstaller.csproj') $output
$data=Join-Path $output 'Data'
New-Item -ItemType Directory -Force (Join-Path $data 'Plugins') | Out-Null
Copy-Item (Join-Path $repo 'assets/*') $data -Recurse -Force
foreach($plugin in @('Route.CsvRw','Train.OpenBve','Object.CsvB3d','Object.Animated','Sound.RiffWave','OpenBveAts','Texture.Native')) {
    $stage=Join-Path $PSScriptRoot "artifacts/windows-simulator/$plugin"
    Publish (Join-Path $PSScriptRoot "$plugin/$plugin.csproj") $stage
    Get-ChildItem $stage -Filter '*.dll' | Where-Object Name -ne "$plugin.dll" | Copy-Item -Destination $output -Force
    Copy-Item (Join-Path $stage "$plugin.dll") (Join-Path $data 'Plugins') -Force
}
Copy-Item (Join-Path $repo 'dependencies/x64/*') $output -Recurse -Force
Write-Host "OpenBVE pronto: $output"
if($Run){
    if(-not $Route){$Route=Join-Path $repo 'Railway/Route/Mobitrens/Linha 1/Mobitrens Linha 1 (GAT-CAC) - night.csv'}
    if(-not $Train){$Train=Join-Path $repo 'Train/Serie 1000 Cofebra'}
    & (Join-Path $output 'OpenBve.exe') "/route=$([IO.Path]::GetFullPath($Route))" "/train=$([IO.Path]::GetFullPath($Train))" "/mode=$Mode"
    if($LASTEXITCODE -ne 0){throw "OpenBVE encerrou com código $LASTEXITCODE"}
}
