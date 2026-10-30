@echo off
rem Couch Bowl: double-click to open the practice lab (throwing, catching, tackling and juking drills).
if not exist "%~dp0Builds\Windows\PocketFootball.exe" (
  echo The game has not been built on this computer yet. Ask Claude to run the build.
  pause
  exit /b 1
)
cd /d "%~dp0Builds\Windows"
start "" PocketFootball.exe -lab
