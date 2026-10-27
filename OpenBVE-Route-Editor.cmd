@echo off
cd /d "%~dp0"
"native\dotnet10\routeviewer-windows\RouteViewer.exe" %*
if errorlevel 1 pause
