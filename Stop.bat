@echo off
cd /d "%~dp0"
if exist "RussiaFlag.exe" start "" /wait "%~dp0RussiaFlag.exe" --stop
