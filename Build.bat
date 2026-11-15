@echo off
setlocal
cd /d "%~dp0"
set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
"%CSC%" /nologo /target:winexe /optimize+ /unsafe /platform:anycpu /win32manifest:app.manifest /win32icon:assets\russia-flag.ico /resource:assets\russia-flag.ico,RussiaFlag.Tray.ico /out:RussiaFlag.exe /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:Accessibility.dll src\*.cs
if errorlevel 1 exit /b 1
echo Built RussiaFlag.exe
