using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
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
    private bool IsAdminLicense => (Application.Current as App)?.ActiveLicense?.IsAdmin == true;

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

        // Load saved theme & language
        _darkTheme = !_configService.Config.Theme.Equals("Light", StringComparison.OrdinalIgnoreCase);
        ApplyTheme(_darkTheme, saveConfig: false);
        _loc.SetLanguage(_configService.Config.Language ?? "pt");
        UpdateLanguageUi();
        UpdateLicenseUi();
        UpdateNavBadges();
        UpdateActiveNavButton(_currentPage);
    }

    private void UpdateLicenseUi()
    {
        var license = (Application.Current as App)?.ActiveLicense;
        NavIsos.Visibility = IsAdminLicense ? Visibility.Visible : Visibility.Collapsed;
        LicenseLabel.Text = license is null ? "Sem licença ativa" : $"{(license.IsAdmin ? "Admin" : _loc.T("Padrão", "Standard"))} · {license.Licensee}";
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

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_currentPage == "dashboard") await ShowDashboardAsync();
            else NavigateTo(_currentPage);
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
        UpdateActiveNavButton(page);
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
        }
    }

    private void UpdateActiveNavButton(string page)
    {
        var buttons = new[] { NavDashboard, NavOpt, NavStartup, NavDrivers, NavIsos, NavTools, NavSettings, NavAbout, NavHistory, NavPatchNotes, NavBios, NavAdmin };
        foreach (var b in buttons) b.IsChecked = b.Tag?.ToString() == page;
    }

    #region Theming
    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyTheme(!_darkTheme, saveConfig: true);
        // Configurações mostra qual tema está ativo; nas outras páginas, redesenhar apagaria o que está
        // na tela (ex.: a saída de uma operação em andamento ou os ajustes marcados na revisão)
        if (_currentPage == "settings" && !_operationRunning) ShowSettings();
        else if (Application.Current.TryFindResource("ShadowColor") is Color shadow) RefreshThemedVisuals(ContentHost, shadow);
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

    private void ApplyTheme(bool isDark, bool saveConfig)
    {
        _darkTheme = isDark;
        ThemeService.Apply(Application.Current.Resources, isDark);
        ThemeButton.Content = GlyphIcon(isDark ? Glyphs.Sun : Glyphs.Moon, 13);
        ThemeButton.ToolTip = isDark ? "Alternar para tema claro" : "Alternar para tema escuro";

        if (saveConfig)
        {
            try { _configService.SaveTheme(isDark ? "Dark" : "Light"); }
            catch (IOException ex) { _log.Write("WARN", "Preferências não foram salvas: " + (ex.InnerException?.Message ?? ex.Message)); }
        }
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

    private void AddLogLine(string line) { _activity.Add(line); while (_activity.Count > 150) _activity.RemoveAt(0); }
    #endregion
}
