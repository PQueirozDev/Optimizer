using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PQueirozOptimizer.Models;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

public partial class MainWindow : Window
{
    private readonly SystemInfoService _system = new();
    private readonly ActivityLog _log = (Application.Current as App)?.Log ?? new();
    private readonly PowerShellBridge _powershell;
    private readonly QuickCleanService _cleaner;
    private readonly ConfigService _configService = new();
    private readonly DriverService _driverService = new();
    private readonly LocalizationService _loc = new();

    private SystemSnapshot? _snapshot;
    private bool _operationRunning;
    private bool _darkTheme = true;
    private string _currentPage = "dashboard";
    private string _currentOptCategory = "todas";
    private string _driverCategory = "Todos";
    private string _driverSearch = "";
    internal bool IsOperationRunning => _operationRunning;
    private bool IsAdminLicense =>(Application.Current as App)?.ActiveLicense?.IsAdmin == true;

    public MainWindow() : this("dashboard") { _promptForUpdates = true; }

    // Só pergunta sobre atualização na abertura do app, não quando a janela é recriada (troca de idioma)
    private bool _promptForUpdates;

    public MainWindow(string startPage)
    {
        InitializeComponent();
        _currentPage = startPage;
        _powershell = new PowerShellBridge(_log);
        _cleaner = new QuickCleanService(_log);
        foreach (var line in _log.Recent()) AddLogLine(line);
        // O log é do processo inteiro; a janela recriada na troca de idioma não pode continuar ouvindo
        Action<string> onEntry = line => Dispatcher.BeginInvoke(() => AddLogLine(line));
        _log.EntryAdded += onEntry;
        Closed += (_, _) => _log.EntryAdded -= onEntry;
        if (_configService.LastLoadError is { } loadError) _log.Write("WARN", loadError);
        if (_configService.LastSaveError is { } saveError) _log.Write("WARN", "Preferências não foram salvas: " + saveError);
        VersionLabel.Text = "Versão " + AppVersion;
        Closing += (_, e) =>
        {
            if (!_operationRunning) return;
            e.Cancel = true;
            if (_closeAfterOperation) return;
            if (Msg("Há uma operação em andamento. Deseja cancelá-la e fechar o aplicativo? O que já foi aplicado continua registrado no backup e pode ser revertido em Atividade e reversão.",
                    "Cancelar operação", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                OperationStatus.Text = "Aguarde a operação terminar antes de fechar.";
                return;
            }
            _closeAfterOperation = true;
            if (_operationCts is { } cts) { OperationStatus.Text = "Cancelando..."; cts.Cancel(); }
            else Dispatcher.BeginInvoke(Close); // terminou enquanto a pergunta estava aberta
        };

        // Aparência salva (já aplicada pelo App antes da janela abrir; reaplicada aqui para janelas recriadas)
        AppearanceService.Apply(ConfigService.EffectiveAppearance(_configService.Config));
        _darkTheme = ThemeService.IsDark;
        UpdateThemeButton();
        Action onAppearance = () => Dispatcher.BeginInvoke(OnAppearanceChanged);
        AppearanceService.Changed += onAppearance;
        Closed += (_, _) => AppearanceService.Changed -= onAppearance;
        StartAurora();
        UpdatePageHeader(_currentPage);
        _loc.SetLanguage(_configService.Config.Language ?? "pt");
        UpdateLanguageUi();
        UpdateLicenseUi();
        UpdateNavBadges();
        UpdateActiveNavButton(_currentPage);
        StartHud();
    }

    private void UpdateLicenseUi()
    {
        var license = (Application.Current as App)?.ActiveLicense;
        NavIsos.Visibility = IsAdminLicense ? Visibility.Visible : Visibility.Collapsed;
        LicenseLabel.Text = license is null ? "Sem licença ativa" : $"{(license.IsAdmin ? "Admin" : license.PlanName)} · {license.Licensee}";
        LicenseLabel.ToolTip = license?.ExpiresAtUtc is { } expires ? $"Válida até {expires.ToLocalTime():dd/MM/yyyy}" : "Licença sem data de expiração";
    }

    #region Window & Language Controls
    private void LangButton_Click(object sender, RoutedEventArgs e) => ChangeLanguage(_loc.IsEnglish ? "pt" : "en");

    /// <summary>
    /// Troca o idioma recriando a janela na mesma posição e página: assim todos os textos,
    /// inclusive os fixos do layout, são exibidos de novo já no idioma escolhido.
    /// </summary>
    private void ChangeLanguage(string language)
    {
        if (_operationRunning) { OperationStatus.Text = "Aguarde a operação em andamento."; return; }
        if (language == _loc.CurrentLanguage) return;
        _loc.SetLanguage(language);
        try { _configService.SaveLanguage(language); }
        catch (IOException ex) { _log.Write("WARN", "Preferências não foram salvas: " + (ex.InnerException?.Message ?? ex.Message)); }

        var replacement = new MainWindow(_currentPage == "admin" ? "dashboard" : _currentPage);
        if (WindowState == WindowState.Normal)
        {
            replacement.WindowStartupLocation = WindowStartupLocation.Manual;
            replacement.Left = Left; replacement.Top = Top; replacement.Width = Width; replacement.Height = Height;
        }
        replacement.WindowState = WindowState;
        Application.Current.MainWindow = replacement;
        replacement.Show();
        Close();
    }

    private void UpdateLanguageUi()
    {
        // Os demais textos fixos do layout são traduzidos pelo Translator ao serem exibidos
        LangButton.Content = _loc.IsEnglish ? "EN" : "PT";
        LangButton.ToolTip = _loc.T("Alternar idioma para inglês (EN)", "Switch language to Portuguese (PT)");
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void BtnMaximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = (WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            MainRootBorder.Padding = new Thickness(7);
            BtnMaximize.Content = Glyphs.Restore;
            BtnMaximize.ToolTip = _loc.T("Restaurar", "Restore");
        }
        else
        {
            MainRootBorder.Padding = new Thickness(0);
            BtnMaximize.Content = Glyphs.Maximize;
            BtnMaximize.ToolTip = _loc.T("Maximizar", "Maximize");
        }
    }
    #endregion

    private void UpdateNavBadges()
    {
        try
        {
            var active = _configService.GetActiveProfile();
            NavOptBadgeText.Text = active.EnabledOptimizations.Count.ToString();
        }
        catch (Exception ex) { _log.Write("WARN", "Perfil ativo indisponível: " + ex.Message); }
    }

    /// <summary>
    /// Abertura: o logo cresce e aparece, some em seguida e a interface sobe no lugar (~1,2 s).
    /// Não depende das animações do Windows: a otimização de efeitos visuais do próprio app as desliga.
    /// </summary>
    private void PlayIntroAnimation()
    {
        // Animações desligadas em Configurações → Aparência: a interface aparece direto
        if (!AppearanceService.AnimationsEnabled) { IntroOverlay.Visibility = Visibility.Collapsed; return; }
        static DoubleAnimation Anim(double from, double to, double beginMs, double durationMs, IEasingFunction? ease = null) =>
            new(from, to, TimeSpan.FromMilliseconds(durationMs))
            {
                BeginTime = TimeSpan.FromMilliseconds(beginMs),
                EasingFunction = ease ?? new CubicEase { EasingMode = EasingMode.EaseOut }
            };

        var logoScale = (ScaleTransform)IntroLogo.RenderTransform;
        var pop = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };
        IntroLogo.BeginAnimation(OpacityProperty, Anim(0, 1, 0, 350));
        logoScale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(0.6, 1, 0, 550, pop));
        logoScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(0.6, 1, 0, 550, pop));
        IntroTitle.BeginAnimation(OpacityProperty, Anim(0, 1, 200, 400));
        ((TranslateTransform)IntroTitle.RenderTransform).BeginAnimation(TranslateTransform.YProperty, Anim(10, 0, 200, 400));

        // A interface entra enquanto a abertura sai
        const double revealAt = 850;
        foreach (UIElement part in RootGrid.Children)
        {
            if (part == IntroOverlay) continue;
            var slide = new TranslateTransform(0, 16);
            part.RenderTransform = slide;
            part.Opacity = 0;
            part.BeginAnimation(OpacityProperty, Anim(0, 1, revealAt, 380));
            slide.BeginAnimation(TranslateTransform.YProperty, Anim(16, 0, revealAt, 450));
        }
        var fadeOut = Anim(1, 0, revealAt, 350, new CubicEase { EasingMode = EasingMode.EaseIn });
        fadeOut.Completed += (_, _) => IntroOverlay.Visibility = Visibility.Collapsed;
        IntroOverlay.BeginAnimation(OpacityProperty, fadeOut);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        PlayIntroAnimation();
        try
        {
            if (_currentPage == "dashboard") await ShowDashboardAsync();
            else NavigateTo(_currentPage);
            MaybeShowWelcomeTour();
            await CheckForUpdateAsync(showPrompt: _promptForUpdates);
        }
        catch (Exception ex)
        {
            _log.Write("ERROR", $"Falha ao carregar dashboard: {ex}");
            TitleAdminChip.Text = "Indisponível";
            ContentHost.Children.Clear();
            ContentHost.Children.Add(Card("ERRO DE INICIALIZAÇÃO", "O aplicativo abriu, mas não conseguiu consultar todas as informações do sistema.", Glyphs.Warning, "WarningBrush"));
        }
    }

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        var page = (sender as FrameworkElement)?.Tag?.ToString() ?? "dashboard";
        NavigateTo(page);
    }

    private void NavigateTo(string page)
    {
        if (page == "isos" && !IsAdminLicense)
        {
            Msg("As imagens personalizadas do Windows estão disponíveis somente para licenças de administrador.", "Acesso restrito", MessageBoxButton.OK, MessageBoxImage.Information);
            UpdateActiveNavButton(_currentPage);
            return;
        }
        if (page == "admin")
        {
            // A ativação abre uma janela; a seleção da navegação continua na página atual
            UpdateActiveNavButton(_currentPage);
            ActivateAdminLicense();
            return;
        }
        _currentPage = page;
        _tutorialMarks.Clear();
        UpdateActiveNavButton(page);
        UpdatePageHeader(page);
        ContentScroll.ScrollToTop();

        switch (page)
        {
            case "dashboard": _ = ShowDashboardAsync(); break;
            case "optimization": ShowOptimization(); break;
            case "drivers": ShowDrivers(); break;
            case "isos": ShowIsos(); break;
            case "tools": ShowTools(); break;
            case "settings": ShowSettings(); break;
            case "about": ShowAbout(); break; case "history": ShowHistory(); break;
            case "patchnotes": ShowPatchNotes(); break;
            case "bios": ShowBios(); break;
            case "startup": ShowStartup(); break;
            case "gaming": ShowGaming(); break;
            case "network": ShowNetwork(); break;
            case "restore": ShowRestorePoints(); break;
            case "resources": ShowResources(); break;
            case "fixes": ShowFixes(); break;
            case "services": ShowServices(); break;
            case "apps": ShowApps(); break;
        }
        AnimatePageIn();
        MaybeShowPageTutorial(page);
    }

    private void UpdateActiveNavButton(string page)
    {
        var buttons = new[] { NavDashboard, NavOpt, NavStartup, NavDrivers, NavIsos, NavTools, NavGaming, NavNetwork, NavRestore, NavResources, NavFixes, NavServices, NavApps, NavSettings, NavAbout, NavHistory, NavPatchNotes, NavBios, NavAdmin };
        foreach (var b in buttons) b.IsChecked = b.Tag?.ToString() == page;
    }

    #region Theming
    /// <summary>Botão do topo: alterna Escuro → OLED → Automático (o mesmo de Configurações → Aparência).</summary>
    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        var next = AppearanceService.Current.Clone();
        next.Theme = next.Theme switch { Models.ThemeMode.Dark => Models.ThemeMode.Oled, Models.ThemeMode.Oled => Models.ThemeMode.Auto, _ => Models.ThemeMode.Dark };
        SaveAppearance(next);
    }

    /// <summary>Aplica e salva novas preferências de aparência.</summary>
    private void SaveAppearance(Models.AppearanceSettings settings)
    {
        AppearanceService.Apply(settings);
        try { _configService.SaveAppearance(settings); }
        catch (IOException ex) { _log.Write("WARN", "Preferências não foram salvas: " + (ex.InnerException?.Message ?? ex.Message)); }
    }

    /// <summary>
    /// Depois de mudar a aparência: atualiza o que não acompanha os recursos dinâmicos (sombras, aurora,
    /// ícone do tema) e redesenha a página — exceto durante uma operação, para não apagar a saída na tela.
    /// </summary>
    private void OnAppearanceChanged()
    {
        _darkTheme = ThemeService.IsDark;
        StartAurora(); // reinicia (ou para) o movimento do fundo conforme as animações
        UpdateThemeButton();
        if (Application.Current.TryFindResource("ShadowColor") is Color shadow) RefreshThemedVisuals(this, shadow);
        if (!_operationRunning && IsLoaded && _currentPage is not ("dashboard")) NavigateTo(_currentPage);
        else if (!_operationRunning && IsLoaded) _ = RenderDashboardAsync();
    }

    private void UpdateThemeButton()
    {
        var mode = AppearanceService.Current.Theme;
        ThemeButton.Content = GlyphIcon(mode switch { Models.ThemeMode.Oled => Glyphs.Moon, Models.ThemeMode.Auto => Glyphs.Refresh, _ => Glyphs.Sun }, 13);
        ThemeButton.ToolTip = mode switch { Models.ThemeMode.Dark => "Tema: Escuro (clique para OLED)", Models.ThemeMode.Oled => "Tema: OLED (clique para Automático)", _ => "Tema: Automático (clique para Escuro)" };
    }

    /// <summary>
    /// Sombras e as cores das linhas da saída (calculadas por conversor) não acompanham os recursos
    /// dinâmicos; são as únicas partes da tela que precisam ser atualizadas à mão na troca de tema.
    /// </summary>
    private static void RefreshThemedVisuals(DependencyObject node, Color shadow)
    {
        if (node is UIElement { Effect: System.Windows.Media.Effects.DropShadowEffect { IsFrozen: false } effect }) effect.Color = shadow;
        if (node is ListBox list) { list.Items.Refresh(); return; }
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) RefreshThemedVisuals(VisualTreeHelper.GetChild(node, i), shadow);
    }

    /// <summary>Usado pela renderização de teste: claro força a paleta clara; escuro volta às preferências salvas.</summary>
    private void ApplyTheme(bool isDark, bool saveConfig)
    {
        ThemeService.Apply(Application.Current.Resources, AppearanceService.Current, forceLight: !isDark);
        _darkTheme = ThemeService.IsDark;
        RefreshAuroraColors();
        UpdateThemeButton();
    }
    #endregion

    #region Dashboard
    private async Task ShowDashboardAsync() => await RenderDashboardAsync();
    #endregion


    #region Helpers & Actions
    private Button ActionButton(string text, string operation)
    {
        var b = new Button { Content = text, Tag = operation };
        b.Click += RunOperation_Click;
        return b;
    }

    private async void RunOperation_Click(object sender, RoutedEventArgs e) => await PrepareOperationAsync((sender as Button)?.Tag?.ToString() ?? "");

    private void AddLogLine(string line)
    {
        _activity.Add(line);
        while (_activity.Count > 150) _activity.RemoveAt(0);
        ShowLastActivity(line);
    }

    /// <summary>Barra de status: a última entrada do registro de atividade, com hora e cor do resultado.</summary>
    private void ShowLastActivity(string line)
    {
        var m = System.Text.RegularExpressions.Regex.Match(line, @"^\[(?:\d{4}-\d{2}-\d{2} )?(\d{2}:\d{2})(?::\d{2})?\]\s*\[(\w+)\]\s*(.+)$");
        if (!m.Success || m.Groups[2].Value == "INFO") return; // só resultados (sucesso, aviso, erro)
        var tone = m.Groups[2].Value switch { "ERROR" => "DangerBrush", "WARN" => "WarningBrush", _ => "SuccessBrush" };
        LastActivityDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, tone);
        LastActivityText.Text = $"{m.Groups[1].Value} · {Translator.Tr(m.Groups[3].Value.Trim())}";
        LastActivityText.ToolTip = line;
        LastActivityPanel.Visibility = Visibility.Visible;
    }
    #endregion
}
