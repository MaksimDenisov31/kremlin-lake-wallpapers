$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Push-Location $repo
try {
    New-Item -ItemType Directory -Force .tools\video-stage\assets,.tools\video-output | Out-Null
    Copy-Item assets\kremlin-office.png,assets\lake-rest.png,assets\window-view.png,assets\fishing-box.png .tools\video-stage\assets
    $compiler = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    & $compiler /nologo /target:exe /optimize+ /unsafe /platform:anycpu /main:RussiaFlag.ReviewVideo /out:.tools\video-stage\RenderReview.exe /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:Accessibility.dll src\*.cs tools\video\RenderReview.cs
    if ($LASTEXITCODE -ne 0) { throw 'Video renderer compilation failed' }
    python tools/video/make_review.py
    if ($LASTEXITCODE -ne 0) { throw 'Video encoding failed' }
    python tools/video/verify_review.py
    if ($LASTEXITCODE -ne 0) { throw 'Video verification failed' }
    Copy-Item .tools\video-output\Kremlin-Lake-Wallpapers-Review-1080p.mp4 media\youtube\Kremlin-Lake-Wallpapers-Review-1080p.mp4
} finally { Pop-Location }
