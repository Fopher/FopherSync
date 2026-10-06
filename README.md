# FopherSync 🛡️

**FopherSync** is a modern, reliable Windows backup solution that pairs Microsoft's high-performance `robocopy.exe` with a sleek dark-mode desktop interface, silent background operation, sleep-wake catchup scheduling, and instant phone alerts.

---

## 🌟 Key Features

- **Built-in Safety Rails (Zero Accidental Deletions):**
  - **Safe Incremental by Default (`/E /XO`):** Never deletes a file from either source or destination.
  - **Empty Source Protection:** Aborts immediately if the source drive/folder is empty or disconnected before any mirror operation touches your destination.
  - **Root & Collision Protection:** Blocks running if source equals destination or targets drive roots (e.g. `C:\`).
  - **What-If Simulation Mode:** Runs RoboCopy with `/L` to preview exactly what would be copied without modifying any files.
  - **Double Confirmation:** Explicit prompts before running any destructive modes (`/MIR` or `/MOVE`).
- **Sleep & Wake-up Catchup:**
  - Integrates with native Windows Task Scheduler with `StartWhenAvailable = true`.
  - If a 2:00 AM backup is missed because your computer was asleep, it triggers automatically as soon as you wake it up.
- **Silent Background & System Tray:**
  - Closes or minimizes to the system tray, running invisibly in the background.
  - Silent headless CLI mode (`--run-job <id> --silent`) for zero-interruption scheduled execution.
- **Tri-Channel Alerting (Instant Phone Alerts):**
  - **ntfy.sh (Primary Push):** Free, private push notifications straight to your phone.
  - **Discord Webhook:** Formatted status cards sent directly to your server.
  - **Gmail / SMTP:** Full HTML backup reports delivered to your inbox.
- **SQLite & JSON Persistence:**
  - Complete historical log audit stored in local SQLite database.
  - Human-readable JSON profile and settings configuration.

---

## 🏗️ Solution Architecture

The solution contains four clean, decoupled projects:

1. **`FopherSync.Core`**:
   - `BackupJob.cs`, `BackupMode.cs`, `JobRunRecord.cs`, `AppSettings.cs`
   - `RoboCopyRunner.cs`: Output streaming process runner with exit-code bitmask analyzer.
   - `PathSafetyValidator.cs`: 7 safety rails protecting against file loss.
   - `JobHistoryDatabase.cs`: SQLite database manager.
   - `ConfigService.cs`: Thread-safe JSON configuration.
2. **`FopherSync.Notifications`**:
   - `NtfyNotifier.cs`: Free phone push notifications via ntfy.sh.
   - `DiscordNotifier.cs`: Rich Discord webhooks.
   - `SmtpNotifier.cs`: MailKit Gmail/SMTP HTML email reports.
   - `NotificationDispatcher.cs`: Multi-channel parallel broadcaster.
3. **`FopherSync.Scheduler`**:
   - `TaskSchedulerService.cs`: Native Windows Task Scheduler integration with wake-up catchup logic.
4. **`FopherSync.Wpf`**:
   - Modern dark UI (Gemini/Antigravity inspired).
   - System tray integration with right-click menu and minimize-to-tray.
   - Headless background execution routing.

---

## 🚀 Building & Running

### Prerequisites
- Windows 10 / 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 (Community edition or higher) or VS Code

### Building from Source

To build and run from the command line:

```powershell
# Clone or navigate to the directory
cd FopherSync

# Build the solution
dotnet build FopherSync.sln

# Run the WPF Desktop application
dotnet run --project src\FopherSync.Wpf\FopherSync.Wpf.csproj
```

Or open `FopherSync.sln` in **Visual Studio** and press **F5**.

### Creating & Running the Installer

FopherSync includes an easy-to-use, classic Windows setup wizard that bundles the **End User License Agreement (EULA.txt)** and **Open Source License (LICENSE.txt)** directly into the application folder and displays them during installation.

#### Option A: Build with Inno Setup (Recommended for Releases)
Run the build script:
```powershell
.\build.ps1
```
If [Inno Setup 6](https://jrsoftware.org/isdl.php) is installed, it will automatically compile `packaging\installer.iss` into `dist\FopherSync_Setup_v1.0.0.exe`.

#### Option B: Standalone Setup Wizard (Zero-Dependency)
You can also launch the classic setup wizard directly without compiling Inno Setup:
```powershell
.\packaging\Setup.bat
```
or in PowerShell:
```powershell
.\packaging\Setup-Wizard.ps1
```

The installer:
- Prompts for license & EULA acceptance before installation
- Lets users choose their installation directory (defaults to `C:\Program Files\FopherSync`)
- Installs the app binaries, icons, `EULA.txt`, and `LICENSE.txt` into the install folder
- Creates Start Menu & Desktop shortcuts, with an optional Windows Startup task
- Registers a Start Menu shortcut to view the EULA & License at any time


---

## 📱 Setting Up Phone Push Notifications (ntfy.sh)

1. Install the free **ntfy** app on your phone ([Google Play](https://play.google.com/store/apps/details?id=io.heckel.ntfy) or [F-Droid](https://f-droid.org/packages/io.heckel.ntfy/)).
2. Pick a unique, unguessable topic name in FopherSync (e.g. `backup-john-8291`).
3. In the ntfy app on your phone, tap **+** and subscribe to that same topic name.
4. Click **Send Test Push** in FopherSync Settings — your phone will chime instantly!
