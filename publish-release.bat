@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

set "REPO=Darschnid479/XenonForge"
set "PROJECT=%CD%\XenonForge\XenonForge.vbproj"
set "PUBLISH=%CD%\publish"
set "DIST=%CD%\dist"

echo ============================================================
echo  XenonForge - Build + GitHub Release Publisher
echo ============================================================
echo.

if not exist "%PROJECT%" (
  echo ERROR: XenonForge project not found:
  echo   %PROJECT%
  echo.
  echo Put this BAT in the ROOT of the XenonForge repository.
  pause
  exit /b 1
)

where dotnet >nul 2>nul || (
  echo ERROR: .NET 8 SDK is required.
  echo https://dotnet.microsoft.com/download/dotnet/8.0
  pause
  exit /b 1
)

where git >nul 2>nul || (
  echo ERROR: Git is required.
  echo https://git-scm.com/download/win
  pause
  exit /b 1
)

where gh >nul 2>nul || (
  echo ERROR: GitHub CLI ^(gh^) is required.
  echo https://cli.github.com/
  pause
  exit /b 1
)

gh auth status >nul 2>nul
if errorlevel 1 (
  echo.
  echo GitHub CLI is not logged in.
  echo A browser login will start now.
  gh auth login
  if errorlevel 1 goto :fail
)

set "VERSION=%~1"
if not defined VERSION (
  set /p VERSION=Release version [example 1.0.1]: 
)
if not defined VERSION goto :fail
if /i "!VERSION:~0,1!"=="v" set "VERSION=!VERSION:~1!"
set "TAG=v!VERSION!"

echo.
echo Release: !TAG!
echo Repository: %REPO%
echo.

echo [1/7] Preparing embedded assets...
powershell -NoProfile -ExecutionPolicy Bypass -File "%CD%\prepare-assets.ps1"
if errorlevel 1 goto :fail

echo.
echo [2/7] Building self-contained Windows x64 EXE...
if exist "%PUBLISH%" rmdir /s /q "%PUBLISH%"
dotnet publish "%PROJECT%" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:Version=!VERSION! -o "%PUBLISH%"
if errorlevel 1 goto :fail
if not exist "%PUBLISH%\XenonForge.exe" (
  echo ERROR: publish\XenonForge.exe was not created.
  goto :fail
)

echo.
echo [3/7] Creating SHA-256 and release ZIP...
if exist "%DIST%" rmdir /s /q "%DIST%"
mkdir "%DIST%"
copy /y "%PUBLISH%\XenonForge.exe" "%DIST%\XenonForge.exe" >nul
powershell -NoProfile -ExecutionPolicy Bypass -Command "$h=(Get-FileHash -Algorithm SHA256 -LiteralPath '%DIST%\XenonForge.exe').Hash.ToLowerInvariant(); Set-Content -NoNewline -LiteralPath '%DIST%\XenonForge.exe.sha256' -Value ($h+'  XenonForge.exe'); Compress-Archive -Force -Path '%DIST%\XenonForge.exe','%DIST%\XenonForge.exe.sha256' -DestinationPath '%DIST%\XenonForge-win-x64-!TAG!.zip'"
if errorlevel 1 goto :fail

echo.
echo [4/7] Committing source...
if not exist ".git" (
  git init -b main
  if errorlevel 1 goto :fail
)
git remote get-url origin >nul 2>nul
if errorlevel 1 git remote add origin "https://github.com/%REPO%.git"

git add -A
git diff --cached --quiet
if errorlevel 1 (
  git commit -m "Release !TAG!"
  if errorlevel 1 goto :fail
) else (
  echo No source changes to commit.
)

echo.
echo [5/7] Pushing main...
git branch -M main
git push -u origin main
if errorlevel 1 goto :fail

echo.
echo [6/7] Creating tag !TAG!...
git tag -a "!TAG!" -m "XenonForge !TAG!"
if errorlevel 1 (
  echo ERROR: Tag !TAG! already exists locally.
  echo Choose a new version, for example: publish-release.bat 1.0.2
  goto :fail
)
git push origin "!TAG!"
if errorlevel 1 goto :fail

echo.
echo [7/7] Publishing GitHub Release...
set "NOTES_ARG=--generate-notes"
if exist "%CD%\RELEASE_NOTES.md" set "NOTES_ARG=--notes-file RELEASE_NOTES.md"

gh release create "!TAG!" ^
  "%DIST%\XenonForge.exe#XenonForge.exe" ^
  "%DIST%\XenonForge.exe.sha256#SHA-256 checksum" ^
  "%DIST%\XenonForge-win-x64-!TAG!.zip#Portable Windows x64 package" ^
  --repo "%REPO%" ^
  --title "XenonForge !TAG!" ^
  !NOTES_ARG!

if errorlevel 1 goto :fail

echo.
echo ============================================================
echo  RELEASE PUBLISHED SUCCESSFULLY
echo ============================================================
echo  Tag:     !TAG!
echo  EXE:     %DIST%\XenonForge.exe
echo  ZIP:     %DIST%\XenonForge-win-x64-!TAG!.zip
echo.
gh release view "!TAG!" --repo "%REPO%" --web
pause
exit /b 0

:fail
echo.
echo ============================================================
echo  RELEASE FAILED
echo ============================================================
echo Review the error above. Nothing after the failed step was run.
pause
exit /b 1
