@echo off
setlocal
title Building FopherSync Standalone App...

echo ===================================================
echo    Packaging FopherSync into Standalone Executable
echo ===================================================
echo.

cd /d "%~dp0"

echo [1/3] Closing any running instances and preparing cat mascot icons...
taskkill /f /im FopherSync.exe >nul 2>&1
timeout /t 1 /nobreak >nul

if not exist "src\FopherSync.Wpf\Assets" mkdir "src\FopherSync.Wpf\Assets"
if exist "C:\Users\cmcha\.gemini\antigravity\brain\0c1ba891-097b-4db4-9d54-5ec23fbdcd4c\.user_uploaded\media_1790658250291.png" (
    copy /Y "C:\Users\cmcha\.gemini\antigravity\brain\0c1ba891-097b-4db4-9d54-5ec23fbdcd4c\.user_uploaded\media_1790658250291.png" "src\FopherSync.Wpf\Assets\cat.png" >nul
)
if exist "src\FopherSync.Wpf\Assets\cat.png" (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "$src = 'src\FopherSync.Wpf\Assets\cat.png'; Add-Type -AssemblyName System.Drawing; $b = [System.Drawing.Bitmap]::FromFile($src); $min = [Math]::Min($b.Width, $b.Height); $sr = New-Object System.Drawing.Rectangle(([int](($b.Width - $min) / 2)), ([int](($b.Height - $min) / 2)), $min, $min); $t = New-Object System.Drawing.Bitmap(128, 128); $g = [System.Drawing.Graphics]::FromImage($t); $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic; $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality; $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality; $dr = New-Object System.Drawing.Rectangle(0, 0, 128, 128); $g.DrawImage($b, $dr, $sr, [System.Drawing.GraphicsUnit]::Pixel); $g.Dispose(); $b.Dispose(); $ms = New-Object System.IO.MemoryStream; $t.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $t.Save('src\FopherSync.Wpf\Assets\cat_square.png', [System.Drawing.Imaging.ImageFormat]::Png); $png = $ms.ToArray(); $ms.Dispose(); $t.Dispose(); $bw = New-Object System.IO.BinaryWriter([System.IO.File]::Create('src\FopherSync.Wpf\Assets\app.ico')); $bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]1); $bw.Write([Byte]128); $bw.Write([Byte]128); $bw.Write([Byte]0); $bw.Write([Byte]0); $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$png.Length); $bw.Write([UInt32]22); $bw.Write($png); $bw.Close();"
)

echo [2/3] Compiling self-contained single-file FopherSync.exe...
dotnet publish src\FopherSync.Wpf\FopherSync.Wpf.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -o "%~dp0dist"

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] Build failed! Check errors above.
    pause
    exit /b %ERRORLEVEL%
)

if not exist "%~dp0dist\Assets" mkdir "%~dp0dist\Assets"
copy /Y "src\FopherSync.Wpf\Assets\*.*" "%~dp0dist\Assets\" >nul

if not exist "%~dp0dist\psshutdown.exe" (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; (New-Object Net.WebClient).DownloadFile('https://live.sysinternals.com/psshutdown.exe', '%~dp0dist\psshutdown.exe')" >nul 2>&1
)

echo.
echo [3/3] Creating Desktop Shortcut...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut([IO.Path]::Combine([Environment]::GetFolderPath('Desktop'), 'FopherSync.lnk')); $s.TargetPath = '%~dp0dist\FopherSync.exe'; $s.WorkingDirectory = '%~dp0dist'; $s.IconLocation = '%~dp0dist\FopherSync.exe,0'; $s.Description = 'FopherSync - Automated Backup Sentry'; $s.Save()"

echo.
echo ===================================================
echo   SUCCESS! 
echo   1. Standalone executable: %~dp0dist\FopherSync.exe
echo   2. Shortcut created on your Desktop: FopherSync
echo   3. Setup Scheduled Task (Run Once): "Run Once Only - Enable Task Scheduler Backups.bat"
echo ===================================================
powershell -NoProfile -ExecutionPolicy Bypass -Command "$run = $false; for ($i = 3; $i -gt 0; $i--) { $unit = if ($i -eq 1) { '1 Second ' } else { \"$i Seconds\" }; Write-Host -NoNewline \"`rRun now? Press Enter or Program Will Exit in: $unit\"; $sw = [Diagnostics.Stopwatch]::StartNew(); while ($sw.ElapsedMilliseconds -lt 1000) { try { if ([Console]::KeyAvailable) { $k = [Console]::ReadKey($true); if ($k.Key -eq [ConsoleKey]::Enter -or $k.KeyChar -eq 'y' -or $k.KeyChar -eq 'Y') { $run = $true; break } } } catch {} Start-Sleep -Milliseconds 50 } if ($run) { break } } Write-Host ''; if ($run) { exit 1 } else { exit 0 }"
if %ERRORLEVEL% EQU 1 (
    echo Starting FopherSync...
    start "" "%~dp0dist\FopherSync.exe"
)
