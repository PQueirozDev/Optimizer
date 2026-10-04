using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PQueirozOptimizer.Services;

/// <summary>
/// Uma preferência do Windows com liga/desliga. <see cref="Recommended"/> é o valor sugerido (null = gosto pessoal);
/// <see cref="NeedsExplorerRestart"/> indica que o Explorer precisa reiniciar para mostrar a mudança.
/// </summary>
public sealed record WindowsSetting(string Category, string Title, string Description, Func<bool> Read, Action<bool> Write,
    bool? Recommended = null, bool NeedsExplorerRestart = false, string? Requires = null);

/// <summary>
/// Personalizar Windows: preferências do sistema lidas e gravadas direto no registro, na hora e sem passar
/// pela revisão de otimizações (cada uma volta ao estado anterior desligando o interruptor).
/// </summary>
public sealed class WindowsCustomizeService
{
    private const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string DesktopIcons = @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel";
    private const string ClassicMenu = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";

    private readonly ActivityLog _log;
    public WindowsCustomizeService(ActivityLog log) => _log = log;

    [DllImport("user32.dll", SetLastError = true)] private static extern bool SystemParametersInfo(uint action, uint param, int[] values, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, UIntPtr wParam, string? lParam, uint flags, uint timeout, out UIntPtr result);

    /// <summary>Opções que valem para esta versão do Windows, na ordem da tela.</summary>
    public IReadOnlyList<WindowsSetting> Settings => AllSettings().Where(s => SystemConditions.Satisfies(s.Requires)).ToList();

    private static IEnumerable<WindowsSetting> AllSettings()
    {
        // Barra de tarefas
        yield return new("Barra de tarefas", "Ícones alinhados à esquerda", "Iniciar e os apps ficam no canto esquerdo, como no Windows 10.",
            () => Dword(Registry.CurrentUser, Advanced, "TaskbarAl", 1) == 0, on => SetDword(Registry.CurrentUser, Advanced, "TaskbarAl", on ? 0 : 1), Requires: "win11");
        yield return new("Barra de tarefas", "Botão \"Finalizar tarefa\"", "Clique direito num app da barra para fechá-lo na hora, sem abrir o Gerenciador de Tarefas.",
            () => Dword(Registry.CurrentUser, Advanced + @"\TaskbarDeveloperSettings", "TaskbarEndTask", 0) == 1,
            on => SetDword(Registry.CurrentUser, Advanced + @"\TaskbarDeveloperSettings", "TaskbarEndTask", on ? 1 : 0), Recommended: true, NeedsExplorerRestart: true, Requires: "win11");
        yield return new("Barra de tarefas", "Segundos no relógio", "Mostra os segundos no relógio da barra de tarefas.",
            () => Dword(Registry.CurrentUser, Advanced, "ShowSecondsInSystemClock", 0) == 1, on => SetDword(Registry.CurrentUser, Advanced, "ShowSecondsInSystemClock", on ? 1 : 0), NeedsExplorerRestart: true);
        yield return new("Barra de tarefas", "Botão Visão de Tarefas", "Botão que mostra as janelas abertas e as áreas de trabalho virtuais.",
            () => Dword(Registry.CurrentUser, Advanced, "ShowTaskViewButton", 1) == 1, on => SetDword(Registry.CurrentUser, Advanced, "ShowTaskViewButton", on ? 1 : 0));
        yield return new("Barra de tarefas", "Pesquisa só como ícone", "Troca a caixa de pesquisa larga por uma lupa, liberando espaço na barra.",
            () => Dword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Search", "SearchboxTaskbarMode", 2) == 1,
            on => SetDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Search", "SearchboxTaskbarMode", on ? 1 : 2), NeedsExplorerRestart: true);

        // Explorador de Arquivos
        yield return new("Explorador de Arquivos", "Mostrar extensões dos arquivos", "Exibe o .exe, .pdf, .zip no nome: evita abrir um \"documento.pdf.exe\" por engano.",
            () => Dword(Registry.CurrentUser, Advanced, "HideFileExt", 1) == 0, on => SetDword(Registry.CurrentUser, Advanced, "HideFileExt", on ? 0 : 1), Recommended: true, NeedsExplorerRestart: true);
        yield return new("Explorador de Arquivos", "Mostrar arquivos ocultos", "Exibe pastas como AppData e arquivos marcados como ocultos.",
            () => Dword(Registry.CurrentUser, Advanced, "Hidden", 2) == 1, on => SetDword(Registry.CurrentUser, Advanced, "Hidden", on ? 1 : 2), NeedsExplorerRestart: true);
        yield return new("Explorador de Arquivos", "Abrir em \"Este Computador\"", "O Explorador abre mostrando as unidades de disco, em vez da página Início.",
            () => Dword(Registry.CurrentUser, Advanced, "LaunchTo", 2) == 1, on => SetDword(Registry.CurrentUser, Advanced, "LaunchTo", on ? 1 : 2));
        yield return new("Explorador de Arquivos", "Menu de contexto clássico", "O clique direito abre direto o menu completo, sem precisar de \"Mostrar mais opções\".",
            () => Registry.CurrentUser.OpenSubKey(ClassicMenu + @"\InprocServer32") is { } key && Dispose(key),
            on =>
            {
                if (on) { using var key = Registry.CurrentUser.CreateSubKey(ClassicMenu + @"\InprocServer32"); key.SetValue("", ""); }
                else Registry.CurrentUser.DeleteSubKeyTree(ClassicMenu, throwOnMissingSubKey: false);
            }, NeedsExplorerRestart: true, Requires: "win11");
        yield return new("Explorador de Arquivos", "Caixas de seleção nos itens", "Marque vários arquivos com o mouse, sem segurar Ctrl.",
            () => Dword(Registry.CurrentUser, Advanced, "AutoCheckSelect", 0) == 1, on => SetDword(Registry.CurrentUser, Advanced, "AutoCheckSelect", on ? 1 : 0), NeedsExplorerRestart: true);

        // Área de trabalho (0 = mostrar, 1 = ocultar; sem valor, só a Lixeira aparece)
        yield return DesktopIcon("\"Este Computador\" na área de trabalho", "{20D04FE0-3AEA-1069-A2D8-08002B30309D}", shownByDefault: false);
        yield return DesktopIcon("Lixeira na área de trabalho", "{645FF040-5081-101B-9F08-00AA002F954E}", shownByDefault: true);
        yield return DesktopIcon("Pasta do usuário na área de trabalho", "{59031a47-3f72-44a7-89c5-5595fe6b30ee}", shownByDefault: false);
        yield return DesktopIcon("Rede na área de trabalho", "{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}", shownByDefault: false);
        yield return DesktopIcon("Painel de Controle na área de trabalho", "{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}", shownByDefault: false);

        // Sistema
        yield return new("Sistema", "Histórico da área de transferência", "Win + V mostra os últimos itens copiados, não só o último.",
            () => Dword(Registry.CurrentUser, @"Software\Microsoft\Clipboard", "EnableClipboardHistory", 0) == 1,
            on => SetDword(Registry.CurrentUser, @"Software\Microsoft\Clipboard", "EnableClipboardHistory", on ? 1 : 0), Recommended: true);
        yield return new("Sistema", "Num Lock ligado ao iniciar", "O teclado numérico já começa ligado na tela de login.",
            () => (ParseLong(Registry.Users.OpenSubKey(@".DEFAULT\Control Panel\Keyboard")?.GetValue("InitialKeyboardIndicators") as string) & 2) != 0,
            on =>
            {
                foreach (var (hive, path) in new[] { (Registry.Users, @".DEFAULT\Control Panel\Keyboard"), (Registry.CurrentUser, @"Control Panel\Keyboard") })
                {
                    using var key = hive.CreateSubKey(path);
                    var current = ParseLong(key.GetValue("InitialKeyboardIndicators") as string);
                    key.SetValue("InitialKeyboardIndicators", (on ? current | 2 : current & ~2L).ToString(), RegistryValueKind.String);
                }
            }, Recommended: true);
        yield return new("Sistema", "Caminhos longos (mais de 260 caracteres)", "Permite pastas e arquivos com caminho muito comprido, comum em projetos e jogos com mods.",
            () => Dword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\FileSystem", "LongPathsEnabled", 0) == 1,
            on => SetDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\FileSystem", "LongPathsEnabled", on ? 1 : 0), Recommended: true);
        yield return new("Sistema", "Agitar a janela minimiza as outras", "Segurar a barra de título e sacudir minimiza todas as outras janelas.",
            () => Dword(Registry.CurrentUser, Advanced, "DisallowShaking", SystemConditions.WindowsBuild >= 22631 ? 1 : 0) == 0,
            on => SetDword(Registry.CurrentUser, Advanced, "DisallowShaking", on ? 0 : 1), Recommended: false);
        yield return new("Sistema", "Modo escuro do Windows", "Barra de tarefas, Iniciar, Configurações e apps compatíveis ficam escuros.",
            () => Dword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) == 0,
            on =>
            {
                SetDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", on ? 0 : 1);
                SetDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", on ? 0 : 1);
            });
        yield return new("Sistema", "Menus sem atraso", "Submenus abrem na hora ao passar o mouse, sem a espera padrão de 0,4 s.",
            () => Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop")?.GetValue("MenuShowDelay") as string == "0",
            on => { using var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop"); key.SetValue("MenuShowDelay", on ? "0" : "400", RegistryValueKind.String); },
            Recommended: true, NeedsExplorerRestart: true);

        // Teclado e mouse
        yield return new("Teclado e mouse", "Precisão do ponteiro (aceleração do mouse)", "Com ela ligada, o ponteiro anda mais quanto mais rápido você move o mouse; desligada, a mira fica consistente nos jogos.",
            () => Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse")?.GetValue("MouseSpeed") as string is not "0",
            on =>
            {
                using (var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Mouse"))
                {
                    key.SetValue("MouseSpeed", on ? "1" : "0", RegistryValueKind.String);
                    key.SetValue("MouseThreshold1", on ? "6" : "0", RegistryValueKind.String);
                    key.SetValue("MouseThreshold2", on ? "10" : "0", RegistryValueKind.String);
                }
                // SPI_SETMOUSE com SPIF_UPDATEINIFILE | SPIF_SENDCHANGE: vale na hora, sem sair da conta
                SystemParametersInfo(0x0004, 0, on ? new[] { 6, 10, 1 } : new[] { 0, 0, 0 }, 0x01 | 0x02);
            }, Recommended: false);
        yield return new("Teclado e mouse", "Atalho das Teclas de Aderência (Shift 5 vezes)", "Apertar Shift cinco vezes abre o aviso das Teclas de Aderência, que costuma tirar você do jogo. Vale a partir do próximo login.",
            () => (ParseLong(Registry.CurrentUser.OpenSubKey(@"Control Panel\Accessibility\StickyKeys")?.GetValue("Flags") as string) & 4) != 0,
            on =>
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Accessibility\StickyKeys");
                var flags = ParseLong(key.GetValue("Flags") as string ?? "510");
                key.SetValue("Flags", (on ? flags | 4 : flags & ~4L).ToString(), RegistryValueKind.String);
            }, Recommended: false);
    }

    private static WindowsSetting DesktopIcon(string title, string clsid, bool shownByDefault) => new("Área de trabalho", title, "Mostra ou esconde o ícone na área de trabalho.",
        () => Dword(Registry.CurrentUser, DesktopIcons, clsid, shownByDefault ? 0 : 1) == 0,
        on => SetDword(Registry.CurrentUser, DesktopIcons, clsid, on ? 0 : 1), NeedsExplorerRestart: true);

    private static bool Dispose(RegistryKey key) { key.Dispose(); return true; }

    private static int Dword(RegistryKey hive, string path, string name, int fallback)
    {
        using var key = hive.OpenSubKey(path);
        return key?.GetValue(name) is int value ? value : fallback;
    }

    private static void SetDword(RegistryKey hive, string path, string name, int value)
    {
        using var key = hive.CreateSubKey(path);
        key.SetValue(name, value, RegistryValueKind.DWord);
    }

    private static long ParseLong(string? text) => long.TryParse(text, out var value) ? value : 0;

    /// <summary>Grava a opção e avisa os programas abertos que as configurações mudaram.</summary>
    public void Apply(WindowsSetting setting, bool on)
    {
        setting.Write(on);
        // HWND_BROADCAST + WM_SETTINGCHANGE: Explorer e apps releem o que conseguem sem reiniciar
        SendMessageTimeout((IntPtr)0xFFFF, 0x001A, UIntPtr.Zero, null, 0x0002, 1000, out _);
        SendMessageTimeout((IntPtr)0xFFFF, 0x001A, UIntPtr.Zero, "ImmersiveColorSet", 0x0002, 1000, out _);
        _log.Write("SUCCESS", $"Personalizar Windows: {setting.Title} {(on ? "ligado" : "desligado")}");
    }

    /// <summary>
    /// Reinicia o Explorer (barra de tarefas e janelas de pastas). O próprio Windows o abre de novo, na conta do
    /// usuário; o app não o abre por conta própria porque roda como administrador e o Explorer abriria elevado.
    /// </summary>
    public void RestartExplorer()
    {
        foreach (var process in Process.GetProcessesByName("explorer"))
        {
            try { process.Kill(); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            finally { process.Dispose(); }
        }
        _log.Write("INFO", "Explorer reiniciado para aplicar as personalizações");
    }
}
