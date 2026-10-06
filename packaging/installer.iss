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

; Installation target directory (per-user by default, clean and elevation-free)
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}

; Classic old-school wizard presentation
WizardStyle=classic
DisableWelcomePage=no
DisableDirPage=no
DisableProgramGroupPage=no
DisableReadyPage=no
DisableFinishedPage=no

; License agreement presentation (requires user acceptance before proceeding)
LicenseFile=..\packaging\agreement.txt

; Output setup executable
OutputDir=..\dist
OutputBaseFilename=FopherSync_Setup_v1.0.0
Compression=lzma2/ultra64
SolidCompression=yes

; User privileges (per-user setup allows zero-elevation installation)
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

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

; Application icon
Source: "..\src\FopherSync.Wpf\Assets\app.ico"; DestDir: "{app}\Assets"; Flags: ignoreversion

[Icons]
; Start Menu shortcuts
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\app.ico"; Comment: "FopherSync Backup Orchestrator"
Name: "{group}\View EULA & License"; Filename: "{sys}\notepad.exe"; Parameters: """{app}\Agreement.txt"""; IconFilename: "{app}\Assets\app.ico"; Comment: "View End User License Agreement & License"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"; IconFilename: "{app}\Assets\app.ico"

; Desktop shortcut
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\app.ico"; Comment: "{#MyAppName}"; Tasks: desktopicon

; Windows startup shortcut
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\app.ico"; Comment: "{#MyAppName}"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
