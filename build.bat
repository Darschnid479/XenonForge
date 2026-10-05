@echo off
setlocal
cd /d "%~dp0"

echo ============================================================
echo  XenonForge - Release Build
echo  Native VB.NET Xbox ISO to GOD engine - no iso2god.exe
echo ============================================================
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
  echo ERROR: .NET 8 SDK was not found.
  echo Install it from https://dotnet.microsoft.com/download/dotnet/8.0
  pause
  exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0prepare-assets.ps1"
if errorlevel 1 goto :fail

echo.
echo Building self-contained Windows x64 single-file release...
dotnet publish "%~dp0XenonForge\XenonForge.vbproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%~dp0publish"
if errorlevel 1 goto :fail

echo.
echo Build complete:
echo   %~dp0publish\XenonForge.exe
for %%F in ("%~dp0publish\XenonForge.exe") do echo   Size: %%~zF bytes
powershell -NoProfile -Command "(Get-FileHash '%~dp0publish\XenonForge.exe' -Algorithm SHA256).Hash | Set-Content '%~dp0publish\XenonForge.exe.sha256'"
echo.
pause
exit /b 0

:fail
echo.
echo BUILD FAILED.
pause
exit /b 1
