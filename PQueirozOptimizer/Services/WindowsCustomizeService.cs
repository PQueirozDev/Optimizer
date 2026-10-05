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

        foreach (var effect in VisualEffects()) yield return effect;
    }

    // ================= Efeitos visuais (Sistema → Opções de desempenho) =================
    public const string VisualEffectsCategory = "Efeitos visuais";
    private const string VisualFx = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects";
    private const string Dwm = @"Software\Microsoft\Windows\DWM";
    private const uint SpifSave = 0x01 | 0x02; // SPIF_UPDATEINIFILE | SPIF_SENDCHANGE: vale na hora e fica salvo no perfil

    [StructLayout(LayoutKind.Sequential)] private struct AnimationInfo { public uint Size; public int MinAnimate; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SystemParametersInfo(uint action, uint param, IntPtr value, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SystemParametersInfo(uint action, uint param, ref int value, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SystemParametersInfo(uint action, uint param, ref AnimationInfo value, uint flags);

    /// <summary>
    /// Os mesmos itens da aba Efeitos visuais do Windows, na mesma ordem. Os que o Windows guarda via SystemParametersInfo
    /// são aplicados por ela (valem na hora); os demais ficam no registro do Explorer/DWM.
    /// <see cref="WindowsSetting.Recommended"/> segue a otimização do app: só a fonte suavizada, as miniaturas e o conteúdo ao arrastar ficam ligados.
    /// </summary>
    private static IEnumerable<WindowsSetting> VisualEffects()
    {
        // Ordem e nomes da janela do Windows; Spi(get, set) usa o BOOL no pvParam (padrão das ações 0x10xx)
        yield return Spi("Animar controles e elementos dentro das janelas", "Botões, barras e listas com transições animadas.", 0x1042, 0x1043);
        yield return Effect("Animar janelas ao minimizar e maximizar", "A janela \"voa\" até a barra de tarefas em vez de sumir na hora.",
            () => { var info = new AnimationInfo { Size = 8 }; return SystemParametersInfo(0x0048, 8, ref info, 0) && info.MinAnimate != 0; },
            on => { var info = new AnimationInfo { Size = 8, MinAnimate = on ? 1 : 0 }; Check(SystemParametersInfo(0x0049, 8, ref info, SpifSave)); });
        yield return Effect("Animações na barra de tarefas", "Ícones da barra deslizam e piscam ao abrir e fechar apps.",
            () => Dword(Registry.CurrentUser, Advanced, "TaskbarAnimations", 1) == 1, on => SetDword(Registry.CurrentUser, Advanced, "TaskbarAnimations", on ? 1 : 0), restart: true);
        yield return Effect("Ativar Espiar", "Passar o mouse no canto da barra deixa as janelas transparentes para ver a área de trabalho.",
            () => Dword(Registry.CurrentUser, Dwm, "EnableAeroPeek", 1) == 1, on => SetDword(Registry.CurrentUser, Dwm, "EnableAeroPeek", on ? 1 : 0), restart: true);
        yield return Spi("Esmaecer ou deslizar menus", "Menus aparecem com fade ou deslizando, em vez de abrir na hora.", 0x1002, 0x1003);
        yield return Spi("Esmaecer ou deslizar dicas de ferramenta", "As dicas ao passar o mouse aparecem com animação.", 0x1016, 0x1017);
        yield return Spi("Esmaecer itens de menu após clicar", "O item clicado some aos poucos depois que o menu fecha.", 0x1014, 0x1015);
        yield return Effect("Salvar visualizações de miniaturas da barra de tarefas", "Guarda a prévia das janelas minimizadas na memória.",
            () => Dword(Registry.CurrentUser, Dwm, "AlwaysHibernateThumbnails", 0) == 1, on => SetDword(Registry.CurrentUser, Dwm, "AlwaysHibernateThumbnails", on ? 1 : 0), restart: true);
        yield return Effect("Mostrar retângulo de seleção translúcido", "O retângulo ao selecionar arquivos com o mouse fica azul translúcido.",
            () => Dword(Registry.CurrentUser, Advanced, "ListviewAlphaSelect", 1) == 1, on => SetDword(Registry.CurrentUser, Advanced, "ListviewAlphaSelect", on ? 1 : 0), restart: true);
        yield return Effect("Mostrar conteúdo da janela ao arrastar", "A janela inteira acompanha o mouse ao ser arrastada, e não só o contorno.",
            () => { var value = 0; return SystemParametersInfo(0x0026, 0, ref value, 0) && value != 0; },
            on => Check(SystemParametersInfo(0x0025, on ? 1u : 0u, IntPtr.Zero, SpifSave)), recommended: true);
        yield return Spi("Mostrar sombras sob janelas", "Sombra suave em volta das janelas abertas.", 0x1024, 0x1025);
        yield return Effect("Mostrar miniaturas em vez de ícones", "O Explorador mostra a prévia de fotos e vídeos; desligado, só o ícone do tipo de arquivo.",
            () => Dword(Registry.CurrentUser, Advanced, "IconsOnly", 0) == 0, on => SetDword(Registry.CurrentUser, Advanced, "IconsOnly", on ? 0 : 1), recommended: true, restart: true);
        yield return Spi("Mostrar sombras sob o ponteiro do mouse", "Sombra embaixo da seta do mouse.", 0x101A, 0x101B);
        yield return Effect("Usar sombras subjacentes para rótulos de ícones na área de trabalho", "Sombra atrás do nome dos ícones da área de trabalho.",
            () => Dword(Registry.CurrentUser, Advanced, "ListviewShadow", 1) == 1, on => SetDword(Registry.CurrentUser, Advanced, "ListviewShadow", on ? 1 : 0), restart: true);
        yield return Effect("Suavizar bordas das fontes de tela", "Deixa o texto nítido (ClearType). Desligado, as letras ficam serrilhadas.",
            () => { var value = 0; return SystemParametersInfo(0x004A, 0, ref value, 0) && value != 0; },
            on => Check(SystemParametersInfo(0x004B, on ? 1u : 0u, IntPtr.Zero, SpifSave)), recommended: true);
        yield return Spi("Rolagem suave de caixas de listagem", "Listas rolam com animação em vez de pular de linha em linha.", 0x1006, 0x1007);
        yield return Spi("Deslizar caixas de combinação ao abrir", "Listas suspensas abrem deslizando.", 0x1004, 0x1005);
    }

    private static WindowsSetting Spi(string title, string description, uint get, uint set) => Effect(title, description,
        () => { var value = 0; return SystemParametersInfo(get, 0, ref value, 0) && value != 0; },
        on => Check(SystemParametersInfo(set, 0, (IntPtr)(on ? 1 : 0), SpifSave)));

    // Mexer num item vira "Personalizado" na janela do Windows (VisualFXSetting 3), como quando se marca uma caixa lá
    private static WindowsSetting Effect(string title, string description, Func<bool> read, Action<bool> write, bool recommended = false, bool restart = false) =>
        new(VisualEffectsCategory, title, description, read, on => { write(on); SetDword(Registry.CurrentUser, VisualFx, "VisualFXSetting", 3); }, recommended, restart);

    private static void Check(bool ok)
    {
        if (!ok) throw new System.IO.IOException($"O Windows recusou a alteração (erro {Marshal.GetLastWin32Error()}).");
    }

    /// <summary>
    /// Os botões "Melhor desempenho" e "Melhor aparência" da janela do Windows: desliga ou liga todos os efeitos
    /// e deixa a mesma opção marcada lá (VisualFXSetting 2 ou 1).
    /// </summary>
    public void ApplyVisualPreset(bool bestPerformance)
    {
        foreach (var effect in VisualEffects()) effect.Write(!bestPerformance);
        SetDword(Registry.CurrentUser, VisualFx, "VisualFXSetting", bestPerformance ? 2 : 1);
        SendMessageTimeout((IntPtr)0xFFFF, 0x001A, UIntPtr.Zero, null, 0x0002, 1000, out _);
        _log.Write("SUCCESS", $"Efeitos visuais: {(bestPerformance ? "melhor desempenho" : "melhor aparência")}");
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
