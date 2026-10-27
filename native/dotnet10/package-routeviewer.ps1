[CmdletBinding()]
param([switch]$IncludeDemo,[switch]$SkipBuild)
$ErrorActionPreference='Stop'
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$dotnet=Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
if(-not $dotnet -and (Test-Path "$env:ProgramFiles/dotnet/dotnet.exe")){$dotnet="$env:ProgramFiles/dotnet/dotnet.exe"}
if(-not $dotnet){throw 'O SDK .NET 10 e necessario para validar o pacote antes de gerar o ZIP.'}
if(-not $SkipBuild){& (Join-Path $PSScriptRoot 'build-routeviewer.ps1')}
$runtime=Join-Path $PSScriptRoot 'routeviewer-windows'
foreach($required in @('RouteViewer.exe','RouteViewer.Desktop.exe','coreclr.dll','libSkiaSharp.dll','OpenAL32.dll','Data/Plugins/Route.CsvRw.dll')) {
    if(-not (Test-Path (Join-Path $runtime $required))){throw "Build incompleto: $required"}
}
$name='RouteViewer-LTARTS-Windows-x64'+$(if($IncludeDemo){'-com-rotas'})
$artifacts=Join-Path $PSScriptRoot 'artifacts'
$stage=Join-Path $artifacts ('portable-'+[guid]::NewGuid().ToString('N'))
$bundle=Join-Path $stage $name
New-Item -ItemType Directory -Force $bundle | Out-Null
Copy-Item (Join-Path $runtime '*') $bundle -Recurse -Force
# Keep test captures, debug symbols and local error reports out of distribution.
Get-ChildItem $bundle -Recurse -File | Where-Object { $_.Extension -in '.pdb','.log' } | ForEach-Object {
    # Each resolved target is explicitly checked before deletion.
    if(-not $_.FullName.StartsWith($bundle+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Arquivo fora do pacote'}
    Remove-Item -LiteralPath $_.FullName
}
Copy-Item (Join-Path $repo 'licenses') $bundle -Recurse
Copy-Item (Join-Path $repo 'Readme.md') (Join-Path $bundle 'OpenBVE-README.md')
Copy-Item (Join-Path $repo 'native/windows-desktop/LEIA-ME.txt') $bundle
New-Item -ItemType File (Join-Path $bundle 'portable.flag') | Out-Null
$packages=if($env:NUGET_PACKAGES){$env:NUGET_PACKAGES}else{Join-Path $env:USERPROFILE '.nuget/packages'}
Copy-Item (Join-Path $packages 'skiasharp/4.153.1/LICENSE.txt') (Join-Path $bundle 'licenses/SkiaSharp.txt')
foreach($framework in @('microsoft.netcore.app.runtime.win-x64','microsoft.windowsdesktop.app.runtime.win-x64')) {
    $config=Get-Content (Join-Path $runtime 'RouteViewer.Desktop.runtimeconfig.json') -Raw | ConvertFrom-Json
    $version=$config.runtimeOptions.includedFrameworks | Select-Object -First 1 -ExpandProperty version
    $folder=Join-Path $packages "$framework/$version"
    foreach($notice in @('LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')) {
        if(Test-Path (Join-Path $folder $notice)){Copy-Item (Join-Path $folder $notice) (Join-Path $bundle "licenses/$framework-$notice")}
    }
}
@'
@echo off
cd /d "%~dp0"
RouteViewer.exe %* 1>viewer-output.log 2>viewer-errors.log
if errorlevel 1 (
  echo Falha ao abrir. Envie os arquivos de log ao desenvolvedor.
  pause
)
'@ | Set-Content (Join-Path $bundle 'Abrir com diagnostico.cmd') -Encoding ASCII
if($IncludeDemo) {
    Write-Host 'Incluindo rotas, objetos, sons e trem de teste...'
    Copy-Item (Join-Path $repo 'Railway'),(Join-Path $repo 'Train') $bundle -Recurse
    Copy-Item (Join-Path $repo 'CONTEUDO.md') $bundle
    $audio=Join-Path $bundle 'Railway/Sound/L2 verde/SomPlat.wav'
    if(-not (Test-Path $audio)) {
        $stream=[IO.File]::Create($audio)
        try {
            Get-ChildItem (Join-Path $repo 'content-parts/SomPlat.wav/part-*') | Sort-Object Name | ForEach-Object {
                $part=[IO.File]::OpenRead($_.FullName)
                try{$part.CopyTo($stream)}finally{$part.Dispose()}
            }
        }finally{$stream.Dispose()}
    }
    if((Get-FileHash $audio -Algorithm SHA256).Hash -ne '43410e4277dcb119d9dcd08497dfbb0977dda19874054c8c1d535d76c57d9a3f') {throw 'Audio de teste incompleto ou alterado.'}
@'
@echo off
cd /d "%~dp0"
RouteViewer.exe "Railway\Route\Mobitrens\Mobitrens Linha 1 - Gatos-Cachorros.csv"
if errorlevel 1 pause
'@ | Set-Content (Join-Path $bundle 'Testar rota.cmd') -Encoding ASCII
}
$zip=Join-Path $artifacts ($name+'-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.zip')
Write-Host 'Validando as imagens com o plugin exato que sera distribuido...'
& $dotnet build (Join-Path $PSScriptRoot 'TextureAudit/TextureAudit.csproj') -c Release -m:1 /nodeReuse:false /p:UseSharedCompilation=false -v minimal
if($LASTEXITCODE -ne 0){throw 'Falha ao compilar o validador de texturas. ZIP nao gerado.'}
$auditor=Join-Path $PSScriptRoot 'TextureAudit/bin/Release/net10.0/TextureAudit.dll'
$plugin=Join-Path $bundle 'Data/Plugins/Texture.Native.dll'
$checks=foreach($folder in @('Data','Railway','Train')) {
    $root=Join-Path $bundle $folder
    if(-not (Test-Path $root)){continue}
    $report=Join-Path $artifacts ('texture-validation-'+[IO.Path]::GetFileName($stage)+'-'+$folder+'.json')
    & $dotnet $auditor $root $report $plugin | Out-Host
    if($LASTEXITCODE -ne 0){throw "Texturas invalidas em $folder. ZIP nao gerado. Relatorio: $report"}
    Get-Content $report -Raw | ConvertFrom-Json
}
$lines=@('VALIDACAO DE TEXTURAS - OpenBVE by LT ARTS', ('Plugin SHA-256: '+$checks[0].PluginSha256))
foreach($check in $checks){$lines+=([IO.Path]::GetFileName($check.Root)+': '+$check.Checked+' imagens verificadas; '+$check.Failed+' falhas.')}
$lines+=@('Formatos verificados: BMP, PNG, GIF, JPEG e TIFF.','Consulta de dimensoes e decodificacao completas, sem diagnosticos de erro.','Verificacao limitada aos arquivos incluidos; nao garante integridade de rotas externas.')
$lines | Set-Content (Join-Path $bundle 'VALIDACAO.txt') -Encoding UTF8
Write-Host 'Compactando pacote portatil...'
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage,$zip,[IO.Compression.CompressionLevel]::Optimal,$false)
$hash=(Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($zip))" | Set-Content ($zip+'.sha256') -Encoding ASCII
Write-Host "ZIP pronto: $zip"
Write-Output $zip
