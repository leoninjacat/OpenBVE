@echo off
cd /d "%~dp0"
"native\dotnet10\openbve-windows-beta\OpenBve.exe" /mode=normal
if errorlevel 1 pause
