@echo off
setlocal
set "ROOT=%~dp0"
if not exist "%ROOT%native\dotnet10\openbve-windows-beta\OpenBve.exe" set "ROOT=%USERPROFILE%\Documents\OpenBVE\"
set "APP=%ROOT%native\dotnet10\openbve-windows-beta\OpenBve.exe"
set "ROUTE=%ROOT%Railway\Route\Mobitrens\IdaVolta\idavolta.csv"
set "TRAIN=%ROOT%Train\Serie 1000 Cofebra"
set "LOG=%ROOT%native\dotnet10\artifacts\idavolta-ultimo-inicio.log"
if not exist "%APP%" goto missing
if not exist "%ROUTE%" goto missing
if not exist "%ROOT%Railway\Route\Mobitrens\IdaVolta\idavolta.roundtrip.xml" goto missing
if not exist "%TRAIN%\Train.dat" goto missing
if not exist "%ROOT%native\dotnet10\artifacts" mkdir "%ROOT%native\dotnet10\artifacts"
cd /d "%ROOT%native\dotnet10\openbve-windows-beta"
echo Abrindo o teste de ida e volta. Aguarde o carregamento...
echo Registro: "%LOG%"
start "" /b /wait "%APP%" "/route=%ROUTE%" "/train=%TRAIN%" /mode=normal %* > "%LOG%" 2>&1
set "RESULT=%ERRORLEVEL%"
if not "%RESULT%"=="0" goto failed
exit /b 0
:missing
echo Nao foi possivel encontrar os arquivos do teste.
echo Pasta procurada: "%ROOT%"
echo Execute este atalho na pasta Documents\OpenBVE do projeto.
pause
exit /b 1
:failed
echo.
echo O simulador encerrou com erro %RESULT%.
echo Registro completo: "%LOG%"
echo.
type "%LOG%"
echo.
pause
exit /b %RESULT%
