#define AppVersion "1.0.0"
#ifdef InstallerTest
  #define AppName "Kremlin Lake Wallpapers Test " + InstallerTest
  #define AppId "KremlinLake.Test." + InstallerTest
  #define OutputName "KremlinLakeWallpapers-Setup-Test"
#else
  #define AppName "Kremlin Lake Wallpapers"
  #define AppId "KremlinLakeWallpapers.MaksimDenisovRussia"
  #define OutputName "KremlinLakeWallpapers-Setup-" + AppVersion
#endif

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Максим Денисов
AppPublisherURL=https://github.com/MaksimDenisovRussia/kremlin-lake-wallpapers
AppSupportURL=https://github.com/MaksimDenisovRussia/kremlin-lake-wallpapers/issues
AppUpdatesURL=https://github.com/MaksimDenisovRussia/kremlin-lake-wallpapers/releases
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
PrivilegesRequired=lowest
MinVersion=10.0.14393
DisableWelcomePage=no
DisableDirPage=no
DisableProgramGroupPage=yes
SetupIconFile=..\assets\russia-flag.ico
UninstallDisplayIcon={app}\RussiaFlag.exe
UninstallDisplayName={#AppName}
OutputDir=..\installer-output
OutputBaseFilename={#OutputName}
VersionInfoVersion={#AppVersion}
VersionInfoDescription=Установка «Кремль и озеро»
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
CloseApplications=yes
CloseApplicationsFilter=RussiaFlag.exe
RestartApplications=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
russian.DesktopTask=Создать ярлык на рабочем столе
english.DesktopTask=Create a desktop shortcut
russian.LaunchApp=Запустить обои «Кремль и озеро»
english.LaunchApp=Start Kremlin Lake Wallpapers
russian.StopShortcut=Остановить обои
english.StopShortcut=Stop wallpapers
russian.DotNetMissing=Для этих обоев требуется .NET Framework 4.8 или новее.%n%nОткрыть официальную страницу Microsoft? После установки .NET запустите этот установщик снова.
english.DotNetMissing=These wallpapers require .NET Framework 4.8 or later.%n%nOpen the official Microsoft page? After installing .NET, run setup again.
russian.CloseFailed=Не удалось закрыть обои. Закройте их через значок флага в трее и повторите попытку.
english.CloseFailed=Could not close the wallpapers. Close them using the flag icon in the system tray and try again.
russian.DataKept=Настройки и файлы из ящиков сохранены в папке ниже. Для возврата файлов можно снова установить обои в эту же папку.
english.DataKept=Settings and pocket files have been preserved in the folder below. Reinstall into the same folder to restore files to their original locations.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopTask}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Explicit list: no personal settings, pocket manifests or user files are packaged.
Source: "..\RussiaFlag.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Start.bat"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Stop.bat"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\assets\kremlin-office.png"; DestDir: "{app}\assets"; Flags: ignoreversion
Source: "..\assets\office-original.jpg"; DestDir: "{app}\assets"; Flags: ignoreversion
Source: "..\assets\lake-rest.png"; DestDir: "{app}\assets"; Flags: ignoreversion
Source: "..\assets\window-view.png"; DestDir: "{app}\assets"; Flags: ignoreversion
Source: "..\assets\fishing-box.png"; DestDir: "{app}\assets"; Flags: ignoreversion
Source: "..\assets\russia-flag.ico"; DestDir: "{app}\assets"; Flags: ignoreversion
Source: "..\assets\anthem.mp3"; DestDir: "{app}\assets"; Flags: ignoreversion
Source: "..\assets\shaman-chorus.mp3"; DestDir: "{app}\assets"; Flags: ignoreversion
Source: "..\assets\shaman-full.mp3"; DestDir: "{app}\assets"; Flags: ignoreversion
Source: "..\assets\nature.mp3"; DestDir: "{app}\assets"; Flags: ignoreversion
Source: "..\assets\nature-license.txt"; DestDir: "{app}\assets"; Flags: ignoreversion
Source: "..\assets\shaman-source.txt"; DestDir: "{app}\assets"; Flags: ignoreversion
Source: "..\assets\office-source.txt"; DestDir: "{app}\assets"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\{#AppName}\{#AppName}"; Filename: "{app}\Start.bat"; WorkingDir: "{app}"; IconFilename: "{app}\RussiaFlag.exe"; Flags: runminimized
Name: "{userprograms}\{#AppName}\{cm:StopShortcut}"; Filename: "{app}\Stop.bat"; WorkingDir: "{app}"; IconFilename: "{app}\RussiaFlag.exe"; Flags: runminimized
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\Start.bat"; WorkingDir: "{app}"; IconFilename: "{app}\RussiaFlag.exe"; Flags: runminimized; Tasks: desktopicon

[Run]
Filename: "{app}\Start.bat"; Description: "{cm:LaunchApp}"; Flags: shellexec nowait postinstall skipifsilent runhidden

[Code]
function InitializeSetup(): Boolean;
var
  Release: Cardinal;
  ErrorCode: Integer;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) and (Release >= 528040);
  if not Result then
    if not WizardSilent then
      if MsgBox(ExpandConstant('{cm:DotNetMissing}'), mbInformation, MB_YESNO) = IDYES then
        ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet-framework/net48', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;

function StopInstalledWallpapers(): Boolean;
#ifndef InstallerTest
var
  ExitCode, Attempt: Integer;
#endif
begin
  Result := True;
#ifndef InstallerTest
  if not FileExists(ExpandConstant('{app}\RussiaFlag.exe')) then Exit;
  if not CheckForMutexes('Local\RussiaFlagWallpaper.Instance') then Exit;
  Result := False;
  if not Exec(ExpandConstant('{app}\RussiaFlag.exe'), '--stop', ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ExitCode) then Exit;
  if ExitCode <> 0 then Exit;
  for Attempt := 1 to 60 do
  begin
    if not CheckForMutexes('Local\RussiaFlagWallpaper.Instance') then
    begin
      Result := True;
      Exit;
    end;
    Sleep(250);
  end;
#endif
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not StopInstalledWallpapers() then Result := ExpandConstant('{cm:CloseFailed}');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    if not StopInstalledWallpapers() then RaiseException(ExpandConstant('{cm:CloseFailed}'));
  if CurUninstallStep = usPostUninstall then
    if not UninstallSilent then
      if DirExists(ExpandConstant('{app}\pocket-files')) or FileExists(ExpandConstant('{app}\settings.ini')) then
        MsgBox(ExpandConstant('{cm:DataKept}') + #13#10#13#10 + ExpandConstant('{app}'), mbInformation, MB_OK);
end;
