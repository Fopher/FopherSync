# FopherSync Classic Setup Wizard
# A standalone, zero-dependency Windows Forms installation wizard.
# Runs directly on Windows 10/11 without requiring Inno Setup compiler.

param(
    [string]$SourceDir = ""
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

[System.Windows.Forms.Application]::EnableVisualStyles()

# Determine root and source paths
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$projectRoot = Split-Path -Parent $scriptDir

if ([string]::IsNullOrWhiteSpace($SourceDir)) {
    $possibleDirs = @(
        (Join-Path $projectRoot "src\FopherSync.Wpf\bin\Release\net8.0-windows\win-x64\publish"),
        (Join-Path $projectRoot "src\FopherSync.Wpf\bin\Debug\net8.0-windows\win-x64\publish"),
        (Join-Path $projectRoot "src\FopherSync.Wpf\bin\Release\net8.0-windows\publish"),
        (Join-Path $scriptDir "app")
    )
    foreach ($dir in $possibleDirs) {
        if (Test-Path $dir) {
            $SourceDir = $dir
            break
        }
    }
}

# License file locations
$eulaPath = Join-Path $scriptDir "eula.txt"
$licensePath = Join-Path $scriptDir "license.txt"
$agreementPath = Join-Path $scriptDir "agreement.txt"

$agreementText = ""
if (Test-Path $agreementPath) {
    $agreementText = [System.IO.File]::ReadAllText($agreementPath)
} elseif (Test-Path $eulaPath) {
    $agreementText = [System.IO.File]::ReadAllText($eulaPath)
} else {
    $agreementText = "FopherSync End User License Agreement and MIT License."
}

# Wizard Form Setup
$form = New-Object System.Windows.Forms.Form
$form.Text = "FopherSync Setup"
$form.Size = New-Object System.Drawing.Size(520, 420)
$form.StartPosition = "CenterScreen"
$form.FormBorderStyle = "FixedDialog"
$form.MaximizeBox = $false
$form.MinimizeBox = $true
$form.BackColor = [System.Drawing.SystemColors]::Control

# Set App Icon if present
$iconPath = Join-Path $projectRoot "src\FopherSync.Wpf\Assets\app.ico"
if (Test-Path $iconPath) {
    try {
        $form.Icon = New-Object System.Drawing.Icon($iconPath)
    } catch {}
}

# Bottom panel for buttons
$bottomPanel = New-Object System.Windows.Forms.Panel
$bottomPanel.Dock = [System.Windows.Forms.DockStyle]::Bottom
$bottomPanel.Height = 52
$bottomPanel.BackColor = [System.Drawing.SystemColors]::Control
$form.Controls.Add($bottomPanel)

# Bevel separator above bottom panel
$bottomSeparator = New-Object System.Windows.Forms.Label
$bottomSeparator.Location = New-Object System.Drawing.Point(0, 0)
$bottomSeparator.Size = New-Object System.Drawing.Size(520, 2)
$bottomSeparator.BorderStyle = "Fixed3D"
$bottomPanel.Controls.Add($bottomSeparator)

# Buttons
$btnCancel = New-Object System.Windows.Forms.Button
$btnCancel.Text = "Cancel"
$btnCancel.Size = New-Object System.Drawing.Size(80, 26)
$btnCancel.Location = New-Object System.Drawing.Point(412, 14)
$bottomPanel.Controls.Add($btnCancel)

$btnNext = New-Object System.Windows.Forms.Button
$btnNext.Text = "Next >"
$btnNext.Size = New-Object System.Drawing.Size(80, 26)
$btnNext.Location = New-Object System.Drawing.Point(324, 14)
$bottomPanel.Controls.Add($btnNext)

$btnBack = New-Object System.Windows.Forms.Button
$btnBack.Text = "< Back"
$btnBack.Size = New-Object System.Drawing.Size(80, 26)
$btnBack.Location = New-Object System.Drawing.Point(236, 14)
$btnBack.Enabled = $false
$bottomPanel.Controls.Add($btnBack)

# Main Content Container
$contentPanel = New-Object System.Windows.Forms.Panel
$contentPanel.Dock = [System.Windows.Forms.DockStyle]::Fill
$form.Controls.Add($contentPanel)

# Helper function to create top banner for pages
function Create-PageBanner($title, $subtitle) {
    $banner = New-Object System.Windows.Forms.Panel
    $banner.Dock = [System.Windows.Forms.DockStyle]::Top
    $banner.Height = 60
    $banner.BackColor = [System.Drawing.Color]::White

    $lblTitle = New-Object System.Windows.Forms.Label
    $lblTitle.Text = $title
    $lblTitle.Font = New-Object System.Drawing.Font("Segoe UI", 9.5, [System.Drawing.FontStyle]::Bold)
    $lblTitle.Location = New-Object System.Drawing.Point(20, 10)
    $lblTitle.Size = New-Object System.Drawing.Size(460, 20)
    $banner.Controls.Add($lblTitle)

    $lblSub = New-Object System.Windows.Forms.Label
    $lblSub.Text = $subtitle
    $lblSub.Font = New-Object System.Drawing.Font("Segoe UI", 8.5)
    $lblSub.Location = New-Object System.Drawing.Point(36, 32)
    $lblSub.Size = New-Object System.Drawing.Size(444, 20)
    $lblSub.ForeColor = [System.Drawing.Color]::FromArgb(60, 60, 60)
    $banner.Controls.Add($lblSub)

    $bannerSep = New-Object System.Windows.Forms.Label
    $bannerSep.Dock = [System.Windows.Forms.DockStyle]::Bottom
    $bannerSep.Height = 2
    $bannerSep.BorderStyle = "Fixed3D"
    $banner.Controls.Add($bannerSep)

    return $banner
}

# ---------------- PAGE 1: Welcome ----------------
$pageWelcome = New-Object System.Windows.Forms.Panel
$pageWelcome.Dock = [System.Windows.Forms.DockStyle]::Fill

$lblWelcomeTitle = New-Object System.Windows.Forms.Label
$lblWelcomeTitle.Text = "Welcome to the FopherSync Setup Wizard"
$lblWelcomeTitle.Font = New-Object System.Drawing.Font("Segoe UI", 12, [System.Drawing.FontStyle]::Bold)
$lblWelcomeTitle.Location = New-Object System.Drawing.Point(30, 30)
$lblWelcomeTitle.Size = New-Object System.Drawing.Size(450, 30)
$pageWelcome.Controls.Add($lblWelcomeTitle)

$lblWelcomeBody = New-Object System.Windows.Forms.Label
$lblWelcomeBody.Text = "This will install FopherSync 1.0.0 on your computer.`n`nFopherSync is a fast, reliable Windows backup utility with multi-channel alerting, scheduled catch-up execution, and comprehensive safety rails.`n`nIt includes the complete End User License Agreement (EULA) and Open Source License terms in the installation directory.`n`nClick Next to continue, or Cancel to exit Setup."
$lblWelcomeBody.Font = New-Object System.Drawing.Font("Segoe UI", 9.5)
$lblWelcomeBody.Location = New-Object System.Drawing.Point(30, 80)
$lblWelcomeBody.Size = New-Object System.Drawing.Size(450, 180)
$pageWelcome.Controls.Add($lblWelcomeBody)

# ---------------- PAGE 2: License Agreement / EULA ----------------
$pageLicense = New-Object System.Windows.Forms.Panel
$pageLicense.Dock = [System.Windows.Forms.DockStyle]::Fill

$licBanner = Create-PageBanner "License Agreement" "Please review the license terms before installing FopherSync."
$pageLicense.Controls.Add($licBanner)

$txtLicense = New-Object System.Windows.Forms.TextBox
$txtLicense.Multiline = $true
$txtLicense.ScrollBars = "Vertical"
$txtLicense.ReadOnly = $true
$txtLicense.Font = New-Object System.Drawing.Font("Consolas", 8.5)
$txtLicense.BackColor = [System.Drawing.Color]::White
$txtLicense.Location = New-Object System.Drawing.Point(24, 70)
$txtLicense.Size = New-Object System.Drawing.Size(460, 180)
$txtLicense.Text = $agreementText
$pageLicense.Controls.Add($txtLicense)

$rbAccept = New-Object System.Windows.Forms.RadioButton
$rbAccept.Text = "I accept the agreement"
$rbAccept.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$rbAccept.Location = New-Object System.Drawing.Point(26, 258)
$rbAccept.Size = New-Object System.Drawing.Size(300, 22)
$pageLicense.Controls.Add($rbAccept)

$rbDecline = New-Object System.Windows.Forms.RadioButton
$rbDecline.Text = "I do not accept the agreement"
$rbDecline.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$rbDecline.Checked = $true
$rbDecline.Location = New-Object System.Drawing.Point(26, 282)
$rbDecline.Size = New-Object System.Drawing.Size(300, 22)
$pageLicense.Controls.Add($rbDecline)

# ---------------- PAGE 3: Select Destination Location ----------------
$pageDir = New-Object System.Windows.Forms.Panel
$pageDir.Dock = [System.Windows.Forms.DockStyle]::Fill

$dirBanner = Create-PageBanner "Select Destination Location" "Where should FopherSync be installed?"
$pageDir.Controls.Add($dirBanner)

$lblDirPrompt = New-Object System.Windows.Forms.Label
$lblDirPrompt.Text = "Setup will install FopherSync into the following folder:`n`nTo continue, click Next. If you would like to select a different folder, click Browse."
$lblDirPrompt.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$lblDirPrompt.Location = New-Object System.Drawing.Point(24, 75)
$lblDirPrompt.Size = New-Object System.Drawing.Size(460, 50)
$pageDir.Controls.Add($lblDirPrompt)

$defaultInstallPath = Join-Path $env:LOCALAPPDATA "Programs\FopherSync"
$txtInstallDir = New-Object System.Windows.Forms.TextBox
$txtInstallDir.Location = New-Object System.Drawing.Point(24, 140)
$txtInstallDir.Size = New-Object System.Drawing.Size(360, 24)
$txtInstallDir.Text = $defaultInstallPath
$pageDir.Controls.Add($txtInstallDir)

$btnBrowse = New-Object System.Windows.Forms.Button
$btnBrowse.Text = "Browse..."
$btnBrowse.Size = New-Object System.Drawing.Size(85, 24)
$btnBrowse.Location = New-Object System.Drawing.Point(395, 139)
$btnBrowse.Add_Click({
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    $dlg.Description = "Select destination folder for FopherSync"
    $dlg.SelectedPath = $txtInstallDir.Text
    if ($dlg.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
        $txtInstallDir.Text = $dlg.SelectedPath
    }
})
$pageDir.Controls.Add($btnBrowse)

# ---------------- PAGE 4: Select Additional Tasks ----------------
$pageTasks = New-Object System.Windows.Forms.Panel
$pageTasks.Dock = [System.Windows.Forms.DockStyle]::Fill

$taskBanner = Create-PageBanner "Select Additional Tasks" "Which additional shortcuts should be created?"
$pageTasks.Controls.Add($taskBanner)

$lblTaskPrompt = New-Object System.Windows.Forms.Label
$lblTaskPrompt.Text = "Select the additional tasks you would like Setup to perform while installing FopherSync:"
$lblTaskPrompt.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$lblTaskPrompt.Location = New-Object System.Drawing.Point(24, 75)
$lblTaskPrompt.Size = New-Object System.Drawing.Size(460, 30)
$pageTasks.Controls.Add($lblTaskPrompt)

$chkDesktop = New-Object System.Windows.Forms.CheckBox
$chkDesktop.Text = "Create a desktop shortcut"
$chkDesktop.Checked = $true
$chkDesktop.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$chkDesktop.Location = New-Object System.Drawing.Point(40, 115)
$chkDesktop.Size = New-Object System.Drawing.Size(350, 24)
$pageTasks.Controls.Add($chkDesktop)

$chkStartup = New-Object System.Windows.Forms.CheckBox
$chkStartup.Text = "Start FopherSync automatically when Windows starts"
$chkStartup.Checked = $false
$chkStartup.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$chkStartup.Location = New-Object System.Drawing.Point(40, 145)
$chkStartup.Size = New-Object System.Drawing.Size(350, 24)
$pageTasks.Controls.Add($chkStartup)

# ---------------- PAGE 5: Ready to Install ----------------
$pageReady = New-Object System.Windows.Forms.Panel
$pageReady.Dock = [System.Windows.Forms.DockStyle]::Fill

$readyBanner = Create-PageBanner "Ready to Install" "Setup is now ready to begin installing FopherSync on your computer."
$pageReady.Controls.Add($readyBanner)

$txtReadySummary = New-Object System.Windows.Forms.TextBox
$txtReadySummary.Multiline = $true
$txtReadySummary.ReadOnly = $true
$txtReadySummary.BackColor = [System.Drawing.SystemColors]::Control
$txtReadySummary.BorderStyle = "None"
$txtReadySummary.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$txtReadySummary.Location = New-Object System.Drawing.Point(24, 75)
$txtReadySummary.Size = New-Object System.Drawing.Size(460, 220)
$pageReady.Controls.Add($txtReadySummary)

# ---------------- PAGE 6: Installing Progress ----------------
$pageInstalling = New-Object System.Windows.Forms.Panel
$pageInstalling.Dock = [System.Windows.Forms.DockStyle]::Fill

$installBanner = Create-PageBanner "Installing" "Please wait while Setup installs FopherSync on your computer."
$pageInstalling.Controls.Add($installBanner)

$lblStatus = New-Object System.Windows.Forms.Label
$lblStatus.Text = "Extracting files..."
$lblStatus.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$lblStatus.Location = New-Object System.Drawing.Point(24, 100)
$lblStatus.Size = New-Object System.Drawing.Size(460, 24)
$pageInstalling.Controls.Add($lblStatus)

$progressBar = New-Object System.Windows.Forms.ProgressBar
$progressBar.Location = New-Object System.Drawing.Point(24, 130)
$progressBar.Size = New-Object System.Drawing.Size(460, 22)
$progressBar.Style = "Continuous"
$pageInstalling.Controls.Add($progressBar)

# ---------------- PAGE 7: Finish ----------------
$pageFinish = New-Object System.Windows.Forms.Panel
$pageFinish.Dock = [System.Windows.Forms.DockStyle]::Fill

$lblFinishTitle = New-Object System.Windows.Forms.Label
$lblFinishTitle.Text = "Completing the FopherSync Setup Wizard"
$lblFinishTitle.Font = New-Object System.Drawing.Font("Segoe UI", 12, [System.Drawing.FontStyle]::Bold)
$lblFinishTitle.Location = New-Object System.Drawing.Point(30, 30)
$lblFinishTitle.Size = New-Object System.Drawing.Size(450, 30)
$pageFinish.Controls.Add($lblFinishTitle)

$lblFinishBody = New-Object System.Windows.Forms.Label
$lblFinishBody.Text = "Setup has finished installing FopherSync on your computer.`n`nThe EULA (EULA.txt) and License Agreement (LICENSE.txt) have been included directly in your installation folder for future reference.`n`nClick Finish to exit Setup."
$lblFinishBody.Font = New-Object System.Drawing.Font("Segoe UI", 9.5)
$lblFinishBody.Location = New-Object System.Drawing.Point(30, 80)
$lblFinishBody.Size = New-Object System.Drawing.Size(450, 120)
$pageFinish.Controls.Add($lblFinishBody)

$chkLaunchApp = New-Object System.Windows.Forms.CheckBox
$chkLaunchApp.Text = "Launch FopherSync now"
$chkLaunchApp.Checked = $true
$chkLaunchApp.Font = New-Object System.Drawing.Font("Segoe UI", 9.5)
$chkLaunchApp.Location = New-Object System.Drawing.Point(30, 220)
$chkLaunchApp.Size = New-Object System.Drawing.Size(300, 26)
$pageFinish.Controls.Add($chkLaunchApp)

# Pages array and state
$pages = @($pageWelcome, $pageLicense, $pageDir, $pageTasks, $pageReady, $pageInstalling, $pageFinish)
$currentPage = 0

function Show-Page($pageIndex) {
    for ($i = 0; $i -lt $pages.Length; $i++) {
        $pages[$i].Visible = ($i -eq $pageIndex)
    }

    $script:currentPage = $pageIndex
    $btnBack.Enabled = ($pageIndex -gt 0 -and $pageIndex -lt 5)

    if ($pageIndex -eq 0) {
        $btnNext.Text = "Next >"
        $btnNext.Enabled = $true
    } elseif ($pageIndex -eq 1) {
        $btnNext.Text = "Next >"
        $btnNext.Enabled = $rbAccept.Checked
    } elseif ($pageIndex -eq 4) {
        $btnNext.Text = "Install"
        $btnNext.Enabled = $true
        # Populate summary
        $txtReadySummary.Text = "Destination location:`r`n   $($txtInstallDir.Text)`r`n`r`nShortcuts:`r`n   " + `
            $(if ($chkDesktop.Checked) { "• Desktop shortcut`r`n   " } else { "" }) + `
            "• Start Menu shortcut`r`n   " + `
            $(if ($chkStartup.Checked) { "• Windows Startup shortcut`r`n   " } else { "" }) + `
            "`r`nIncluded Documentation:`r`n   • EULA.txt (End User License Agreement)`r`n   • LICENSE.txt (MIT License)`r`n   • Agreement.txt"
    } elseif ($pageIndex -eq 5) {
        $btnNext.Enabled = $false
        $btnBack.Enabled = $false
        $btnCancel.Enabled = $false
    } elseif ($pageIndex -eq 6) {
        $btnNext.Text = "Finish"
        $btnNext.Enabled = $true
        $btnBack.Enabled = $false
        $btnCancel.Enabled = $false
    }
}

# License radio buttons change handler
$rbAccept.Add_CheckedChanged({
    if ($currentPage -eq 1) {
        $btnNext.Enabled = $rbAccept.Checked
    }
})

# Add all pages to container
foreach ($p in $pages) {
    $p.Visible = $false
    $contentPanel.Controls.Add($p)
}

# Start at Page 0
Show-Page 0

# Back button handler
$btnBack.Add_Click({
    if ($currentPage -gt 0) {
        Show-Page ($currentPage - 1)
    }
})

# Cancel button handler
$btnCancel.Add_Click({
    $res = [System.Windows.Forms.MessageBox]::Show(
        "Are you sure you want to exit FopherSync Setup?",
        "Exit Setup",
        [System.Windows.Forms.MessageBoxButtons]::YesNo,
        [System.Windows.Forms.MessageBoxIcon]::Question
    )
    if ($res -eq [System.Windows.Forms.DialogResult]::Yes) {
        $form.Close()
    }
})

# Next / Install / Finish button handler
$btnNext.Add_Click({
    if ($currentPage -eq 6) {
        # Finish
        $targetExe = Join-Path $txtInstallDir.Text "FopherSync.exe"
        if ($chkLaunchApp.Checked -and (Test-Path $targetExe)) {
            Start-Process -FilePath $targetExe
        }
        $form.Close()
        return
    }

    if ($currentPage -eq 4) {
        # Transition to Installing
        Show-Page 5
        $form.Refresh()

        # Execute File Copy & Shortcut Creation
        $targetDir = $txtInstallDir.Text
        try {
            if (!(Test-Path $targetDir)) {
                New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
            }

            # Check for source binaries
            if (Test-Path $SourceDir) {
                $files = Get-ChildItem -Path $SourceDir -Recurse -File
                $total = [Math]::Max(1, $files.Count)
                $count = 0

                foreach ($f in $files) {
                    $rel = $f.FullName.Substring($SourceDir.Length).TrimStart("\", "/")
                    $destPath = Join-Path $targetDir $rel
                    $destFolder = Split-Path -Parent $destPath
                    if (!(Test-Path $destFolder)) {
                        New-Item -ItemType Directory -Path $destFolder -Force | Out-Null
                    }
                    Copy-Item $f.FullName -Destination $destPath -Force
                    $count++
                    $progressBar.Value = [Math]::Min(80, [int](($count / $total) * 80))
                    $lblStatus.Text = "Copying $($f.Name)..."
                    $form.Refresh()
                }
            } else {
                $lblStatus.Text = "Creating installation directory..."
                $form.Refresh()
            }

            # Copy EULA and License files
            $lblStatus.Text = "Installing EULA and license documentation..."
            $form.Refresh()
            if (Test-Path $eulaPath) {
                Copy-Item $eulaPath -Destination (Join-Path $targetDir "EULA.txt") -Force
            }
            if (Test-Path $licensePath) {
                Copy-Item $licensePath -Destination (Join-Path $targetDir "LICENSE.txt") -Force
            }
            if (Test-Path $agreementPath) {
                Copy-Item $agreementPath -Destination (Join-Path $targetDir "Agreement.txt") -Force
            }

            # Copy Assets (app.ico)
            $assetsDir = Join-Path $targetDir "Assets"
            if (!(Test-Path $assetsDir)) { New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null }
            if (Test-Path $iconPath) {
                Copy-Item $iconPath -Destination (Join-Path $assetsDir "app.ico") -Force
            }

            $progressBar.Value = 90
            $lblStatus.Text = "Creating shortcuts..."
            $form.Refresh()

            # Create Shortcuts
            $wsh = New-Object -ComObject WScript.Shell
            $exePath = Join-Path $targetDir "FopherSync.exe"
            $installedIcon = Join-Path $assetsDir "app.ico"

            # 1. Start Menu Shortcuts
            $startMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\FopherSync"
            if (!(Test-Path $startMenuDir)) { New-Item -ItemType Directory -Path $startMenuDir -Force | Out-Null }

            $appShortcut = $wsh.CreateShortcut((Join-Path $startMenuDir "FopherSync.lnk"))
            $appShortcut.TargetPath = $exePath
            $appShortcut.WorkingDirectory = $targetDir
            $appShortcut.Description = "FopherSync Backup Orchestrator"
            if (Test-Path $installedIcon) { $appShortcut.IconLocation = "$installedIcon,0" }
            $appShortcut.Save()

            $eulaShortcut = $wsh.CreateShortcut((Join-Path $startMenuDir "View EULA & License.lnk"))
            $eulaShortcut.TargetPath = "$env:WINDIR\notepad.exe"
            $eulaShortcut.Arguments = "`"$targetDir\Agreement.txt`""
            $eulaShortcut.Description = "View FopherSync EULA and License Agreement"
            $eulaShortcut.Save()

            # 2. Desktop Shortcut
            if ($chkDesktop.Checked) {
                $desktopPath = [Environment]::GetFolderPath("Desktop")
                $deskShortcut = $wsh.CreateShortcut((Join-Path $desktopPath "FopherSync.lnk"))
                $deskShortcut.TargetPath = $exePath
                $deskShortcut.WorkingDirectory = $targetDir
                $deskShortcut.Description = "FopherSync Backup Orchestrator"
                if (Test-Path $installedIcon) { $deskShortcut.IconLocation = "$installedIcon,0" }
                $deskShortcut.Save()
            }

            # 3. Startup Shortcut
            if ($chkStartup.Checked) {
                $startupPath = [Environment]::GetFolderPath("Startup")
                $startShortcut = $wsh.CreateShortcut((Join-Path $startupPath "FopherSync.lnk"))
                $startShortcut.TargetPath = $exePath
                $startShortcut.WorkingDirectory = $targetDir
                $startShortcut.Description = "FopherSync (Auto-start)"
                if (Test-Path $installedIcon) { $startShortcut.IconLocation = "$installedIcon,0" }
                $startShortcut.Save()
            }

            $progressBar.Value = 100
            $lblStatus.Text = "Installation completed successfully."
            $form.Refresh()
            Start-Sleep -Milliseconds 400

            Show-Page 6
        } catch {
            [System.Windows.Forms.MessageBox]::Show(
                "An error occurred during installation:`r`n$($_.Exception.Message)",
                "Installation Error",
                [System.Windows.Forms.MessageBoxButtons]::OK,
                [System.Windows.Forms.MessageBoxIcon]::Error
            )
            Show-Page 4
        }
        return
    }

    # Standard next step
    if ($currentPage -lt 4) {
        Show-Page ($currentPage + 1)
    }
})

# Display dialog
[void]$form.ShowDialog()
