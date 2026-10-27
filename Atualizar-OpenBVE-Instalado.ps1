$ErrorActionPreference = 'Stop'
$installedRoot = 'C:\Users\thoma\OneDrive\Documentos\OpenBVE by LT ARTS'
try {
    foreach ($package in @(@('native\dotnet10\openbve-windows-beta','App','OpenBve.exe'),@('native\dotnet10\routeviewer-windows','Editor','RouteViewer.exe'))) {
        $packageSource = Join-Path $PSScriptRoot $package[0]
        $packageTarget = Join-Path $installedRoot $package[1]
        if (!(Test-Path -LiteralPath (Join-Path $packageTarget $package[2]))) { throw "Instalacao nao encontrada: $packageTarget" }
        if (!(Test-Path -LiteralPath (Join-Path $packageSource 'Data\Plugins\Route.CsvRw.dll'))) { throw 'Carregador atualizado nao encontrado.' }
    }
    foreach ($package in @(@('native\dotnet10\openbve-windows-beta','App','OpenBve.exe'),@('native\dotnet10\routeviewer-windows','Editor','RouteViewer.exe'))) {
        $packageSource = Join-Path $PSScriptRoot $package[0]
        $packageTarget = Join-Path $installedRoot $package[1]
        Get-ChildItem -LiteralPath $packageSource -File | Where-Object { $_.Extension -in '.exe','.dll','.json','.pdb' } | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $packageTarget $_.Name) -Force
        }
        Copy-Item -LiteralPath (Join-Path $packageSource 'Data\Plugins\Route.CsvRw.dll') -Destination (Join-Path $packageTarget 'Data\Plugins\Route.CsvRw.dll') -Force
        foreach ($verifyName in @($package[2],'OpenBveApi.dll','RouteManager2.dll','TrainManager.dll','Data\Plugins\Route.CsvRw.dll')) {
            if ((Get-FileHash -LiteralPath (Join-Path $packageSource $verifyName)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $packageTarget $verifyName)).Hash) { throw "Falha na verificacao: $verifyName" }
        }
    }
    Write-Host 'Simulador e Route Editor atualizados. Suas rotas e configuracoes foram preservadas.' -ForegroundColor Green
} catch {
    Write-Host 'Atualizacao nao concluida. Feche o simulador e o editor antes de tentar novamente.' -ForegroundColor Red
    Write-Host $_
    exit 1
}
