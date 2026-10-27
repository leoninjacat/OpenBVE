# Development helper: refresh the generated payload after changing only setup code.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$WorkDirectory)
$ErrorActionPreference='Stop'
$repo=Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$work=[IO.Path]::GetFullPath($WorkDirectory)
$expected=Join-Path $repo 'native/dotnet10/artifacts/'
if(-not $work.StartsWith($expected,[StringComparison]::OrdinalIgnoreCase)){throw 'WorkDirectory must be an installer artifacts directory.'}
$payload=Join-Path $work 'payload'
$csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$references=@('/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll')
$source=Join-Path $PSScriptRoot 'Setup.cs'
$manifest=Join-Path $PSScriptRoot 'app.manifest'
$icon=Join-Path $repo 'assets/icon.ico'
& $csc /nologo /target:winexe /platform:x64 /optimize+ @references /define:UNINSTALL "/win32manifest:$manifest" "/win32icon:$icon" "/out:$(Join-Path $payload 'Desinstalar.exe')" $source
if($LASTEXITCODE -ne 0){throw 'Uninstaller compile failed'}
$lines=Get-Content (Join-Path $payload 'installation.sha256')
$hash=(Get-FileHash (Join-Path $payload 'Desinstalar.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
$lines=$lines | ForEach-Object {if($_.Substring(66) -eq 'Desinstalar.exe'){"$hash  Desinstalar.exe"}else{$_}}
$lines | Set-Content (Join-Path $payload 'installation.sha256') -Encoding UTF8
$zipPath=Join-Path $work 'Payload.zip'
$zip=[IO.Compression.ZipFile]::Open($zipPath,[IO.Compression.ZipArchiveMode]::Update)
try {
    foreach($name in @('Desinstalar.exe','installation.sha256')) {
        $entry=$zip.GetEntry($name); if($entry){$entry.Delete()}
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,(Join-Path $payload $name),$name,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}finally{$zip.Dispose()}
$output=Join-Path $repo 'native/dotnet10/artifacts/OpenBVE-LTARTS-Setup.exe'
& $csc /nologo /target:winexe /platform:x64 /optimize+ @references "/win32manifest:$manifest" "/win32icon:$icon" "/resource:$zipPath,Payload.zip" "/out:$output" $source
if($LASTEXITCODE -ne 0){throw 'Setup compile failed'}
Write-Output $output
