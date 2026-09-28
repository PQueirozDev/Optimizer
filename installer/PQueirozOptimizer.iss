#define MyAppName "PQueiroz Optimizer"
#ifndef MyAppVersion
  #define MyAppVersion "1.4.0"
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
; O desinstalador remove os atalhos de Limpeza Rapida que o aplicativo cria no perfil do usuario
UsedUserAreasWarning=no

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

[UninstallDelete]
; Atalhos "Limpeza Rapida" criados pelo aplicativo (Ferramentas): sem isso apontariam para um executavel removido.
; O curinga evita depender da codificacao deste arquivo para o "a" acentuado do nome.
Type: files; Name: "{userdesktop}\Limpeza R*pida.lnk"
Type: files; Name: "{userprograms}\Limpeza R*pida.lnk"
; Atalho do seletor de modo de energia (Ferramentas)
Type: files; Name: "{userdesktop}\Modo de Energia.lnk"
; Instaladores de atualizacao baixados pelo aplicativo
Type: filesandordirs; Name: "{commonappdata}\PQueirozOptimizer\Updates"

[Run]
; O aplicativo exige elevação. ShellExecute com o verbo "runas" solicita o UAC;
; CreateProcess direto falha com ERROR_ELEVATION_REQUIRED (740).
Filename: "{app}\{#MyAppExeName}"; Description: "Executar o aplicativo"; Verb: "runas"; Flags: nowait postinstall skipifsilent shellexec
; Atualização automática (/VERYSILENT): o instalador já está elevado, então reabre o
; aplicativo diretamente quando termina.
Filename: "{app}\{#MyAppExeName}"; Flags: nowait; Check: WizardSilent
