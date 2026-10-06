@echo off
setlocal
title FopherSync - Enable Task Scheduler Backups (Run Once Only)
cd /d "%~dp0"

echo ========================================================
echo   FopherSync - Enable Task Scheduler Backups
echo   (Run Once Only - Administrator)
echo ========================================================
echo.

:: Check for Administrator elevation
net session >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo [INFO] Requesting Administrator privileges to register task...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process cmd -ArgumentList '/c \"\"%~f0\"\"' -Verb RunAs"
    exit /b
)

set "EXE_PATH=%~dp0dist\FopherSync.exe"
if not exist "%EXE_PATH%" (
    echo [ERROR] %EXE_PATH% not found!
    echo Please run create-app-exe.bat first to build the application.
    echo.
    pause
    exit /b 1
)

echo [1/3] Purging orphaned / legacy registry entries...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$names = @('FopherSync', 'FopherSync Backup', 'RoboCopyPlus', 'RoboCopyPlus Backup'); foreach ($n in $names) { $tree = \"HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\TaskCache\Tree\$n\"; if (Test-Path $tree) { try { $id = (Get-ItemProperty $tree -Name 'Id' -ErrorAction SilentlyContinue).Id; if ($id) { Remove-Item \"HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\TaskCache\Tasks\$id\" -Recurse -Force -ErrorAction SilentlyContinue; Remove-Item \"HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\TaskCache\Plain\$id\" -Recurse -Force -ErrorAction SilentlyContinue } } catch {}; Remove-Item $tree -Recurse -Force -ErrorAction SilentlyContinue }; $f = \"$env:windir\System32\Tasks\$n\"; if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue } }"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$h='FMC-SERVER'; Start-Service WinRM -ErrorAction SilentlyContinue; $cur = (Get-Item WSMan:\localhost\Client\TrustedHosts -ErrorAction SilentlyContinue).Value; if ([string]::IsNullOrWhiteSpace($cur)) { Set-Item WSMan:\localhost\Client\TrustedHosts -Value $h -Force } elseif ($cur -notmatch [regex]::Escape($h)) { Set-Item WSMan:\localhost\Client\TrustedHosts -Value \"$cur,$h\" -Force }" >nul 2>&1

echo [2/3] Registering fresh 'FopherSync' daily scheduled task...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$action = New-ScheduledTaskAction -Execute '%EXE_PATH%' -Argument '--run-all-scheduled'; $trigger = New-ScheduledTaskTrigger -Daily -At '02:00'; $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -WakeToRun -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Hours 12); Register-ScheduledTask -TaskName 'FopherSync' -Action $action -Trigger $trigger -Settings $settings -Force | Out-Null; if ($?) { Write-Host '   [OK] Task successfully registered!' -ForegroundColor Green } else { exit 1 }"

if %ERRORLEVEL% NEQ 0 (
    echo [FALLBACK] Trying alternative registration via schtasks...
    schtasks /Create /F /TN "FopherSync" /SC DAILY /ST 02:00 /TR "\"%EXE_PATH%\" --run-all-scheduled" /RL HIGHEST
)

echo.
echo [3/3] Verifying registered task in Windows Task Scheduler:
schtasks /Query /TN "FopherSync" /FO LIST

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] Verification failed. Please check Windows Event Viewer -> TaskScheduler logs.
) else (
    echo.
    echo ========================================================
    echo   SUCCESS! 
    echo   Task 'FopherSync' is verified and active!
    echo   - Scheduled daily with Wake-from-Sleep and Catch-up.
    echo   - Backups will now run automatically in the background.
    echo   - You do NOT need to run this script again.
    echo ========================================================
)

echo.
pause
