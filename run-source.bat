@echo off
setlocal
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0prepare-assets.ps1" || exit /b 1
dotnet run --project "%~dp0XenonForge\XenonForge.vbproj" -c Debug
