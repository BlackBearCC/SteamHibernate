; Inno Setup script for SteamHibernate.
; Build:  ISCC.exe /DStageDir=<folder-with-published-app+7za.exe+precomp.exe> installer\SteamHibernate.iss
; Produces: installer\Output\SteamHibernate-Setup-<version>.exe

#define MyAppName "SteamHibernate"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "BlackBearCC"
#define MyAppURL "https://github.com/BlackBearCC/SteamHibernate"
#define MyAppExeName "SteamHibernate.App.exe"

#ifndef StageDir
  #define StageDir "stage"
#endif

[Setup]
AppId={{B7E4B1C2-9D3A-4F5E-8A1B-2C3D4E5F6A7B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=Output
OutputBaseFilename=SteamHibernate-Setup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
; Self-contained published app + bundled 7za.exe + precomp.exe, all staged in StageDir.
Source: "{#StageDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
