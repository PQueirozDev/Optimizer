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
    private readonly ActivityLog _log = new();
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

    public MainWindow()
    {
        InitializeComponent();
        _powershell = new PowerShellBridge(_log);
        _cleaner = new QuickCleanService(_log);
        _log.EntryAdded += line => Dispatcher.BeginInvoke(() => AddLogLine(line));
        foreach (var line in _log.Recent()) AddLogLine(line);
        if (_configService.LastLoadError is { } loadError) _log.Write("WARN", loadError);
        if (_configService.LastSaveError is { } saveError) _log.Write("WARN", "Preferências não foram salvas: " + saveError);
        VersionLabel.Text = "Versão " + AppVersion;
        Closing += (_, e) => { if (_operationRunning) { e.Cancel = true; OperationStatus.Text = "Aguarde a operação terminar antes de fechar."; } };

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
        LicenseLabel.Text = license is null ? "Sem licença ativa" : $"{(license.IsAdmin ? "Admin" : "Padrão")} · {license.Licensee}";
        LicenseLabel.ToolTip = license?.ExpiresAtUtc is { } expires ? $"Válida até {expires.ToLocalTime():dd/MM/yyyy}" : "Licença sem data de expiração";
    }

    #region Window & Language Controls
    private void LangButton_Click(object sender, RoutedEventArgs e)
    {
        var newLang = _loc.ToggleLanguage();
        _configService.SaveLanguage(newLang);
        UpdateLanguageUi();
        NavigateTo(_currentPage);
    }

    private void UpdateLanguageUi()
    {
        LangButton.Content = _loc.IsEnglish ? "EN" : "PT";
        LangButton.ToolTip = _loc.T("Alternar idioma para Inglês (EN)", "Switch language to Portuguese (PT)");
        TitlePageHint.Text = _loc.T(" Otimizador e Gerenciador do Windows", " Windows Optimizer & System Manager");
        TitleOptChip.Text = _loc.T("Pronto", "Ready");

        NavSecMonitor.Text = _loc.T("MONITORAR", "MONITOR");
        NavSecOptimizations.Text = _loc.T("OTIMIZAÇÕES", "OPTIMIZATIONS");
        NavSecSystem.Text = _loc.T("SISTEMA", "SYSTEM");

        NavDashboardText.Text = _loc.T("Visão geral", "Overview");
        NavOptText.Text = _loc.T("Otimizações", "Optimization");
        NavDriversText.Text = _loc.T("Drivers", "Drivers");
        NavIsosText.Text = _loc.T("Imagens do Windows", "Windows images");
        NavToolsText.Text = _loc.T("Ferramentas", "Tools");
        NavHistoryText.Text = _loc.T("Atividade e reversão", "Activity & restore");
        NavSettingsText.Text = _loc.T("Configurações", "Settings");
        NavAboutText.Text = _loc.T("Sobre", "About");
        NavFooterStatus.Text = _loc.T("Sistema ativo", "System active");
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
            await ShowDashboardAsync();
            await CheckForUpdateAsync(showPrompt: true);
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
            MessageBox.Show("As imagens personalizadas do Windows estão disponíveis somente para licenças de administrador.", "Acesso restrito", MessageBoxButton.OK, MessageBoxImage.Information);
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
        }
    }

    private void UpdateActiveNavButton(string page)
    {
        var buttons = new[] { NavDashboard, NavOpt, NavDrivers, NavIsos, NavTools, NavSettings, NavAbout, NavHistory, NavPatchNotes, NavBios, NavAdmin };
        foreach (var b in buttons) b.IsChecked = b.Tag?.ToString() == page;
    }

    #region Theming
    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        _darkTheme = !_darkTheme;
        ApplyTheme(_darkTheme, saveConfig: true);
        NavigateTo(_currentPage);
    }

    private void ApplyTheme(bool isDark, bool saveConfig)
    {
        _darkTheme = isDark;
        ThemeService.Apply(Application.Current.Resources, isDark);
        ThemeButton.Content = Icon(isDark ? Glyphs.Sun : Glyphs.Moon, 13);
        ThemeButton.ToolTip = isDark ? "Alternar para tema claro" : "Alternar para tema escuro";

        if (saveConfig)
        {
            _configService.SaveTheme(isDark ? "Dark" : "Light");
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

    private Border Card(string title, string value, string colorHex)
    {
        var card = new Border
        {
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 12, 12)
        };
        card.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var sp = new StackPanel();
        var t = new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = new BrushConverter().ConvertFromString(colorHex) as Brush ?? Brushes.Gray
        };
        var v = new TextBlock
        {
            Text = value,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        v.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

        sp.Children.Add(t);
        sp.Children.Add(v);
        card.Child = sp;
        return card;
    }

    private void AddInfo(Panel panel, string title, string value)
    {
        panel.Children.Add(Card(title, value, "#4F75FF"));
    }

    private void AddLogLine(string line) { _activity.Add(line); while (_activity.Count > 150) _activity.RemoveAt(0); }
    #endregion
}
