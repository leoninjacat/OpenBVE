[CmdletBinding()]
param([switch]$RebuildRuntime,[switch]$SkipInstallTest)
$ErrorActionPreference='Stop'
$repo=Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$native=Join-Path $repo 'native/dotnet10'
$runtime=Join-Path $native 'openbve-windows-beta'
$editorRuntime=Join-Path $native 'routeviewer-windows'
$artifacts=Join-Path $native 'artifacts'
$work=Join-Path $artifacts ('installer-'+[guid]::NewGuid().ToString('N'))
$payload=Join-Path $work 'payload'
$csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if(-not (Test-Path $csc)){throw 'Compilador .NET Framework 4 não encontrado.'}
if($RebuildRuntime){& (Join-Path $native 'build-openbve.ps1'); & (Join-Path $native 'build-routeviewer.ps1')}
foreach($file in @('OpenBve.exe','coreclr.dll','System.Windows.Forms.dll','OpenAL32.dll','Data/Plugins/Train.OpenBve.dll','Data/Plugins/Route.CsvRw.dll','Data/Plugins/Texture.Native.dll')) {
    if(-not (Test-Path (Join-Path $runtime $file))){throw "Runtime incompleto: $file"}
}
New-Item -ItemType Directory -Force $payload | Out-Null
New-Item -ItemType Directory -Force (Join-Path $payload 'App') | Out-Null
Get-ChildItem $runtime | Where-Object Name -NotLike 'OpenBVE.Launcher*' | Copy-Item -Destination (Join-Path $payload 'App') -Recurse
foreach($file in @('RouteViewer.exe','RouteViewer.Desktop.exe','Data/Plugins/Texture.Native.dll')) {
    if(-not(Test-Path (Join-Path $editorRuntime $file))){throw "Route Editor incompleto: $file"}
}
Copy-Item $editorRuntime (Join-Path $payload 'Editor') -Recurse
New-Item -ItemType File -Force (Join-Path $payload 'Editor/portable.flag') | Out-Null
# A fresh installation must never inherit developer settings, captures or logs.
foreach($folder in @('App/UserData','App/.config','Editor/UserData','Editor/.config')){
    $target=[IO.Path]::GetFullPath((Join-Path $payload $folder))
    if(-not $target.StartsWith($payload+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Pasta fora do pacote'}
    if(Test-Path $target){Remove-Item -LiteralPath $target -Recurse -Force}
}
Get-ChildItem (Join-Path $payload 'App'),(Join-Path $payload 'Editor') -Recurse -File | Where-Object {$_.Extension -in '.pdb','.log','.exp','.lib'} | ForEach-Object {
    if(-not $_.FullName.StartsWith($payload+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Arquivo fora do pacote'}
    Remove-Item -LiteralPath $_.FullName
}
New-Item -ItemType Directory -Force (Join-Path $payload 'Railway/Route') | Out-Null
Copy-Item (Join-Path $repo 'Railway/Route/Mobitrens') (Join-Path $payload 'Railway/Route/Mobitrens') -Recurse
foreach($part in @('Object','Sound','Train')) {
    if(Test-Path (Join-Path $repo "Railway/$part")){Copy-Item (Join-Path $repo "Railway/$part") (Join-Path $payload "Railway/$part") -Recurse}
}
Copy-Item (Join-Path $repo 'Train') $payload -Recurse
$audio=Join-Path $payload 'Railway/Sound/L2 verde/SomPlat.wav'
if(-not (Test-Path $audio)) {
    New-Item -ItemType Directory -Force (Split-Path $audio) | Out-Null
    $stream=[IO.File]::Create($audio)
    try {foreach($part in (Get-ChildItem (Join-Path $repo 'content-parts/SomPlat.wav/part-*') | Sort-Object Name)) {
        $input=[IO.File]::OpenRead($part.FullName); try{$input.CopyTo($stream)}finally{$input.Dispose()}
    }}finally{$stream.Dispose()}
}
if((Get-FileHash $audio -Algorithm SHA256).Hash -ne '43410e4277dcb119d9dcd08497dfbb0977dda19874054c8c1d535d76c57d9a3f'){throw 'Áudio SomPlat incompleto.'}
Copy-Item (Join-Path $repo 'licenses') (Join-Path $payload 'Licencas') -Recurse
Copy-Item (Join-Path $repo 'CONTEUDO.md') $payload
Copy-Item (Join-Path $repo 'Readme.md') (Join-Path $payload 'OpenBVE-README.md')
Copy-Item (Join-Path $repo 'assets/icon.ico') (Join-Path $payload 'OpenBVE.ico')
Copy-Item (Join-Path $PSScriptRoot 'LEIA-ME.txt') $payload
foreach($mode in @('normal','cco')) {
    $name=if($mode -eq 'cco'){'Iniciar-CCO.cmd'}else{'Iniciar-OpenBVE.cmd'}
    $launcher=@'
@echo off
cd /d "%~dp0"
"App\OpenBve.exe" /mode=MODE
if errorlevel 1 pause
'@
    $launcher.Replace('MODE',$mode) | Set-Content (Join-Path $payload $name) -Encoding ASCII
}
@'
@echo off
cd /d "%~dp0"
"Editor\RouteViewer.exe" %*
if errorlevel 1 pause
'@ | Set-Content (Join-Path $payload 'OpenBVE-Route-Editor.cmd') -Encoding ASCII
# Runtime license notices are distributed with the self-contained runtime.
$packages=if($env:NUGET_PACKAGES){$env:NUGET_PACKAGES}else{Join-Path $env:USERPROFILE '.nuget/packages'}
$config=Get-Content (Join-Path $runtime 'OpenBve.runtimeconfig.json') -Raw | ConvertFrom-Json
foreach($framework in $config.runtimeOptions.includedFrameworks) {
    $folder=Join-Path $packages ($framework.name.ToLowerInvariant()+'.runtime.win-x64/'+$framework.version)
    foreach($notice in @('LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')){
        if(Test-Path (Join-Path $folder $notice)){Copy-Item (Join-Path $folder $notice) (Join-Path $payload ('Licencas/'+$framework.name+'-'+$notice))}
    }
}
Write-Host 'Validando todas as texturas do pacote...'
$auditor=Join-Path $native 'TextureAudit/bin/Release/net10.0/TextureAudit.dll'
if(-not(Test-Path $auditor)){throw 'Compile primeiro native/dotnet10/TextureAudit/TextureAudit.csproj.'}
$plugin=Join-Path $payload 'App/Data/Plugins/Texture.Native.dll'
foreach($folder in @('App/Data','Editor/Data','Railway','Train')) {
    $report=Join-Path $work ($folder.Replace('/','-')+'-textures.json')
    dotnet $auditor (Join-Path $payload $folder) $report $plugin
    if($LASTEXITCODE -ne 0){throw "Falha nas texturas de $folder; instalador não gerado."}
}
$references=@('/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll')
$source=Join-Path $PSScriptRoot 'Setup.cs'
$icon=Join-Path $repo 'assets/icon.ico'
$appManifest=Join-Path $PSScriptRoot 'app.manifest'
& $csc /nologo /target:winexe /platform:x64 /optimize+ @references /define:UNINSTALL "/win32manifest:$appManifest" "/win32icon:$icon" "/out:$(Join-Path $payload 'Desinstalar.exe')" $source
if($LASTEXITCODE -ne 0){throw 'Falha ao compilar desinstalador.'}
Write-Host 'Gerando manifesto de integridade...'
$manifest=foreach($file in Get-ChildItem $payload -Recurse -File | Sort-Object FullName) {
    $relative=[IO.Path]::GetRelativePath($payload,$file.FullName)
    (Get-FileHash $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+$relative
}
$manifest | Set-Content (Join-Path $payload 'installation.sha256') -Encoding UTF8
Write-Host 'Compactando aplicativo, rota e trens...'
$zip=Join-Path $work 'Payload.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($payload,$zip,[IO.Compression.CompressionLevel]::Optimal,$false)
$output=Join-Path $artifacts ('OpenBVE-LTARTS-Completo-Setup-'+(Get-Date -Format yyyyMMdd-HHmmss)+'.exe')
& $csc /nologo /target:winexe /platform:x64 /optimize+ @references "/win32manifest:$appManifest" "/win32icon:$icon" "/resource:$zip,Payload.zip" "/out:$output" $source
if($LASTEXITCODE -ne 0){throw 'Falha ao compilar instalador.'}
if(-not $SkipInstallTest) {
    $test=Join-Path $work 'Teste instalação com espaços e acentos'
    Write-Host 'Testando extração e todos os hashes, sem alterar atalhos ou registro...'
    $process=Start-Process -FilePath $output -ArgumentList @('--verify-install',('"'+$test+'"')) -WindowStyle Hidden -PassThru -Wait
    if($process.ExitCode -ne 0){throw 'O teste de instalação falhou. Consulte %TEMP%/OpenBVE-LTARTS-setup-error.txt.'}
    if(-not(Test-Path ($test+'.verification.txt'))){throw 'Relatório de instalação não encontrado.'}
}
$hash=(Get-FileHash $output -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($output))" | Set-Content ($output+'.sha256') -Encoding ASCII
@("Build: $output", "SHA256: $hash", "Work: $work", "TestDirectory: $test", "InstallTest: $(-not $SkipInstallTest)") | Set-Content ($output+'.validation.txt')
Write-Host "Instalador pronto: $output"
Write-Output $output
