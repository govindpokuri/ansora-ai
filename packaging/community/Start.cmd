@echo off
cd /d "%~dp0"
powershell.exe -NoProfile -File "%~dp0run-community.ps1"
if errorlevel 1 (
  echo.
  echo Setup did not finish. Read FIRST-RUN.txt for prerequisites and troubleshooting.
  pause
)
