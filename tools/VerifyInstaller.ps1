param([string]$Compiler = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$testId = [Guid]::NewGuid().ToString('N').Substring(0,12)
$root = Join-Path $repo ('.tools\wallpaper-installer-tests\'+$testId)
$target = Join-Path $root 'Установка с пробелами'
$name = 'Kremlin Lake Wallpapers Test '+$testId
$id = 'KremlinLake.Test.'+$testId+'_is1'
[IO.Directory]::CreateDirectory($target) | Out-Null
function Assert([bool]$ok,[string]$message) { if (-not $ok) { throw $message } }
function Run([string]$file,[string[]]$arguments) {
    $p=Start-Process -FilePath $file -ArgumentList $arguments -WorkingDirectory $target -WindowStyle Hidden -PassThru -Wait
    Assert ($p.ExitCode -eq 0) ("Exit code $($p.ExitCode): $file")
}
function Registered {
    $base=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,[Microsoft.Win32.RegistryView]::Registry32)
    $key=$base.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Uninstall\'+$id)
    $value=if($key){$key.GetValue('DisplayName');$key.Dispose()}else{$null};$base.Dispose();return $value
}
# Unique AppId and no global stop commands: existing user wallpapers are unaffected.
& $Compiler /Qp ("/DInstallerTest="+$testId) ("/O"+$root) (Join-Path $repo 'installer\Wallpapers.iss')
Assert ($LASTEXITCODE -eq 0) 'Test compilation failed.'
$setup=Join-Path $root 'KremlinLakeWallpapers-Setup-Test.exe'
$argsForSetup=@('/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART','/NOCLOSEAPPLICATIONS','/LANG=russian',('/DIR="'+$target+'"'),'/TASKS=desktopicon')
$uninstaller=Join-Path $target 'unins000.exe'
$link=Join-Path ([Environment]::GetFolderPath('Programs')) ($name+'\'+$name+'.lnk')
$desktop=Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) ($name+'.lnk')
$original=Join-Path $root 'Мой документ.txt'
$token=[Guid]::NewGuid().ToString('N')
$relative='pocket-files\lake\'+$token+'\Мой документ.txt'
$stored=Join-Path $target $relative
$manifest=Join-Path $target 'pocket-lake.txt'
$settings=Join-Path $target 'settings.ini'
try {
    Run $setup $argsForSetup
    Assert ((Registered) -eq $name) 'Uninstall registration missing.'
    Assert ((Test-Path -LiteralPath $link) -and (Test-Path -LiteralPath $desktop)) 'Shortcuts missing.'
    $shell=New-Object -ComObject WScript.Shell
    Assert ($shell.CreateShortcut($link).TargetPath -eq (Join-Path $target 'Start.bat')) 'Wrong shortcut target.'
    Assert (-not (Test-Path -LiteralPath $settings)) 'Personal settings leaked into package.'
    Assert (-not (Test-Path -LiteralPath (Join-Path $target 'pocket-files'))) 'Pocket files leaked into package.'
    foreach($file in @('RussiaFlag.exe','Start.bat','Stop.bat','assets\kremlin-office.png','assets\office-original.jpg','assets\lake-rest.png','assets\window-view.png','assets\fishing-box.png','assets\anthem.mp3','assets\shaman-chorus.mp3','assets\shaman-full.mp3','assets\nature.mp3')) {
        Assert ((Get-FileHash -LiteralPath (Join-Path $repo $file)).Hash -eq (Get-FileHash -LiteralPath (Join-Path $target $file)).Hash) ('Runtime bytes differ: '+$file)
    }
    Run (Join-Path $target 'RussiaFlag.exe') @('--render-test')
    Run (Join-Path $target 'RussiaFlag.exe') @('--startup-test')
    [IO.File]::WriteAllText($settings,"office=original`r`nmusic=none`r`n")
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($stored)) | Out-Null
    [IO.File]::WriteAllText($original,'Pocket fixture: preserve and return.')
    [IO.File]::Move($original,$stored)
    $record=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($original))+"`t"+[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($relative))
    [IO.File]::WriteAllText($manifest,$record+"`r`n",[Text.Encoding]::UTF8)
    $settingsHash=(Get-FileHash -LiteralPath $settings).Hash;$manifestHash=(Get-FileHash -LiteralPath $manifest).Hash
    Run $setup $argsForSetup
    Assert ((Get-FileHash -LiteralPath $settings).Hash -eq $settingsHash) 'Update changed preferences.'
    Assert ((Get-FileHash -LiteralPath $manifest).Hash -eq $manifestHash) 'Update changed return addresses.'
    Run $uninstaller @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART')
    Assert (-not (Test-Path -LiteralPath (Join-Path $target 'RussiaFlag.exe'))) 'Uninstall left program.'
    Assert ($null -eq (Registered)) 'Uninstall entry remains.'
    Assert (-not (Test-Path -LiteralPath $link) -and -not (Test-Path -LiteralPath $desktop)) 'Shortcuts remain.'
    Assert ((Get-FileHash -LiteralPath $settings).Hash -eq $settingsHash) 'Uninstall lost settings.'
    Assert ((Get-FileHash -LiteralPath $manifest).Hash -eq $manifestHash) 'Uninstall lost return addresses.'
    Assert ([IO.File]::ReadAllText($stored) -eq 'Pocket fixture: preserve and return.') 'Uninstall lost pocket file.'
    Run $setup $argsForSetup
    Run (Join-Path $target 'RussiaFlag.exe') @('--restore-pocket','lake',$token)
    Assert ([IO.File]::ReadAllText($original) -eq 'Pocket fixture: preserve and return.') 'Return after reinstall failed.'
    Assert (-not (Test-Path -LiteralPath $stored)) 'Return duplicated file.'
    Run $uninstaller @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART')
    Assert (Test-Path -LiteralPath $original) 'Final uninstall removed returned file.'
    $report='PASS: isolated installation; Unicode path; runtime/media hashes; no private data in package; shortcuts and Windows registration; scene rendering; update preserves settings/pockets; uninstall preserves return addresses and pocket contents; reinstall restores the file without duplication. Existing user wallpapers were not stopped.'
    [IO.File]::WriteAllText((Join-Path $repo '.tools\wallpaper-installer-verification.txt'),$report+[Environment]::NewLine);$report
} finally { if(Test-Path -LiteralPath $uninstaller){Run $uninstaller @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART')} }
