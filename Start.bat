@echo off
setlocal
cd /d "%~dp0"
if not exist "RussiaFlag.exe" (
  call Build.bat
  if errorlevel 1 (
    pause
    exit /b 1
  )
)
start "" /wait "%~dp0RussiaFlag.exe" --stop-soviet
if errorlevel 1 (
  echo Could not close SovietRoom. Close it from the system tray and try again.
  pause
  exit /b 1
)
start "" "%~dp0RussiaFlag.exe"
