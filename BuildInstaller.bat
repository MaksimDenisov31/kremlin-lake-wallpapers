@echo off
setlocal
cd /d "%~dp0"
if not exist RussiaFlag.exe (
  call Build.bat
  if errorlevel 1 exit /b 1
)
set "WALLPAPER_ISCC="
if exist "%ProgramFiles%\Inno Setup 7\ISCC.exe" set "WALLPAPER_ISCC=%ProgramFiles%\Inno Setup 7\ISCC.exe"
if not defined WALLPAPER_ISCC if exist "%ProgramFiles(x86)%\Inno Setup 7\ISCC.exe" set "WALLPAPER_ISCC=%ProgramFiles(x86)%\Inno Setup 7\ISCC.exe"
if not defined WALLPAPER_ISCC if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "WALLPAPER_ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not defined WALLPAPER_ISCC if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "WALLPAPER_ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not "%~1"=="" set "WALLPAPER_ISCC=%~1"
if not defined WALLPAPER_ISCC (
  echo Install Inno Setup from https://jrsoftware.org/inno-setup/downloads
  exit /b 1
)
"%WALLPAPER_ISCC%" installer\Wallpapers.iss
if errorlevel 1 exit /b 1
echo Installer is ready in installer-output.
