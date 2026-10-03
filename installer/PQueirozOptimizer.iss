#define MyAppName "PQueiroz Optimizer"
#ifndef MyAppVersion
  #define MyAppVersion "1.8.1"
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
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://pqoptimizer.vercel.app/
AppSupportURL=https://discord.gg/pHJ4Waxft
AppUpdatesURL=https://github.com/PQueirozDev/Optimizer/releases/latest
AppCopyright=Copyright (c) {#MyAppPublisher}
DefaultDirName={autopf}\PQueiroz Optimizer
DefaultGroupName={#MyAppName}
OutputDir=..\artifacts\installer
OutputBaseFilename=PQueirozOptimizer-Setup-v{#MyAppVersion}
SetupIconFile=..\PQueirozOptimizer\Assets\app.ico
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 1809 ou mais novo (as APIs de energia, NvAPI e Defender usadas pelo app)
MinVersion=10.0.17763
; Visual do assistente: imagens com a identidade do app (geradas por tools\Create-InstallerArt.ps1),
; em 100% e 200% para ficarem nítidas em telas com escala
WizardStyle=modern
WizardImageFile=art\wizard-large.bmp,art\wizard-large-200.bmp
WizardSmallImageFile=art\wizard-small.bmp,art\wizard-small-200.bmp
WizardImageStretch=no
DisableWelcomePage=no
DisableProgramGroupPage=yes
DisableDirPage=auto
DisableReadyPage=no
AllowNoIcons=no
Compression=lzma2/ultra64
SolidCompression=yes
SetupLogging=yes
CloseApplications=force
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

[Messages]
brazilianportuguese.WelcomeLabel1=Bem-vindo ao [name]
brazilianportuguese.WelcomeLabel2=Este assistente vai instalar o [name/ver] no seu computador.%n%nOtimizações revisadas item por item com reversão, Modo Jogo, plano de energia Qrz, perfil NVIDIA, editor de BIOS, instalação limpa de driver, ferramentas de rede e monitor ao vivo — tudo em um só lugar.%n%nRecomenda-se fechar os outros programas antes de continuar.
brazilianportuguese.FinishedHeadingLabel=Tudo pronto!
brazilianportuguese.FinishedLabel=O [name] foi instalado. Na primeira abertura, ative sua chave de acesso pela janela de ativação.
english.WelcomeLabel1=Welcome to [name]
english.WelcomeLabel2=This wizard will install [name/ver] on your computer.%n%nOptimizations reviewed item by item with restore, Game Mode, the Qrz power plan, NVIDIA profile, BIOS editor, clean driver install, network tools and a live monitor — all in one place.%n%nIt is recommended that you close all other applications before continuing.
english.FinishedHeadingLabel=All set!
english.FinishedLabel=[name] has been installed. On first launch, activate your access key in the activation window.

[CustomMessages]
brazilianportuguese.RunApp=Abrir o PQueiroz Optimizer agora
english.RunApp=Open PQueiroz Optimizer now
brazilianportuguese.RemoveData=Remover também os backups e as preferências do PQueiroz Optimizer?%n%nEles guardam o estado original do Windows para a reversão das otimizações. Mantenha-os se pretende reinstalar o aplicativo ou ainda quer desfazer algum ajuste.
english.RemoveData=Also remove PQueiroz Optimizer backups and preferences?%n%nThey keep the original Windows state used to restore optimizations. Keep them if you plan to reinstall the app or still want to undo a tweak.
brazilianportuguese.RequiresAdmin=O PQueiroz Optimizer precisa de permissão de administrador para aplicar as otimizações.
english.RequiresAdmin=PQueiroz Optimizer needs administrator permission to apply optimizations.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Comment: "{cm:RequiresAdmin}"
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
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:RunApp}"; Verb: "runas"; Flags: nowait postinstall skipifsilent shellexec
; Atualização automática (/VERYSILENT): o instalador já está elevado, então reabre o
; aplicativo diretamente quando termina.
Filename: "{app}\{#MyAppExeName}"; Flags: nowait; Check: WizardSilent

[Code]
// Ao desinstalar, pergunta se apaga os backups (usados para reverter otimizações) e as preferências.
// O padrão é manter: apagar por engano impediria desfazer ajustes já aplicados ao Windows.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep <> usPostUninstall then Exit;
  DataDir := ExpandConstant('{commonappdata}\OtimizadorPC');
  if UninstallSilent or not DirExists(DataDir) then Exit;
  if MsgBox(CustomMessage('RemoveData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
  begin
    DelTree(DataDir, True, True, True);
    DelTree(ExpandConstant('{commonappdata}\PQueirozOptimizer'), True, True, True);
  end;
end;
