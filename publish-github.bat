@echo off
setlocal
cd /d "%~dp0"
set REPO=Darschnid479/XenonForge

where gh >nul 2>nul || (
  echo GitHub CLI ^(gh^) is required: https://cli.github.com/
  exit /b 1
)

gh auth status || exit /b 1

if not exist .git git init -b main
git add .
git commit -m "Launch XenonForge 1.0" 2>nul

gh repo view %REPO% >nul 2>nul
if errorlevel 1 (
  gh repo create %REPO% --public --source=. --remote=origin --push --description "Modern native Xbox 360 ISO to GOD converter with metadata and USB deployment"
) else (
  git remote get-url origin >nul 2>nul || git remote add origin https://github.com/%REPO%.git
  git push -u origin main
)

git tag -f v1.0.0
git push origin v1.0.0 --force

echo Pushed source and tag. GitHub Actions will build and publish the release.
