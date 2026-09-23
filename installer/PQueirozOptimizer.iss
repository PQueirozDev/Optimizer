#define MyAppName "PQueiroz Optimizer"
#ifndef MyAppVersion
  #define MyAppVersion "1.2.0"
#endif
#ifndef MyAppPublisher
  #define MyAppPublisher "Pedro Queiroz"
#endif
#define MyAppExeName "PQueirozOptimizer.exe"
#define MyAppId "{{B1F0EA9A-8C70-4F92-9B72-9A5D1C3F0F11}"
#define PublishDir "..\artifacts\publish\win-x64"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://pqueiroz.vercel.app/
DefaultDirName={autopf}\PQueiroz Optimizer
DefaultGroupName={#MyAppName}
OutputDir=..\artifacts\installer
OutputBaseFilename=PQueirozOptimizer-Setup-v{#MyAppVersion}
SetupIconFile=..\PQueirozOptimizer\Assets\app.ico
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
DisableProgramGroupPage=no
DisableDirPage=no
AllowNoIcons=no
Compression=lzma2
SolidCompression=yes
SetupLogging=yes
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Installer
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; O aplicativo exige elevação. ShellExecute com o verbo "runas" solicita o UAC;
; CreateProcess direto falha com ERROR_ELEVATION_REQUIRED (740).
Filename: "{app}\{#MyAppExeName}"; Description: "Executar o aplicativo"; Verb: "runas"; Flags: nowait postinstall skipifsilent shellexec
; Atualização automática (/VERYSILENT): o instalador já está elevado, então reabre o
; aplicativo diretamente quando termina.
Filename: "{app}\{#MyAppExeName}"; Flags: nowait; Check: WizardSilent
