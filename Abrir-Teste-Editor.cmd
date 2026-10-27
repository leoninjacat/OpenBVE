@echo off
cd /d "%~dp0"
"native\dotnet10\routeviewer-windows\RouteViewer.exe" "Railway\Route\Mobitrens\Linha 2 Vermelha (idavolta)\meuteste.csv"
if errorlevel 1 pause
