; FopherSync Inno Setup Script
; Classic, user-friendly installation wizard with EULA, license agreement, and customizable options.

#define MyAppName "FopherSync"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Fopher"
#define MyAppURL "https://github.com/fopher/fophersync"
#define MyAppExeName "FopherSync.exe"

[Setup]
; Unique application GUID
AppId={{9F82B931-41F5-40CE-A69D-3EB419409841}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; 64-bit application architecture
ArchitecturesInstallIn64BitMode=x64compatible

; Installation target directory defaults to Program Files (C:\Program Files\FopherSync)
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}

; Classic old-school wizard presentation
WizardStyle=classic
DisableWelcomePage=no
DisableDirPage=no
DisableProgramGroupPage=no
DisableReadyPage=no
DisableFinishedPage=no

; Clean shutdown and process termination
CloseApplications=force
CloseApplicationsFilter=*{#MyAppExeName}*

; License agreement presentation (requires user acceptance before proceeding)
LicenseFile=..\packaging\agreement.txt

; Output setup executable
OutputDir=..\dist
OutputBaseFilename=FopherSync_Setup_v1.0.0
Compression=lzma2/ultra64
SolidCompression=yes

; Require administrator privileges to install into Program Files
PrivilegesRequired=admin

; Visual assets & icons
SetupIconFile=..\src\FopherSync.Wpf\Assets\app.ico
UninstallDisplayIcon={app}\Assets\app.ico
ChangesAssociations=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startupicon"; Description: "Start FopherSync automatically when Windows starts"; GroupDescription: "Startup Options:"

[Files]
; Main application binaries and assets
Source: "..\src\FopherSync.Wpf\bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; EULA and License agreement files placed directly in the installation folder
Source: "..\packaging\eula.txt"; DestDir: "{app}"; DestName: "EULA.txt"; Flags: ignoreversion
Source: "..\packaging\license.txt"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\packaging\agreement.txt"; DestDir: "{app}"; DestName: "Agreement.txt"; Flags: ignoreversion

; Standalone Uninstaller scripts placed directly into the application folder
Source: "..\packaging\Uninstall-FopherSync.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\packaging\Uninstall.bat"; DestDir: "{app}"; Flags: ignoreversion

; Application icon
Source: "..\src\FopherSync.Wpf\Assets\app.ico"; DestDir: "{app}\Assets"; Flags: ignoreversion

[Icons]
; Start Menu shortcuts
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\app.ico"; Comment: "FopherSync Backup Orchestrator"
Name: "{group}\View EULA & License"; Filename: "{sys}\notepad.exe"; Parameters: """{app}\Agreement.txt"""; IconFilename: "{app}\Assets\app.ico"; Comment: "View End User License Agreement & License"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"; IconFilename: "{app}\Assets\app.ico"; Comment: "Uninstall FopherSync"

; Desktop shortcut
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\app.ico"; Comment: "{#MyAppName}"; Tasks: desktopicon

; Windows startup shortcut
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\app.ico"; Comment: "{#MyAppName}"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Gracefully or forcibly terminate any running FopherSync processes before file deletion
Filename: "taskkill.exe"; Parameters: "/F /IM {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillApp"
; Delete all FopherSync scheduled tasks from Windows Task Scheduler
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""FopherSync"" /F"; Flags: runhidden; RunOnceId: "DelTask1"
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""FopherSync Backup"" /F"; Flags: runhidden; RunOnceId: "DelTask2"
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""RoboCopyPlus"" /F"; Flags: runhidden; RunOnceId: "DelTask3"
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""RoboCopyPlus Backup"" /F"; Flags: runhidden; RunOnceId: "DelTask4"

[UninstallDelete]
; Delete all runtime generated files, logs, and assets in the installation folder
Type: filesandordirs; Name: "{app}"
; Clean up any shortcuts that might exist across user profiles
Type: files; Name: "{userstartup}\{#MyAppName}.lnk"
Type: files; Name: "{commonstartup}\{#MyAppName}.lnk"
Type: files; Name: "{autodesktop}\{#MyAppName}.lnk"
Type: files; Name: "{userdesktop}\{#MyAppName}.lnk"
Type: files; Name: "{commondesktop}\{#MyAppName}.lnk"
Type: files; Name: "{userdesktop}\Automatic Backup Sentry.lnk"
Type: files; Name: "{commondesktop}\Automatic Backup Sentry.lnk"
Type: filesandordirs; Name: "{userprograms}\{#MyAppName}"
Type: filesandordirs; Name: "{commonprograms}\{#MyAppName}"

[Code]
// Refresh Windows Explorer icon cache
procedure SHChangeNotify(wEventId: Integer; uFlags: Cardinal; dwItem1, dwItem2: Integer);
  external 'SHChangeNotify@shell32.dll stdcall';

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  AppDataDir, LegacyDir, AppDir: String;
  DelData: Integer;
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    // Terminate any running FopherSync instance
    Exec('taskkill.exe', '/F /IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    // Delete Windows Task Scheduler tasks
    Exec('schtasks.exe', '/Delete /TN "FopherSync" /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('schtasks.exe', '/Delete /TN "FopherSync Backup" /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('schtasks.exe', '/Delete /TN "RoboCopyPlus" /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('schtasks.exe', '/Delete /TN "RoboCopyPlus Backup" /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    // Prompt user to delete user configurations, history database, and logs
    DelData := MsgBox('Do you also want to remove all FopherSync backup job profiles, settings, history databases, and logs from this computer?' + #13#10#13#10 +
                      'Select "Yes" for a clean removal, or "No" to preserve your backup jobs for future installation.',
                      mbConfirmation, MB_YESNO);
    if DelData = IDYES then
    begin
      AppDataDir := ExpandConstant('{localappdata}\FopherSync');
      if DirExists(AppDataDir) then
        DelTree(AppDataDir, True, True, True);

      LegacyDir := ExpandConstant('{localappdata}\RoboCopyPlus');
      if DirExists(LegacyDir) then
        DelTree(LegacyDir, True, True, True);
    end;
  end;

  if CurUninstallStep = usPostUninstall then
  begin
    // Ensure the entire installation directory is completely removed
    AppDir := ExpandConstant('{app}');
    if DirExists(AppDir) then
      DelTree(AppDir, True, True, True);

    // Refresh Windows shell icon cache ($08000000 = SHCNE_ASSOCCHANGED)
    try
      SHChangeNotify($08000000, 0, 0, 0);
    except
    end;
  end;
end;
