; Build with: powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Installer
; build.ps1 passes the app version and the folder that contains the built app.
#ifndef AppVersion
  #error Build the installer with build.ps1 -Installer.
#endif
#ifndef FileVersion
  #error Build the installer with build.ps1 -Installer.
#endif
#ifndef AppBinDir
  #error Build the installer with build.ps1 -Installer.
#endif
#if Ver < EncodeVer(6, 5, 2)
  #error Inno Setup 6.5.2 or later is required.
#endif

#define AppName "MD301 Plus"
#define AppExeName "MD301Plus.exe"
#define AppUrl "https://github.com/HugoMSFT/MD301_Plus"

[Setup]
; Never change AppId: Windows uses it to recognize upgrades and the uninstall entry.
AppId={{99A44711-DF5B-403A-91B7-72707F7C2119}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=HugoMSFT
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#FileVersion}
; Per-user install into %LOCALAPPDATA%\Programs: no administrator rights needed.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
MinVersion=10.0
; Matches the single-instance mutex in src\Program.cs, so files are never replaced while the app runs.
AppMutex=Local\MD301Plus
OutputBaseFilename=MD301Plus-Setup
SetupIconFile={#AppBinDir}\MD301Plus.ico
WizardSmallImageFile={#AppBinDir}\icon-preview.png
WizardStyle=modern
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/max
SolidCompression=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Start {#AppName} when I sign in to Windows"

[Files]
Source: "{#AppBinDir}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#AppBinDir}\{#AppExeName}.config"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Parameters: "--settings"; Comment: "Open {#AppName} settings"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#AppExeName}"""; Flags: uninsdeletevalue; Tasks: startup
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "{#AppName}"; Flags: deletevalue; Tasks: not startup

[Run]
Filename: "{app}\{#AppExeName}"; Parameters: "--settings"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeSetup: Boolean;
begin
  Result := IsDotNetInstalled(net48, 0);
  if not Result then
    SuppressibleMsgBox(FmtMessage(SetupMessage(msgWinVersionTooLowError), ['.NET Framework', '4.8']),
      mbCriticalError, MB_OK, IDOK);
end;
