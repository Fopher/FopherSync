@echo off
setlocal enabledelayedexpansion
title Building FopherSync Setup Installer...
cd /d "%~dp0"

echo ==========================================================
echo   Building FopherSync Setup Installer for End Users
echo ==========================================================
echo.

:: 1. Check for Inno Setup Compiler
set "ISCC="
if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" set "ISCC=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if exist "C:\Program Files\Inno Setup 6\ISCC.exe" set "ISCC=C:\Program Files\Inno Setup 6\ISCC.exe"
if exist "C:\Program Files (x86)\Inno Setup 5\ISCC.exe" set "ISCC=C:\Program Files (x86)\Inno Setup 5\ISCC.exe"
if exist "C:\Program Files\Inno Setup 5\ISCC.exe" set "ISCC=C:\Program Files\Inno Setup 5\ISCC.exe"
if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"

if not defined ISCC (
    echo [!] Inno Setup compiler was not found on your system.
    echo     Installing Inno Setup via winget...
    echo.
    winget install --id JRSoftware.InnoSetup -e --accept-source-agreements --accept-package-agreements
    if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" set "ISCC=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
    if exist "C:\Program Files\Inno Setup 6\ISCC.exe" set "ISCC=C:\Program Files\Inno Setup 6\ISCC.exe"
    if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
)

if not defined ISCC (
    echo.
    echo [ERROR] Could not find or install Inno Setup.
    echo Please install it manually with: winget install --id JRSoftware.InnoSetup -e
    pause
    exit /b 1
)

echo [OK] Found Inno Setup: "!ISCC!"
echo.

:: 2. Build and Publish Self-Contained Release
echo [1/2] Compiling and publishing FopherSync (.NET 8 WPF x64)...
dotnet publish src\FopherSync.Wpf\FopherSync.Wpf.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=false

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] .NET build failed!
    pause
    exit /b %ERRORLEVEL%
)

:: Ensure publish directory has assets and license files
set "PUB_DIR=src\FopherSync.Wpf\bin\Release\net8.0-windows\win-x64\publish"
if not exist "%PUB_DIR%\Assets" mkdir "%PUB_DIR%\Assets"
copy /Y "src\FopherSync.Wpf\Assets\app.ico" "%PUB_DIR%\Assets\" >nul 2>&1
copy /Y "packaging\eula.txt" "%PUB_DIR%\EULA.txt" >nul 2>&1
copy /Y "packaging\license.txt" "%PUB_DIR%\LICENSE.txt" >nul 2>&1
copy /Y "packaging\agreement.txt" "%PUB_DIR%\Agreement.txt" >nul 2>&1

:: 3. Compile the Inno Setup installer
echo.
echo [2/2] Generating standalone setup executable...
if not exist "dist" mkdir "dist"

"!ISCC!" "packaging\installer.iss"

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] Inno Setup compilation failed!
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo ==========================================================
echo   SUCCESS! Consumer installer created:
echo   %~dp0dist\FopherSync_Setup_v1.0.0.exe
echo ==========================================================
echo.
echo You can give "dist\FopherSync_Setup_v1.0.0.exe" to any user.
echo When they double-click it, the classic setup wizard will run.
echo.
pause
