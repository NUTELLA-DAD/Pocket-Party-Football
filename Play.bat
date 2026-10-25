@echo off
rem Pocket Party Football: double-click to start the game.
if not exist "%~dp0Builds\Windows\PocketFootball.exe" (
  echo The game has not been built on this computer yet. Ask Claude to run the build.
  pause
  exit /b 1
)
cd /d "%~dp0Builds\Windows"
start "" PocketFootball.exe
