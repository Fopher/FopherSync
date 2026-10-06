# FopherSync Complete Uninstaller & System Cleaner
# Safely removes FopherSync binaries, Windows scheduled tasks, shortcuts, registry entries, and user data.

param(
    [switch]$Silent = $false,
    [switch]$PurgeUserData = $false,
    [string]$InstallDir = ""
)

# Ensure script is running with elevated administrator privileges
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
$isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    # Self-elevate with UAC prompt
    $scriptPath = $MyInvocation.MyCommand.Definition
    $argList = "-NoProfile -ExecutionPolicy Bypass -File `"$scriptPath`""
    if ($Silent) { $argList += " -Silent" }
    if ($PurgeUserData) { $argList += " -PurgeUserData" }
    if (-not [string]::IsNullOrWhiteSpace($InstallDir)) { $argList += " -InstallDir `"$InstallDir`"" }

    try {
        Start-Process powershell.exe -ArgumentList $argList -Verb RunAs
        exit
    } catch {
        Write-Error "Administrator privileges are required to uninstall FopherSync and remove Windows scheduled tasks."
        exit 1
    }
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

# Determine installation directory if not explicitly provided
if ([string]::IsNullOrWhiteSpace($InstallDir)) {
    $possibleInstallDirs = @(
        (Join-Path $env:ProgramFiles "FopherSync"),
        (Join-Path ${env:ProgramFiles(x86)} "FopherSync")
    )
    foreach ($dir in $possibleInstallDirs) {
        if (Test-Path $dir) {
            $InstallDir = $dir
            break
        }
    }
}

# Function to execute uninstallation actions
function Invoke-FopherSyncUninstall([bool]$removeUserData) {
    $log = New-Object System.Collections.Generic.List[string]

    # 1. Terminate running FopherSync processes
    try {
        $processes = Get-Process -Name "FopherSync" -ErrorAction SilentlyContinue
        if ($processes) {
            foreach ($p in $processes) {
                try {
                    $p.Kill()
                    $p.WaitForExit(3000)
                    $log.Add("Stopped running FopherSync process (PID $($p.Id)).")
                } catch { }
            }
        }
    } catch { }

    # 2. Remove Windows Task Scheduler Tasks
    $tasksToDelete = @(
        "FopherSync",
        "FopherSync Backup",
        "RoboCopyPlus",
        "RoboCopyPlus Backup"
    )
    foreach ($taskName in $tasksToDelete) {
        try {
            $proc = Start-Process -FilePath "schtasks.exe" -ArgumentList "/Delete /TN `"$taskName`" /F" -WindowStyle Hidden -PassThru -Wait
            if ($proc.ExitCode -eq 0) {
                $log.Add("Removed scheduled task '$taskName' from Windows Task Scheduler.")
            }
        } catch { }
    }

    # Clean any legacy wildcard tasks (RoboCopyPlus_*)
    try {
        $p = Start-Process -FilePath "schtasks.exe" -ArgumentList "/Query /FO CSV /NH" -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput "$env:TEMP\schtasks_query.tmp"
        if (Test-Path "$env:TEMP\schtasks_query.tmp") {
            $lines = Get-Content "$env:TEMP\schtasks_query.tmp"
            Remove-Item "$env:TEMP\schtasks_query.tmp" -Force -ErrorAction SilentlyContinue
            foreach ($line in $lines) {
                $parts = $line.Split(',')
                if ($parts.Length -gt 0) {
                    $tName = $parts[0].Trim('"', '\', ' ')
                    if ($tName.StartsWith("RoboCopyPlus_") -or $tName.StartsWith("FopherSync_")) {
                        Start-Process -FilePath "schtasks.exe" -ArgumentList "/Delete /TN `"$tName`" /F" -WindowStyle Hidden -PassThru -Wait
                        $log.Add("Removed scheduled task '$tName'.")
                    }
                }
            }
        }
    } catch { }

    # 3. Remove Desktop Shortcuts
    $desktopFolders = @(
        [Environment]::GetFolderPath("Desktop"),
        [Environment]::GetFolderPath("CommonDesktopDirectory")
    )
    $desktopShortcuts = @("FopherSync.lnk", "Automatic Backup Sentry.lnk")
    foreach ($folder in $desktopFolders) {
        if (-not (Test-Path $folder)) { continue }
        foreach ($shortcut in $desktopShortcuts) {
            $path = Join-Path $folder $shortcut
            if (Test-Path $path) {
                Remove-Item -Path $path -Force -ErrorAction SilentlyContinue
                $log.Add("Removed Desktop shortcut: $path")
            }
        }
    }

    # 4. Remove Start Menu Folders and Shortcuts
    $startMenuFolders = @(
        (Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\FopherSync"),
        (Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\FopherSync")
    )
    foreach ($smDir in $startMenuFolders) {
        if (Test-Path $smDir) {
            Remove-Item -Path $smDir -Recurse -Force -ErrorAction SilentlyContinue
            $log.Add("Removed Start Menu directory: $smDir")
        }
    }

    # 5. Remove Startup Shortcuts and Run Registry Keys
    $startupDirs = @(
        [Environment]::GetFolderPath("Startup"),
        [Environment]::GetFolderPath("CommonStartup")
    )
    foreach ($sDir in $startupDirs) {
        if (-not (Test-Path $sDir)) { continue }
        $sLnk = Join-Path $sDir "FopherSync.lnk"
        if (Test-Path $sLnk) {
            Remove-Item -Path $sLnk -Force -ErrorAction SilentlyContinue
            $log.Add("Removed Startup shortcut: $sLnk")
        }
    }

    $runKeyPaths = @(
        "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run",
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
    )
    foreach ($regKey in $runKeyPaths) {
        try {
            if (Get-ItemProperty -Path $regKey -Name "FopherSync" -ErrorAction SilentlyContinue) {
                Remove-ItemProperty -Path $regKey -Name "FopherSync" -Force -ErrorAction SilentlyContinue
                $log.Add("Removed Run key from registry: $regKey\FopherSync")
            }
        } catch { }
    }

    # 6. Remove Program Uninstall Registry Keys
    $uninstallKeyPaths = @(
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\FopherSync",
        "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\FopherSync",
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{9F82B931-41F5-40CE-A69D-3EB419409841}_is1",
        "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{9F82B931-41F5-40CE-A69D-3EB419409841}_is1"
    )
    foreach ($uKey in $uninstallKeyPaths) {
        if (Test-Path $uKey) {
            Remove-Item -Path $uKey -Recurse -Force -ErrorAction SilentlyContinue
            $log.Add("Removed Windows Uninstall registration: $uKey")
        }
    }

    # 7. Remove User Data, Databases, Settings & Logs if requested
    if ($removeUserData) {
        $userDataDirs = @(
            (Join-Path $env:LOCALAPPDATA "FopherSync"),
            (Join-Path $env:LOCALAPPDATA "RoboCopyPlus"),
            (Join-Path $env:APPDATA "FopherSync")
        )
        foreach ($uDir in $userDataDirs) {
            if (Test-Path $uDir) {
                Remove-Item -Path $uDir -Recurse -Force -ErrorAction SilentlyContinue
                $log.Add("Purged user data directory: $uDir")
            }
        }
    }

    # 8. Remove Installation Directory (e.g., C:\Program Files\FopherSync)
    if (-not [string]::IsNullOrWhiteSpace($InstallDir) -and (Test-Path $InstallDir)) {
        # Check that it's safe to delete (must contain FopherSync in path)
        if ($InstallDir -match "FopherSync") {
            try {
                Remove-Item -Path $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
                $log.Add("Removed installation directory: $InstallDir")
            } catch {
                $log.Add("Note: Could not completely delete $InstallDir: $($_.Exception.Message)")
            }
        }
    }

    # 9. Refresh Windows Explorer Icon Cache
    try {
        Add-Type -MemberDefinition '[DllImport("shell32.dll")] public static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);' -Name Win32Shell -Namespace Win32 -ErrorAction SilentlyContinue
        [Win32.Win32Shell]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)
    } catch { }

    return $log
}

# If running in silent mode
if ($Silent) {
    $results = Invoke-FopherSyncUninstall ($PurgeUserData.IsPresent -or $PurgeUserData)
    foreach ($r in $results) { Write-Host $r }
    exit 0
}

# ----------------- GUI UNINSTALL WIZARD -----------------

$form = New-Object System.Windows.Forms.Form
$form.Text = "FopherSync Uninstaller"
$form.Size = New-Object System.Drawing.Size(520, 390)
$form.StartPosition = "CenterScreen"
$form.FormBorderStyle = "FixedDialog"
$form.MaximizeBox = $false
$form.MinimizeBox = $true
$form.BackColor = [System.Drawing.SystemColors]::Control

# Header Panel
$headerPanel = New-Object System.Windows.Forms.Panel
$headerPanel.Dock = [System.Windows.Forms.DockStyle]::Top
$headerPanel.Height = 65
$headerPanel.BackColor = [System.Drawing.Color]::White
$form.Controls.Add($headerPanel)

$lblHeaderTitle = New-Object System.Windows.Forms.Label
$lblHeaderTitle.Text = "Uninstall FopherSync"
$lblHeaderTitle.Font = New-Object System.Drawing.Font("Segoe UI", 11, [System.Drawing.FontStyle]::Bold)
$lblHeaderTitle.Location = New-Object System.Drawing.Point(20, 12)
$lblHeaderTitle.Size = New-Object System.Drawing.Size(460, 22)
$headerPanel.Controls.Add($lblHeaderTitle)

$lblHeaderSub = New-Object System.Windows.Forms.Label
$lblHeaderSub.Text = "Remove FopherSync application, background tasks, shortcuts, and data."
$lblHeaderSub.Font = New-Object System.Drawing.Font("Segoe UI", 8.5)
$lblHeaderSub.ForeColor = [System.Drawing.Color]::FromArgb(70, 70, 70)
$lblHeaderSub.Location = New-Object System.Drawing.Point(34, 36)
$lblHeaderSub.Size = New-Object System.Drawing.Size(450, 20)
$headerPanel.Controls.Add($lblHeaderSub)

$headerSep = New-Object System.Windows.Forms.Label
$headerSep.Dock = [System.Windows.Forms.DockStyle]::Bottom
$headerSep.Height = 2
$headerSep.BorderStyle = "Fixed3D"
$headerPanel.Controls.Add($headerSep)

# Bottom Panel
$bottomPanel = New-Object System.Windows.Forms.Panel
$bottomPanel.Dock = [System.Windows.Forms.DockStyle]::Bottom
$bottomPanel.Height = 50
$form.Controls.Add($bottomPanel)

$bottomSep = New-Object System.Windows.Forms.Label
$bottomSep.Location = New-Object System.Drawing.Point(0, 0)
$bottomSep.Size = New-Object System.Drawing.Size(520, 2)
$bottomSep.BorderStyle = "Fixed3D"
$bottomPanel.Controls.Add($bottomSep)

$btnCancel = New-Object System.Windows.Forms.Button
$btnCancel.Text = "Cancel"
$btnCancel.Size = New-Object System.Drawing.Size(85, 26)
$btnCancel.Location = New-Object System.Drawing.Point(405, 12)
$bottomPanel.Controls.Add($btnCancel)

$btnUninstall = New-Object System.Windows.Forms.Button
$btnUninstall.Text = "Uninstall"
$btnUninstall.Size = New-Object System.Drawing.Size(85, 26)
$btnUninstall.Location = New-Object System.Drawing.Point(312, 12)
$bottomPanel.Controls.Add($btnUninstall)

# Content Panel
$contentPanel = New-Object System.Windows.Forms.Panel
$contentPanel.Dock = [System.Windows.Forms.DockStyle]::Fill
$contentPanel.Padding = New-Object System.Windows.Forms.Padding(25, 20, 25, 10)
$form.Controls.Add($contentPanel)

$lblPrompt = New-Object System.Windows.Forms.Label
$lblPrompt.Text = "Are you sure you want to completely uninstall FopherSync from your computer?`n`nThis will stop any running instances, unregister scheduled backup tasks from Windows Task Scheduler, remove desktop and Start menu shortcuts, and delete installed program files."
$lblPrompt.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$lblPrompt.Location = New-Object System.Drawing.Point(25, 80)
$lblPrompt.Size = New-Object System.Drawing.Size(460, 75)
$contentPanel.Controls.Add($lblPrompt)

$grpOptions = New-Object System.Windows.Forms.GroupBox
$grpOptions.Text = "Cleanup Options"
$grpOptions.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
$grpOptions.Location = New-Object System.Drawing.Point(25, 165)
$grpOptions.Size = New-Object System.Drawing.Size(460, 110)
$contentPanel.Controls.Add($grpOptions)

$chkPurgeData = New-Object System.Windows.Forms.CheckBox
$chkPurgeData.Text = "Also delete backup profiles, history database, and logs (recommended for clean removal)"
$chkPurgeData.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Regular)
$chkPurgeData.Checked = $true
$chkPurgeData.Location = New-Object System.Drawing.Point(15, 25)
$chkPurgeData.Size = New-Object System.Drawing.Size(430, 36)
$grpOptions.Controls.Add($chkPurgeData)

$lblDataNotice = New-Object System.Windows.Forms.Label
$lblDataNotice.Text = "Removes %LOCALAPPDATA%\FopherSync (jobs.json, settings.json, history.db, logs). Uncheck this if you plan to reinstall and want to keep your configured backup jobs."
$lblDataNotice.Font = New-Object System.Drawing.Font("Segoe UI", 8)
$lblDataNotice.ForeColor = [System.Drawing.Color]::FromArgb(90, 90, 90)
$lblDataNotice.Location = New-Object System.Drawing.Point(34, 62)
$lblDataNotice.Size = New-Object System.Drawing.Size(410, 38)
$grpOptions.Controls.Add($lblDataNotice)

# Event Handlers
$btnCancel.Add_Click({
    $form.Close()
})

$btnUninstall.Add_Click({
    $btnUninstall.Enabled = $false
    $btnCancel.Enabled = $false
    $form.Cursor = [System.Windows.Forms.Cursors]::WaitCursor

    $doPurge = $chkPurgeData.Checked
    $actions = Invoke-FopherSyncUninstall $doPurge

    $form.Cursor = [System.Windows.Forms.Cursors]::Default

    $summaryMsg = "FopherSync has been successfully uninstalled from your computer.`n`nActions performed:`n"
    foreach ($a in $actions) {
        $summaryMsg += "• $a`n"
    }

    [System.Windows.Forms.MessageBox]::Show(
        $summaryMsg,
        "FopherSync Uninstalled",
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Information
    )

    $form.Close()
})

[void]$form.ShowDialog()
