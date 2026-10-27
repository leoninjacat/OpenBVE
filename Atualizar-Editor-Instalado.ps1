$ErrorActionPreference = 'Stop'
$editorSource = Join-Path $PSScriptRoot 'native\dotnet10\routeviewer-windows'
$editorTarget = 'C:\Users\thoma\OneDrive\Documentos\OpenBVE by LT ARTS\Editor'
if (!(Test-Path -LiteralPath (Join-Path $editorTarget 'RouteViewer.exe'))) { throw 'Editor instalado nao encontrado.' }
try {
    Get-ChildItem -LiteralPath $editorSource -File | Where-Object { $_.Extension -in '.exe','.dll','.json','.pdb' } | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $editorTarget $_.Name) -Force
    }
    Copy-Item -LiteralPath (Join-Path $editorSource 'Data\Plugins\Route.CsvRw.dll') -Destination (Join-Path $editorTarget 'Data\Plugins\Route.CsvRw.dll') -Force
    foreach ($editorName in 'RouteViewer.exe','RouteViewer.dll') {
        if ((Get-FileHash -LiteralPath (Join-Path $editorSource $editorName)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $editorTarget $editorName)).Hash) { throw "Falha ao verificar $editorName" }
    }
    Write-Host 'OpenBVE Route Editor By LT ARTS atualizado com sucesso.' -ForegroundColor Green
} catch {
    Write-Host 'Nao foi possivel concluir. Feche o editor e tente novamente.' -ForegroundColor Red
    Write-Host $_
    exit 1
}
