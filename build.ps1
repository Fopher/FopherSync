# FopherSync Build & Publish Script

$ErrorActionPreference = "Stop"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host " Building FopherSync (.NET 8 WPF x64)" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

# 1. Restore & Build Solution
Write-Host "`n[1/3] Building solution..." -ForegroundColor Yellow
dotnet build FopherSync.sln -c Release

# 2. Publish Self-Contained Desktop Application
Write-Host "`n[2/3] Publishing self-contained win-x64 executable..." -ForegroundColor Yellow
dotnet publish src\FopherSync.Wpf\FopherSync.Wpf.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false

$publishDir = "src\FopherSync.Wpf\bin\Release\net8.0-windows\win-x64\publish"
$publishAssets = Join-Path $publishDir "Assets"
if (!(Test-Path $publishAssets)) { New-Item -ItemType Directory -Path $publishAssets -Force | Out-Null }
Copy-Item "src\FopherSync.Wpf\Assets\app.ico" -Destination "$publishAssets\app.ico" -Force
if (Test-Path "src\FopherSync.Wpf\Assets\app_icon.png") {
    Copy-Item "src\FopherSync.Wpf\Assets\app_icon.png" -Destination "$publishAssets\app_icon.png" -Force
}
# Copy EULA, License, and Uninstaller into publish folder
Copy-Item "packaging\eula.txt" -Destination "$publishDir\EULA.txt" -Force
Copy-Item "packaging\license.txt" -Destination "$publishDir\LICENSE.txt" -Force
Copy-Item "packaging\agreement.txt" -Destination "$publishDir\Agreement.txt" -Force
Copy-Item "packaging\Uninstall-FopherSync.ps1" -Destination "$publishDir\Uninstall-FopherSync.ps1" -Force
Copy-Item "packaging\Uninstall.bat" -Destination "$publishDir\Uninstall.bat" -Force
Write-Host "Published to: $publishDir (with Assets, EULA.txt, LICENSE.txt, Agreement.txt, and Uninstaller)" -ForegroundColor Green

# Refresh Windows Explorer icon cache
try {
    Add-Type -MemberDefinition '[DllImport("shell32.dll")] public static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);' -Name Win32Shell -Namespace Win32 -EA SilentlyContinue
    [Win32.Win32Shell]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)
} catch {}

# 3. Inno Setup Packaging (Classic Wizard Installer)
Write-Host "`n[3/3] Checking for Inno Setup compiler..." -ForegroundColor Yellow
$isccPaths = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 5\ISCC.exe",
    "C:\Program Files\Inno Setup 5\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 5\ISCC.exe"
)
$pathCmd = (Get-Command iscc -ErrorAction SilentlyContinue)?.Source
if ($pathCmd) { $isccPaths = @($pathCmd) + $isccPaths }

$iscc = $isccPaths | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if ($iscc) {
    Write-Host "Compiling classic setup installer with Inno Setup ($iscc)..." -ForegroundColor Cyan
    if (!(Test-Path "dist")) { New-Item -ItemType Directory -Path "dist" -Force | Out-Null }
    & $iscc "packaging\installer.iss"
    Write-Host "Installer generated in dist\FopherSync_Setup_v1.0.0.exe" -ForegroundColor Green
} else {
    Write-Host "Inno Setup compiler (ISCC.exe) not found in standard paths." -ForegroundColor Yellow
    Write-Host "Install Inno Setup 6 (https://jrsoftware.org/isdl.php) to compile packaging\installer.iss," -ForegroundColor Yellow
    Write-Host "or run packaging\Setup-Wizard.ps1 for the standalone Windows wizard." -ForegroundColor Yellow
}

Write-Host "`nBuild complete!" -ForegroundColor Green
