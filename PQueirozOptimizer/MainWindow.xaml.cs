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

    public MainWindow()
    {
        InitializeComponent();
        _powershell = new PowerShellBridge(_log);
        _cleaner = new QuickCleanService(_log);
        _log.EntryAdded += line => Dispatcher.BeginInvoke(() => AddLogLine(line));
        foreach (var line in _log.Recent()) AddLogLine(line);
        VersionLabel.Text = AppVersion;
        Closing += (_, e) => { if (_operationRunning) { e.Cancel = true; OperationStatus.Text = "Aguarde a operação terminar antes de fechar."; } };

        try
        {
            this.Icon = BitmapFrame.Create(new Uri("pack://application:,,,/PQueirozOptimizer;component/Assets/app.ico", UriKind.Absolute));
        }
        catch { }

        // Load saved theme & language
        _darkTheme = !_configService.Config.Theme.Equals("Light", StringComparison.OrdinalIgnoreCase);
        ApplyTheme(_darkTheme, saveConfig: false);
        _loc.SetLanguage(_configService.Config.Language ?? "pt");
        UpdateLanguageUi();
        UpdateNavBadges();
        UpdateActiveNavButton(_currentPage);
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
        ThemeButton.ToolTip = _loc.T("Alternar tema claro / escuro", "Toggle light / dark theme");
        TitlePageHint.Text = _loc.T(" Otimizador e Gerenciador do Windows", " Windows Optimizer & System Manager");
        TitleOptChip.Text = _loc.T("Pronto", "Ready");

        if (NavSecMonitor != null) NavSecMonitor.Text = _loc.T("MONITORAR", "MONITOR");
        if (NavSecOptimizations != null) NavSecOptimizations.Text = _loc.T("OTIMIZAÇÕES", "OPTIMIZATIONS");
        if (NavSecSystem != null) NavSecSystem.Text = _loc.T("SISTEMA", "SYSTEM");

        if (NavDashboard != null) NavDashboard.Content = _loc.T("Dashboard", "Dashboard");
        if (NavOptText != null) NavOptText.Text = _loc.T("Otimizações", "Optimization");
        if (NavDriversText != null) NavDriversText.Text = _loc.T("Drivers", "Drivers");
        if (NavIsos != null) NavIsos.Content = _loc.T("Imagens do Windows", "Windows images");
        if (NavTools != null) NavTools.Content = _loc.T("Ferramentas", "Tools");
        if (NavSettings != null) NavSettings.Content = _loc.T("Configurações", "Settings");
        if (NavAbout != null) NavAbout.Content = _loc.T("Sobre", "About");
        if (NavFooterStatus != null) NavFooterStatus.Text = _loc.T("Sistema Ativo", "System Active");
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
            BtnMaximize.Content = "";
            BtnMaximize.ToolTip = _loc.T("Restaurar", "Restore");
        }
        else
        {
            MainRootBorder.Padding = new Thickness(0);
            BtnMaximize.Content = "▢";
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
        catch { }
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
            ContentHost.Children.Add(Card("ERRO DE INICIALIZAÇÃO", "O aplicativo abriu, mas não conseguiu consultar todas as informações do sistema.", "#F0B429"));
        }
    }

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        var page = (sender as Button)?.Tag?.ToString() ?? "dashboard";
        NavigateTo(page);
    }

    private void NavigateTo(string page)
    {
        _currentPage = page;
        UpdateActiveNavButton(page);

        switch (page)
        {
            case "dashboard": _ = ShowDashboardAsync(); break;
            case "optimization": ShowOptimization(); break;
            case "drivers": ShowDrivers(); break;
            case "isos": ShowIsos(); break;
            case "tools": ShowTools(); break;
            case "settings": ShowSettings(); break;
            case "about": ShowAbout(); break; case "history": ShowHistory(); break;
        }
    }

    private void UpdateActiveNavButton(string page)
    {
        var buttons = new[] { NavDashboard, NavOpt, NavDrivers, NavIsos, NavTools, NavSettings, NavAbout, NavHistory };
        foreach (var b in buttons)
        {
            if (b == null) continue;
            var isCurrent = (b.Tag?.ToString() == page);
            if (isCurrent)
            {
                b.SetResourceReference(Button.BackgroundProperty, "PanelHoverBrush");
                b.SetResourceReference(Button.BorderBrushProperty, "AccentBrush");
                b.BorderThickness = new Thickness(2, 0, 0, 0);
                b.SetResourceReference(Button.ForegroundProperty, "TextBrush");
                b.FontWeight = FontWeights.SemiBold;
            }
            else
            {
                b.Background = Brushes.Transparent;
                b.BorderBrush = Brushes.Transparent;
                b.BorderThickness = new Thickness(0);
                b.SetResourceReference(Button.ForegroundProperty, "MutedBrush");
                b.FontWeight = FontWeights.Medium;
            }
        }
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
        var dict = Application.Current.Resources;

        if (isDark)
        {
            SetBrush(dict, "BackgroundBrush", "#101114");
            SetBrush(dict, "HeaderBrush", "#15161A");
            SetBrush(dict, "SidebarBrush", "#15161A");
            SetBrush(dict, "PanelBrush", "#15161A");
            SetBrush(dict, "PanelHoverBrush", "#24272E");
            SetBrush(dict, "CardBgBrush", "#1B1D22");
            SetBrush(dict, "BorderBrush", "#2A2D34");
            SetBrush(dict, "BorderSubtleBrush", "#202228");
            SetBrush(dict, "TextBrush", "#ECEEF2");
            SetBrush(dict, "MutedBrush", "#969BA6");
            SetBrush(dict, "AccentBrush", "#648CFF");
            SetBrush(dict, "AccentHoverBrush", "#7C9EFF");
            SetBrush(dict, "SuccessBrush", "#10B981");
            SetBrush(dict, "WarningBrush", "#F59E0B");
            SetBrush(dict, "DangerBrush", "#EF4444");

            ThemeButton.Content = "◐";
            ThemeButton.ToolTip = "Alternar para tema claro";
        }
        else
        {
            SetBrush(dict, "BackgroundBrush", "#F4F5F9");
            SetBrush(dict, "HeaderBrush", "#FFFFFF");
            SetBrush(dict, "SidebarBrush", "#FFFFFF");
            SetBrush(dict, "PanelBrush", "#FFFFFF");
            SetBrush(dict, "PanelHoverBrush", "#ECEFF6");
            SetBrush(dict, "CardBgBrush", "#F8F9FC");
            SetBrush(dict, "BorderBrush", "#DCE1EC");
            SetBrush(dict, "BorderSubtleBrush", "#E8ECF4");
            SetBrush(dict, "TextBrush", "#0F172A");
            SetBrush(dict, "MutedBrush", "#64748B");
            SetBrush(dict, "AccentBrush", "#3B5BDB");
            SetBrush(dict, "AccentHoverBrush", "#2F4DC4");
            SetBrush(dict, "SuccessBrush", "#059669");
            SetBrush(dict, "WarningBrush", "#D97706");
            SetBrush(dict, "DangerBrush", "#DC2626");

            ThemeButton.Content = "◐";
            ThemeButton.ToolTip = "Alternar para tema escuro";
        }

        if (saveConfig)
        {
            _configService.SaveTheme(isDark ? "Dark" : "Light");
        }
    }

    private static void SetBrush(ResourceDictionary dict, string key, string hex)
    {
        dict[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }
    #endregion

    #region Dashboard
    private async Task ShowDashboardAsync() => await RenderDashboardAsync();
    #endregion

    #region Optimization Page
    private void ShowOptimization()
    {
        PageTitle.Text = "Otimização";
        var activeProfile = _configService.GetActiveProfile();
        var allowedIds = new HashSet<string>(activeProfile.EnabledOptimizations, StringComparer.OrdinalIgnoreCase);

        var visibleOptimizations = ConfigService.AllOptimizations
            .Where(o => allowedIds.Contains(o.Id))
            .ToList();

        // Update header breadcrumb badge
        PageBadge.Visibility = Visibility.Visible;
        PageBadgeText.Text = $"{activeProfile.Name} · {visibleOptimizations.Count} ativas";

        var root = new StackPanel();

        // 1. Top profile summary bar
        var profileBar = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 0, 0, 16),
            BorderThickness = new Thickness(1)
        };
        profileBar.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        profileBar.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var barGrid = new Grid();
        barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var leftInfo = new StackPanel();
        var profileTitle = new TextBlock
        {
            Text = $"Perfil Ativo: {activeProfile.Name}",
            FontSize = 14.5,
            FontWeight = FontWeights.Bold
        };
        profileTitle.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

        var profileDesc = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(activeProfile.Description)
                ? $"Exibindo {visibleOptimizations.Count} de {ConfigService.AllOptimizations.Count} otimizações configuradas."
                : $"{activeProfile.Description} ({visibleOptimizations.Count} ativas)",
            FontSize = 12,
            Margin = new Thickness(0, 2, 0, 0)
        };
        profileDesc.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        leftInfo.Children.Add(profileTitle);
        leftInfo.Children.Add(profileDesc);
        Grid.SetColumn(leftInfo, 0);
        barGrid.Children.Add(leftInfo);

        var customizeBtn = new Button
        {
            Content = " Personalizar Otimizações",
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        customizeBtn.Click += (_, _) => NavigateTo("settings");
        Grid.SetColumn(customizeBtn, 1);
        barGrid.Children.Add(customizeBtn);

        profileBar.Child = barGrid;
        root.Children.Add(profileBar);

        // 2. Category Filter Pills (AgentTrail inspired tabs)
        var categoryRow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        var categories = new[]
        {
            ("todas", $"Todas ({visibleOptimizations.Count})"),
            ("Desempenho", " Desempenho"),
            ("Limpeza", " Limpeza"),
            ("Manutenção", " Manutenção"),
            ("Diagnóstico", " Diagnóstico"),
            ("Segurança", "↩ Segurança")
        };

        foreach (var cat in categories)
        {
            var isSelected = _currentOptCategory.Equals(cat.Item1, StringComparison.OrdinalIgnoreCase);
            var pill = new Button
            {
                Content = cat.Item2,
                Tag = cat.Item1,
                Padding = new Thickness(12, 5, 12, 5),
                Margin = new Thickness(0, 0, 8, 0),
                FontSize = 12,
                FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Medium
            };
            if (isSelected)
            {
                pill.SetResourceReference(Button.BackgroundProperty, "AccentBrush");
                pill.Foreground = Brushes.White;
            }
            else
            {
                pill.SetResourceReference(Button.BackgroundProperty, "CardBgBrush");
                pill.SetResourceReference(Button.ForegroundProperty, "MutedBrush");
            }

            pill.Click += (s, _) =>
            {
                _currentOptCategory = (s as Button)?.Tag?.ToString() ?? "todas";
                ShowOptimization();
            };
            categoryRow.Children.Add(pill);
        }
        root.Children.Add(categoryRow);

        // Filter items based on selected pill
        var filteredList = visibleOptimizations;
        if (!_currentOptCategory.Equals("todas", StringComparison.OrdinalIgnoreCase))
        {
            filteredList = visibleOptimizations
                .Where(o => o.Category.Equals(_currentOptCategory, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (filteredList.Count == 0)
        {
            var emptyBox = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(24),
                Margin = new Thickness(0, 10, 0, 0),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            emptyBox.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
            emptyBox.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var emptyTxt = new TextBlock
            {
                Text = "Nenhuma otimização encontrada para a categoria selecionada neste modo.",
                FontSize = 14,
                FontWeight = FontWeights.Medium,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            emptyTxt.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

            var emptySub = new TextBlock
            {
                Text = "Você pode ativar mais otimizações em 'Configurações' ou selecionar a categoria 'Todas'.",
                FontSize = 12,
                Margin = new Thickness(0, 6, 0, 14),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            emptySub.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

            var btn = new Button { Content = "Ver Todas as Otimizações", HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(14, 6, 14, 6) };
            btn.Click += (_, _) => { _currentOptCategory = "todas"; ShowOptimization(); };

            sp.Children.Add(emptyTxt);
            sp.Children.Add(emptySub);
            sp.Children.Add(btn);
            emptyBox.Child = sp;
            root.Children.Add(emptyBox);
        }
        else
        {
            // 3. Optimization Cards Grid with FIXED BUTTON LAYOUT
            var panel = new StackPanel();
            foreach (var opt in filteredList)
            {
                var card = new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(18, 16, 18, 16),
                    Margin = new Thickness(0, 0, 16, 16)
                };
                card.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
                card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

                var cardGrid = new Grid();
                cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var leftStack = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };

                var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
                var icon = new TextBlock { Text = "", FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
                var titleText = new TextBlock
                {
                    Text = opt.Name,
                    FontSize = 14.5,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                };
                titleText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

                titleRow.Children.Add(icon);
                titleRow.Children.Add(titleText);

                var catBadge = new Border
                {
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(7, 2, 7, 2),
                    Margin = new Thickness(10, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                catBadge.SetResourceReference(Border.BackgroundProperty, "PanelHoverBrush");
                var catText = new TextBlock { Text = opt.Category, FontSize = 10, FontWeight = FontWeights.SemiBold };
                catText.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                catBadge.Child = catText;
                titleRow.Children.Add(catBadge);

                var descText = new TextBlock
                {
                    Text = opt.Description,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    LineHeight = 17,
                    Margin = new Thickness(0, 6, 0, 0)
                };
                descText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

                leftStack.Children.Add(titleRow);
                leftStack.Children.Add(descText);
                Grid.SetColumn(leftStack, 0);
                cardGrid.Children.Add(leftStack);

                var execBtn = ActionButton("Revisar", opt.Operation);
                execBtn.VerticalAlignment = VerticalAlignment.Center;
                execBtn.Padding = new Thickness(16, 8, 16, 8);
                execBtn.FontWeight = FontWeights.SemiBold;
                execBtn.Margin = new Thickness(0);
                Grid.SetColumn(execBtn, 1);
                cardGrid.Children.Add(execBtn);

                card.Child = cardGrid;
                panel.Children.Add(card);
            }
            root.Children.Add(panel);
        }

        ContentHost.Children.Clear();
        ContentHost.Children.Add(root);
    }
    #endregion

    #region Settings Page
    private void ShowSettings()
    {
        PageTitle.Text = "Configurações";
        PageBadge.Visibility = Visibility.Collapsed;
        var root = new StackPanel { MaxWidth = 980, HorizontalAlignment = HorizontalAlignment.Left };

        // 1. Profile Manager Header
        var profileSection = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(22),
            Margin = new Thickness(0, 0, 0, 24),
            BorderThickness = new Thickness(1)
        };
        profileSection.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        profileSection.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var profileStack = new StackPanel();

        var secTitle = new TextBlock
        {
            Text = "Modos & Perfis de Visualização",
            FontSize = 17.5,
            FontWeight = FontWeights.Bold
        };
        secTitle.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

        var secDesc = new TextBlock
        {
            Text = "Personalize quais otimizações deseja visualizar no aplicativo. Você pode alternar entre os perfis prontos ou salvar seus próprios modos personalizados.",
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 18)
        };
        secDesc.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        profileStack.Children.Add(secTitle);
        profileStack.Children.Add(secDesc);

        // Profile selector row
        var selectorRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
        var selectLabel = new TextBlock
        {
            Text = "Perfil Selecionado:",
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 8)
        };
        selectLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        selectorRow.Children.Add(selectLabel);

        var profileCombo = new ComboBox
        {
            Width = 260,
            Margin = new Thickness(0, 0, 12, 8)
        };

        var allProfiles = _configService.Config.Profiles;
        foreach (var p in allProfiles)
        {
            profileCombo.Items.Add(p.Name + (p.Name == _configService.Config.ActiveProfile ? " (Ativo)" : ""));
        }

        var activeIndex = allProfiles.FindIndex(p => p.Name.Equals(_configService.Config.ActiveProfile, StringComparison.OrdinalIgnoreCase));
        profileCombo.SelectedIndex = activeIndex >= 0 ? activeIndex : 0;
        selectorRow.Children.Add(profileCombo);

        var applyBtn = new Button
        {
            Content = " Ativar Este Perfil",
            Margin = new Thickness(0, 0, 8, 8),
            FontWeight = FontWeights.SemiBold
        };
        selectorRow.Children.Add(applyBtn);

        var deleteBtn = new Button
        {
            Content = " Excluir Perfil",
            Margin = new Thickness(0, 0, 8, 8)
        };
        selectorRow.Children.Add(deleteBtn);

        profileStack.Children.Add(selectorRow);

        // Checklist of optimizations
        var checkListBorder = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 18)
        };
        checkListBorder.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        checkListBorder.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var checkListStack = new StackPanel();

        var checkHeaderDock = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var checkListTitle = new TextBlock
        {
            Text = "Otimizações exibidas neste modo:",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        checkListTitle.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        DockPanel.SetDock(checkListTitle, Dock.Left);
        checkHeaderDock.Children.Add(checkListTitle);

        var quickBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var selectAllBtn = new Button { Content = "Marcar Todas", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0), FontSize = 11.5 };
        var deselectAllBtn = new Button { Content = "Desmarcar Todas", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0), FontSize = 11.5 };
        quickBtns.Children.Add(selectAllBtn);
        quickBtns.Children.Add(deselectAllBtn);
        DockPanel.SetDock(quickBtns, Dock.Right);
        checkHeaderDock.Children.Add(quickBtns);
        checkListStack.Children.Add(checkHeaderDock);

        // Populate checkboxes
        var checkBoxes = new Dictionary<string, CheckBox>();
        var currentProfile = _configService.GetActiveProfile();
        var selectedProfile = (profileCombo.SelectedIndex >= 0 && profileCombo.SelectedIndex < allProfiles.Count)
            ? allProfiles[profileCombo.SelectedIndex]
            : currentProfile;

        var optGrid = new UniformGrid { Columns = 2 };
        foreach (var opt in ConfigService.AllOptimizations)
        {
            var cb = new CheckBox
            {
                IsChecked = selectedProfile.EnabledOptimizations.Contains(opt.Id, StringComparer.OrdinalIgnoreCase),
                Margin = new Thickness(0, 6, 12, 6)
            };

            var cbContent = new StackPanel();
            var cbTitle = new TextBlock { Text = $"{opt.Icon} {opt.Name}", FontSize = 13, FontWeight = FontWeights.Medium };
            cbTitle.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            var cbDesc = new TextBlock { Text = opt.Description, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            cbDesc.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

            cbContent.Children.Add(cbTitle);
            cbContent.Children.Add(cbDesc);
            cb.Content = cbContent;

            checkBoxes[opt.Id] = cb;
            optGrid.Children.Add(cb);
        }
        checkListStack.Children.Add(optGrid);
        checkListBorder.Child = checkListStack;
        profileStack.Children.Add(checkListBorder);

        // Save current changes or Save as new profile row
        var saveRow = new WrapPanel();
        var saveCurrentBtn = new Button
        {
            Content = " Salvar Alterações no Perfil",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 16, 8)
        };
        saveRow.Children.Add(saveCurrentBtn);

        var newProfileBox = new TextBox
        {
            Width = 190,
            Margin = new Thickness(0, 0, 8, 8),
            ToolTip = "Digite o nome do novo perfil"
        };
        saveRow.Children.Add(newProfileBox);

        var saveAsNewBtn = new Button
        {
            Content = "+ Salvar Como Novo Modo",
            Margin = new Thickness(0, 0, 8, 8)
        };
        saveRow.Children.Add(saveAsNewBtn);

        profileStack.Children.Add(saveRow);

        // Actions wiring
        profileCombo.SelectionChanged += (_, _) =>
        {
            if (profileCombo.SelectedIndex >= 0 && profileCombo.SelectedIndex < _configService.Config.Profiles.Count)
            {
                var prof = _configService.Config.Profiles[profileCombo.SelectedIndex];
                deleteBtn.IsEnabled = !prof.IsBuiltIn;
                foreach (var kv in checkBoxes)
                {
                    kv.Value.IsChecked = prof.EnabledOptimizations.Contains(kv.Key, StringComparer.OrdinalIgnoreCase);
                }
            }
        };

        deleteBtn.IsEnabled = !selectedProfile.IsBuiltIn;

        selectAllBtn.Click += (_, _) =>
        {
            foreach (var cb in checkBoxes.Values) cb.IsChecked = true;
        };

        deselectAllBtn.Click += (_, _) =>
        {
            foreach (var cb in checkBoxes.Values) cb.IsChecked = false;
        };

        applyBtn.Click += (_, _) =>
        {
            if (profileCombo.SelectedIndex >= 0 && profileCombo.SelectedIndex < _configService.Config.Profiles.Count)
            {
                var prof = _configService.Config.Profiles[profileCombo.SelectedIndex];
                _configService.SetActiveProfile(prof.Name);
                UpdateNavBadges();
        UpdateActiveNavButton(_currentPage);
                MessageBox.Show($"Perfil '{prof.Name}' ativado com sucesso!", "Perfil Ativado", MessageBoxButton.OK, MessageBoxImage.Information);
                ShowSettings();
            }
        };

        saveCurrentBtn.Click += (_, _) =>
        {
            if (profileCombo.SelectedIndex >= 0 && profileCombo.SelectedIndex < _configService.Config.Profiles.Count)
            {
                var prof = _configService.Config.Profiles[profileCombo.SelectedIndex];
                var enabled = checkBoxes.Where(c => c.Value.IsChecked == true).Select(c => c.Key).ToList();
                _configService.SaveProfile(prof.Name, prof.Description, enabled);
                UpdateNavBadges();
        UpdateActiveNavButton(_currentPage);
                MessageBox.Show($"Perfil '{prof.Name}' atualizado com sucesso!", "Salvo", MessageBoxButton.OK, MessageBoxImage.Information);
                ShowSettings();
            }
        };

        saveAsNewBtn.Click += (_, _) =>
        {
            var name = newProfileBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Por favor, digite um nome para o novo perfil.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var enabled = checkBoxes.Where(c => c.Value.IsChecked == true).Select(c => c.Key).ToList();
            _configService.SaveProfile(name, "Perfil personalizado do usuário", enabled);
            UpdateNavBadges();
        UpdateActiveNavButton(_currentPage);
            MessageBox.Show($"Novo perfil '{name}' criado e ativado com sucesso!", "Perfil Criado", MessageBoxButton.OK, MessageBoxImage.Information);
            ShowSettings();
        };

        deleteBtn.Click += (_, _) =>
        {
            if (profileCombo.SelectedIndex >= 0 && profileCombo.SelectedIndex < _configService.Config.Profiles.Count)
            {
                var prof = _configService.Config.Profiles[profileCombo.SelectedIndex];
                if (prof.IsBuiltIn)
                {
                    MessageBox.Show("Perfis padrão do sistema não podem ser excluídos.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var confirm = MessageBox.Show($"Deseja realmente excluir o perfil '{prof.Name}'?", "Confirmar Exclusão", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm == MessageBoxResult.Yes)
                {
                    _configService.DeleteProfile(prof.Name);
                    UpdateNavBadges();
        UpdateActiveNavButton(_currentPage);
                    MessageBox.Show($"Perfil '{prof.Name}' excluído.", "Excluído", MessageBoxButton.OK, MessageBoxImage.Information);
                    ShowSettings();
                }
            }
        };

        profileSection.Child = profileStack;
        root.Children.Add(profileSection);

        // 2. Appearance Section (Theme Selector)
        var themeSection = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(22),
            Margin = new Thickness(0, 0, 0, 24),
            BorderThickness = new Thickness(1)
        };
        themeSection.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        themeSection.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var themeStack = new StackPanel();
        var themeTitle = new TextBlock { Text = "Aparência & Tema", FontSize = 17.5, FontWeight = FontWeights.Bold };
        themeTitle.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

        var themeDesc = new TextBlock
        {
            Text = "Escolha o esquema de cores para o aplicativo. Todas as janelas e componentes se adaptam instantaneamente.",
            FontSize = 12.5,
            Margin = new Thickness(0, 4, 0, 16)
        };
        themeDesc.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        themeStack.Children.Add(themeTitle);
        themeStack.Children.Add(themeDesc);

        var themeBtns = new WrapPanel();
        var darkBtn = new Button
        {
            Content = "  Modo Escuro (Dark)",
            Padding = new Thickness(16, 9, 16, 9),
            FontWeight = _darkTheme ? FontWeights.Bold : FontWeights.Normal
        };
        darkBtn.Click += (_, _) => { ApplyTheme(true, saveConfig: true); ShowSettings(); };

        var lightBtn = new Button
        {
            Content = "  Modo Claro (Light)",
            Padding = new Thickness(16, 9, 16, 9),
            FontWeight = !_darkTheme ? FontWeights.Bold : FontWeights.Normal
        };
        lightBtn.Click += (_, _) => { ApplyTheme(false, saveConfig: true); ShowSettings(); };

        themeBtns.Children.Add(darkBtn);
        themeBtns.Children.Add(lightBtn);
        themeStack.Children.Add(themeBtns);

        themeSection.Child = themeStack;
        root.Children.Add(themeSection);

        // 3. Language Section (App-wide language switch)
        var langSection = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(22),
            Margin = new Thickness(0, 0, 0, 24),
            BorderThickness = new Thickness(1)
        };
        langSection.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        langSection.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var langStack = new StackPanel();
        var langTitle = new TextBlock
        {
            Text = " Language / Idioma",
            FontSize = 17.5,
            FontWeight = FontWeights.Bold
        };
        langTitle.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

        var langDesc = new TextBlock
        {
            Text = _loc.T(
                "Traduz todo o aplicativo (menus, páginas, drivers e mensagens) para o idioma selecionado.",
                "Translates the entire app (menus, pages, drivers and messages) to the selected language."),
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 16)
        };
        langDesc.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        langStack.Children.Add(langTitle);
        langStack.Children.Add(langDesc);

        var langBtns = new WrapPanel();
        var ptBtn = new Button
        {
            Content = "  Português (PT)",
            Padding = new Thickness(16, 9, 16, 9),
            FontWeight = !_loc.IsEnglish ? FontWeights.Bold : FontWeights.Normal
        };
        ptBtn.Click += (_, _) =>
        {
            if (_loc.IsEnglish)
            {
                _loc.SetLanguage("pt");
                _configService.SaveLanguage("pt");
                UpdateLanguageUi();
            }
            ShowSettings();
        };

        var enBtn = new Button
        {
            Content = "  English (EN)",
            Padding = new Thickness(16, 9, 16, 9),
            FontWeight = _loc.IsEnglish ? FontWeights.Bold : FontWeights.Normal
        };
        enBtn.Click += (_, _) =>
        {
            if (!_loc.IsEnglish)
            { 
                _loc.SetLanguage("en");
                _configService.SaveLanguage("en");
                UpdateLanguageUi();
            }
            ShowSettings();
        };

        langBtns.Children.Add(ptBtn);
        langBtns.Children.Add(enBtn);
        langStack.Children.Add(langBtns);

        langSection.Child = langStack;
        root.Children.Add(langSection);

        ContentHost.Children.Clear();
        ContentHost.Children.Add(root);
    }
    #endregion

    #region ISOs Page
    private void ShowIsos()
    {
        PageTitle.Text = "ISOs do Windows";
        PageBadge.Visibility = Visibility.Visible;
        PageBadgeText.Text = "Download & Gerenciamento";
        var root = new StackPanel();

        // 1. Prominent Download & Mount Banner
        var banner = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(22, 18, 22, 18),
            Margin = new Thickness(0, 0, 0, 20),
            BorderThickness = new Thickness(1)
        };
        banner.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        banner.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var bannerGrid = new Grid();
        bannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var bText = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        var bTitle = new TextBlock
        {
            Text = "Central de Instalação & Download do Windows 11",
            FontSize = 16.5,
            FontWeight = FontWeights.Bold
        };
        bTitle.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

        var bSub = new TextBlock
        {
            Text = "Qualquer usuário pode baixar a ISO oficial do Windows 11 diretamente dos servidores da Microsoft ou utilizar a versão personalizada Pedro Queiroz para máxima performance.",
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 18,
            Margin = new Thickness(0, 4, 0, 0)
        };
        bSub.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        bText.Children.Add(bTitle);
        bText.Children.Add(bSub);
        Grid.SetColumn(bText, 0);
        bannerGrid.Children.Add(bText);

        var addBtn = new Button
        {
            Content = "+ Adicionar Imagem ISO",
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(14, 8, 14, 8)
        };
        addBtn.Click += AddIso_Click;
        Grid.SetColumn(addBtn, 1);
        bannerGrid.Children.Add(addBtn);

        banner.Child = bannerGrid;
        root.Children.Add(banner);

        // 2. Official Windows 11 Card
        var officialCard = new Border
        {
            Width = 470,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20),
            Margin = new Thickness(0, 0, 18, 18)
        };
        officialCard.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        officialCard.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var offContent = new StackPanel();
        var offHead = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var offTitle = new TextBlock { Text = " Windows 11 Oficial (Microsoft)", FontSize = 16, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        offTitle.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        DockPanel.SetDock(offTitle, Dock.Left);
        offHead.Children.Add(offTitle);

        var offBadge = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(7, 2, 7, 2),
            VerticalAlignment = VerticalAlignment.Center
        };
        offBadge.SetResourceReference(Border.BackgroundProperty, "PanelHoverBrush");
        var offBadgeText = new TextBlock { Text = "Oficial 24H2", FontSize = 10.5, FontWeight = FontWeights.SemiBold };
        offBadgeText.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        offBadge.Child = offBadgeText;
        DockPanel.SetDock(offBadge, Dock.Right);
        offHead.Children.Add(offBadge);
        offContent.Children.Add(offHead);

        var offStatus = new TextBlock
        {
            Text = "● Disponível para Download Gratuito",
            Foreground = (Brush)FindResource("SuccessBrush"),
            FontSize = 11.5,
            FontWeight = FontWeights.Medium,
            Margin = new Thickness(0, 0, 0, 8)
        };
        offContent.Children.Add(offStatus);

        var offDesc = new TextBlock
        {
            Text = "Imagem original e limpa direto da Microsoft. Permite gerar pendrive bootável via Media Creation Tool ou baixar a imagem ISO oficial para instalação limpa.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 14)
        };
        offDesc.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        offContent.Children.Add(offDesc);

        var offBtns = new WrapPanel();
        var dlOfficialBtn = new Button
        {
            Content = "⬇ Baixar ISO Oficial",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 8, 8)
        };
        dlOfficialBtn.Click += (_, _) => Process.Start(new ProcessStartInfo("https://www.microsoft.com/software-download/windows11") { UseShellExecute = true });

        var dlToolBtn = new Button
        {
            Content = " Media Creation Tool",
            Margin = new Thickness(0, 0, 8, 8)
        };
        dlToolBtn.Click += (_, _) => Process.Start(new ProcessStartInfo("https://go.microsoft.com/fwlink/?linkid=2156295") { UseShellExecute = true });

        var copyOffLink = new Button
        {
            Content = " Copiar Link",
            Margin = new Thickness(0, 0, 8, 8)
        };
        copyOffLink.Click += (_, _) =>
        {
            Clipboard.SetText("https://www.microsoft.com/software-download/windows11");
            MessageBox.Show("Link oficial copiado para a área de transferência!", "Copiado", MessageBoxButton.OK, MessageBoxImage.Information);
        };

        offBtns.Children.Add(dlOfficialBtn);
        offBtns.Children.Add(dlToolBtn);
        offBtns.Children.Add(copyOffLink);
        offContent.Children.Add(offBtns);

        officialCard.Child = offContent;

        var panel = new WrapPanel();
        panel.Children.Add(officialCard);

        var isos = _configService.Config.IsoCatalog;
        foreach (var iso in isos)
        {
            var card = new Border
            {
                Width = 470,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(20),
                Margin = new Thickness(0, 0, 18, 18)
            };
            card.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

            var content = new StackPanel();

            var headerDock = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var isoTitle = new TextBlock
            {
                Text = " " + iso.Name,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            isoTitle.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            DockPanel.SetDock(isoTitle, Dock.Left);
            headerDock.Children.Add(isoTitle);

            var vBadge = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(7, 2, 7, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            vBadge.SetResourceReference(Border.BackgroundProperty, "PanelHoverBrush");
            var vt = new TextBlock { Text = string.IsNullOrWhiteSpace(iso.Version) ? "Personalizada" : iso.Version, FontSize = 10.5, FontWeight = FontWeights.SemiBold };
            vt.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            vBadge.Child = vt;
            DockPanel.SetDock(vBadge, Dock.Right);
            headerDock.Children.Add(vBadge);
            content.Children.Add(headerDock);

            var exists = !string.IsNullOrWhiteSpace(iso.LocalPath) && File.Exists(iso.LocalPath);
            string sizeStr = "";
            if (exists)
            {
                try
                {
                    var len = new FileInfo(iso.LocalPath).Length;
                    sizeStr = $" • {len / 1024.0 / 1024.0 / 1024.0:N2} GB";
                }
                catch { }
            }

            var statusText = new TextBlock
            {
                Text = exists ? $"● Disponível Localmente{sizeStr}" : "○ Imagem disponível para download / associação",
                Foreground = exists ? (Brush)FindResource("SuccessBrush") : (Brush)FindResource("MutedBrush"),
                FontSize = 11.5,
                FontWeight = FontWeights.Medium,
                Margin = new Thickness(0, 0, 0, 8)
            };
            content.Children.Add(statusText);

            if (!string.IsNullOrWhiteSpace(iso.Description))
            {
                var desc = new TextBlock
                {
                    Text = iso.Description,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    LineHeight = 17,
                    Margin = new Thickness(0, 0, 0, 10)
                };
                desc.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                content.Children.Add(desc);
            }

            if (!string.IsNullOrWhiteSpace(iso.LocalPath))
            {
                var pathBox = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(8, 5, 8, 5),
                    Margin = new Thickness(0, 0, 0, 12)
                };
                pathBox.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
                pathBox.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

                var pathText = new TextBlock
                {
                    Text = iso.LocalPath,
                    FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                pathText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                pathBox.Child = pathText;
                content.Children.Add(pathBox);
            }

            var btnRow = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };

            if (exists)
            {
                var mountBtn = new Button { Content = " Montar ISO", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 8) };
                mountBtn.Click += async (_, _) => await MountIsoAsync(iso.LocalPath);
                btnRow.Children.Add(mountBtn);

                var dismountBtn = new Button { Content = "⏏ Desmontar", Margin = new Thickness(0, 0, 8, 8) };
                dismountBtn.Click += async (_, _) => await DismountIsoAsync(iso.LocalPath);
                btnRow.Children.Add(dismountBtn);

                var folderBtn = new Button { Content = " Abrir Pasta", Margin = new Thickness(0, 0, 8, 8) };
                folderBtn.Click += (_, _) =>
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{iso.LocalPath}\"") { UseShellExecute = true });
                };
                btnRow.Children.Add(folderBtn);
            }
            else
            {
                var locateBtn = new Button { Content = " Localizar no Meu PC", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 8) };
                locateBtn.Click += (_, _) =>
                {
                    var dlg = new OpenFileDialog { Filter = "Imagens ISO (*.iso)|*.iso", Title = $"Localizar arquivo para {iso.Name}" };
                    if (dlg.ShowDialog() == true)
                    {
                        iso.LocalPath = dlg.FileName;
                        _configService.Save();
                        ShowIsos();
                    }
                };
                btnRow.Children.Add(locateBtn);
            }

            if (!string.IsNullOrWhiteSpace(iso.DownloadUrl))
            {
                var dlBtn = new Button { Content = "⬇ Baixar via Google Drive", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 8) };
                dlBtn.Click += (_, _) => Process.Start(new ProcessStartInfo(iso.DownloadUrl) { UseShellExecute = true });
                btnRow.Children.Add(dlBtn);
            }

            if (!string.IsNullOrWhiteSpace(iso.LocalPath))
            {
                var copyBtn = new Button { Content = " Copiar Caminho", Margin = new Thickness(0, 0, 8, 8) };
                copyBtn.Click += (_, _) =>
                {
                    Clipboard.SetText(iso.LocalPath);
                    MessageBox.Show("Caminho copiado para a área de transferência!", "Copiado", MessageBoxButton.OK, MessageBoxImage.Information);
                };
                btnRow.Children.Add(copyBtn);
            }

            content.Children.Add(btnRow);
            card.Child = content;
            panel.Children.Add(card);
        }

        root.Children.Add(panel);
        ContentHost.Children.Clear();
        ContentHost.Children.Add(root);
    }

    private void AddIso_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecionar Imagem ISO do Windows",
            Filter = "Imagens ISO (*.iso)|*.iso|Todos os Arquivos (*.*)|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\Downloads"
        };

        if (dialog.ShowDialog() == true)
        {
            var fileName = Path.GetFileNameWithoutExtension(dialog.FileName);
            var entry = new IsoEntry
            {
                Name = fileName,
                LocalPath = dialog.FileName,
                Version = "Custom",
                Architecture = "x64",
                Description = "Imagem ISO personalizada adicionada pelo usuário."
            };
            _configService.AddIso(entry);
            MessageBox.Show($"ISO '{fileName}' adicionada com sucesso!", "ISO Adicionada", MessageBoxButton.OK, MessageBoxImage.Information);
            ShowIsos();
        }
    }

    private async Task MountIsoAsync(string isoPath)
    {
        if (!File.Exists(isoPath))
        {
            MessageBox.Show($"O arquivo ISO não foi localizado:\n{isoPath}", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            var script = $"$img = Mount-DiskImage -ImagePath '{isoPath.Replace("'", "''")}' -PassThru; $vol = ($img | Get-Volume); if ($vol) {{ $vol.DriveLetter }}";
            var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -WindowStyle Hidden -Command \"{script}\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            var driveLetter = (await p!.StandardOutput.ReadToEndAsync()).Trim();
            await p.WaitForExitAsync();

            if (!string.IsNullOrWhiteSpace(driveLetter))
            {
                var path = $"{driveLetter}:\\";
                Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
                MessageBox.Show($"A imagem ISO foi montada na unidade {path} e aberta no Explorador de Arquivos.", "ISO Montada", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("A imagem ISO foi montada com sucesso. Acesse 'Este Computador' para visualizar a unidade.", "ISO Montada", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Falha ao montar ISO: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private async Task DismountIsoAsync(string isoPath)
    {
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            var script = $"Dismount-DiskImage -ImagePath '{isoPath.Replace("'", "''")}'";
            var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -WindowStyle Hidden -Command \"{script}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            await p!.WaitForExitAsync();
            MessageBox.Show("A imagem ISO foi desmontada com sucesso.", "ISO Desmontada", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Falha ao desmontar ISO: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }
    #endregion

    #region Drivers Page
    private void ShowDrivers()
    {
        PageTitle.Text = "Drivers & Utilitários";
        PageBadge.Visibility = Visibility.Collapsed;

        var allDrivers = _driverService.GetAllDrivers(_snapshot, _loc.IsEnglish);
        var root = new StackPanel();

        // Subtitle & GPU Detection Header
        var subHeader = new TextBlock
        {
            Text = !string.IsNullOrEmpty(_snapshot?.Graphics)
                ? $"Placa de vídeo detectada: {_snapshot.Graphics} • Todos os drivers com links oficiais e opção de download."
                : "Baixe e atualize os drivers essenciais de vídeo, chipset, rede e áudio para máxima taxa de quadros e menor latência.",
            FontSize = 12.5,
            Margin = new Thickness(0, 0, 0, 14)
        };
        subHeader.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        root.Children.Add(subHeader);

        // Top Filter & Search Bar
        var filterBar = new DockPanel { Margin = new Thickness(0, 0, 0, 16), LastChildFill = true };

        // Search Box (Right aligned)
        var searchPanel = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(searchPanel, Dock.Right);
        var searchBox = new TextBox
        {
            Width = 240,
            Height = 32,
            Padding = new Thickness(10, 4, 10, 4),
            FontSize = 12,
            Text = _driverSearch,
            ToolTip = "Filtrar drivers por nome, fabricante ou categoria"
        };
        searchPanel.Children.Add(new TextBlock
        {
            Text = "",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            FontSize = 13
        });
        searchPanel.Children.Add(searchBox);
        filterBar.Children.Add(searchPanel);

        // Categories Pills
        var categoriesPanel = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        var cats = new[]
        {
            ("Todos", $"Todos ({allDrivers.Count})"),
            ("GPU", $"Placas de Vídeo ({allDrivers.Count(d => d.Category == "GPU")})"),
            ("Chipset", $"Chipset & CPU ({allDrivers.Count(d => d.Category == "Chipset")})"),
            ("Áudio", $"Áudio ({allDrivers.Count(d => d.Category == "Áudio")})"),
            ("Rede", $"Rede ({allDrivers.Count(d => d.Category == "Rede")})"),
            ("Utilitários", $"Utilitários ({allDrivers.Count(d => d.Category == "Utilitários")})")
        };

        foreach (var (catKey, catLabel) in cats)
        {
            var isSelected = _driverCategory.Equals(catKey, StringComparison.OrdinalIgnoreCase);
            var catBtn = new Button
            {
                Content = catLabel,
                FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Medium,
                Margin = new Thickness(0, 0, 6, 4),
                Padding = new Thickness(10, 5, 10, 5),
                FontSize = 12
            };

            if (isSelected)
            {
                catBtn.SetResourceReference(Button.BackgroundProperty, "PanelHoverBrush");
                catBtn.SetResourceReference(Button.BorderBrushProperty, "AccentBrush");
                catBtn.SetResourceReference(Button.ForegroundProperty, "TextBrush");
            }
            else
            {
                catBtn.Background = Brushes.Transparent;
                catBtn.SetResourceReference(Button.BorderBrushProperty, "BorderBrush");
                catBtn.SetResourceReference(Button.ForegroundProperty, "MutedBrush");
            }

            catBtn.Click += (_, _) =>
            {
                _driverCategory = catKey;
                ShowDrivers();
            };
            categoriesPanel.Children.Add(catBtn);
        }
        filterBar.Children.Add(categoriesPanel);
        root.Children.Add(filterBar);

        // Host for Cards
        var cardsWrap = new WrapPanel();
        root.Children.Add(cardsWrap);

        searchBox.TextChanged += (_, _) =>
        {
            _driverSearch = searchBox.Text.Trim();
            RenderDriverCards(cardsWrap, allDrivers);
        };

        RenderDriverCards(cardsWrap, allDrivers);

        ContentHost.Children.Clear();
        ContentHost.Children.Add(root);
    }

    private void RenderDriverCards(WrapPanel cardsWrap, List<DriverInfo> allDrivers)
    {
        cardsWrap.Children.Clear();

        var filtered = allDrivers.Where(d =>
        {
            if (!_driverCategory.Equals("Todos", StringComparison.OrdinalIgnoreCase) &&
                !d.Category.Contains(_driverCategory, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(_driverSearch))
            {
                var query = _driverSearch.ToLowerInvariant();
                return d.Name.ToLowerInvariant().Contains(query) ||
                       d.Vendor.ToLowerInvariant().Contains(query) ||
                       d.Description.ToLowerInvariant().Contains(query) ||
                       d.Category.ToLowerInvariant().Contains(query);
            }

            return true;
        }).ToList();

        if (filtered.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = "Nenhum driver encontrado para os filtros selecionados.",
                FontSize = 13,
                Margin = new Thickness(10, 20, 0, 0)
            };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            cardsWrap.Children.Add(empty);
            return;
        }

        foreach (var driver in filtered)
        {
            var card = new Border
            {
                Width = 490,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(18),
                Margin = new Thickness(0, 0, 16, 16)
            };
            card.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

            var cardContent = new StackPanel();

            // Top Badges Row
            var badgesRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };

            // Vendor Badge
            var vendorColor = driver.Vendor switch
            {
                var v when v.Contains("NVIDIA") => "#10B981",
                var v when v.Contains("AMD") => "#EF4444",
                var v when v.Contains("Intel") => "#3B82F6",
                var v when v.Contains("Realtek") => "#8B5CF6",
                _ => "#648CFF"
            };

            var vendorBadge = new Border
            {
                Background = (Brush)new BrushConverter().ConvertFromString(vendorColor)!,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 6, 0)
            };
            vendorBadge.Child = new TextBlock
            {
                Text = driver.Vendor.ToUpperInvariant(),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            };
            badgesRow.Children.Add(vendorBadge);

            // Category Badge
            var catBadge = new Border
            {
                Background = (Brush)FindResource("PanelHoverBrush"),
                BorderBrush = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 6, 0)
            };
            catBadge.Child = new TextBlock
            {
                Text = driver.Category,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("MutedBrush")
            };
            badgesRow.Children.Add(catBadge);

            // Recommended Badge
            if (driver.IsRecommendedForCurrentHardware)
            {
                var recBadge = new Border
                {
                    Background = (Brush)FindResource("PanelHoverBrush"),
                    BorderBrush = (Brush)FindResource("AccentBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2, 6, 2)
                };
                recBadge.Child = new TextBlock
                {
                    Text = "⭐ Recomendado para o seu PC",
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = (Brush)FindResource("AccentBrush")
                };
                badgesRow.Children.Add(recBadge);
            }

            cardContent.Children.Add(badgesRow);

            // Title & Version
            var title = new TextBlock
            {
                Text = driver.Name,
                FontSize = 15.5,
                FontWeight = FontWeights.SemiBold
            };
            title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            cardContent.Children.Add(title);

            var ver = new TextBlock
            {
                Text = driver.Version,
                FontSize = 11.5,
                Margin = new Thickness(0, 2, 0, 6)
            };
            ver.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            cardContent.Children.Add(ver);

            // Description
            var desc = new TextBlock
            {
                Text = driver.Description,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                LineHeight = 16.5,
                Margin = new Thickness(0, 0, 0, 10)
            };
            desc.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            cardContent.Children.Add(desc);

            // Existing local file check
            var existingFile = _driverService.GetExistingInstallerPath(driver);
            bool isDownloaded = !string.IsNullOrEmpty(existingFile);

            var status = new TextBlock
            {
                Text = isDownloaded
                    ? $"● Instalador disponível: {Path.GetFileName(existingFile)}"
                    : "○ Disponível para download oficial",
                Foreground = isDownloaded ? (Brush)FindResource("SuccessBrush") : (Brush)FindResource("MutedBrush"),
                FontSize = 11.5,
                Margin = new Thickness(0, 0, 0, 10),
                TextWrapping = TextWrapping.Wrap
            };
            cardContent.Children.Add(status);

            // Progress Bar for direct download
            var pbar = new ProgressBar
            {
                Height = 4,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 0, 0, 10)
            };
            cardContent.Children.Add(pbar);

            // Action Buttons
            var btnRow = new WrapPanel();

            // Official Download Button
            var officialBtn = new Button
            {
                Content = "⬇ Baixar (Site Oficial)",
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(12, 6, 12, 6),
                FontSize = 12
            };
            officialBtn.Click += (_, _) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo(driver.OfficialDownloadUrl) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Falha ao abrir navegador: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            btnRow.Children.Add(officialBtn);

            // Secondary Download Button (ex.: Google Drive do autor)
            if (!string.IsNullOrEmpty(driver.SecondaryDownloadUrl))
            {
                var secondaryBtn = new Button
                {
                    Content = $"⬇ {driver.SecondaryDownloadLabel ?? "Download Alternativo"}",
                    Padding = new Thickness(12, 6, 12, 6),
                    FontSize = 12
                };
                secondaryBtn.Click += (_, _) =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(driver.SecondaryDownloadUrl!) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Falha ao abrir navegador: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };
                btnRow.Children.Add(secondaryBtn);
            }

            // Direct Download Button (if direct URL available)
            if (!string.IsNullOrEmpty(driver.DirectDownloadUrl))
            {
                var directBtn = new Button
                {
                    Content = " Download Direto",
                    Padding = new Thickness(12, 6, 12, 6),
                    FontSize = 12
                };
                directBtn.Click += async (_, _) =>
                {
                    directBtn.IsEnabled = false;
                    officialBtn.IsEnabled = false;
                    pbar.Visibility = Visibility.Visible;
                    pbar.IsIndeterminate = false;
                    pbar.Value = 0;
                    status.Text = "Iniciando download...";
                    status.Foreground = (Brush)FindResource("AccentBrush");

                    var progress = new Progress<(long read, long total)>(p =>
                    {
                        if (p.total > 0)
                        {
                            var pct = (double)p.read / p.total * 100.0;
                            pbar.Value = pct;
                            status.Text = $"Baixando... {p.read / 1024d / 1024d:N1} MB / {p.total / 1024d / 1024d:N1} MB ({pct:N0}%)";
                        }
                        else
                        {
                            status.Text = $"Baixando... {p.read / 1024d / 1024d:N1} MB";
                        }
                    });

                    try
                    {
                        var downloadedPath = await _driverService.DownloadDirectAsync(driver, progress);
                        pbar.Visibility = Visibility.Collapsed;
                        status.Text = $"● Concluído! Salvo em: {Path.GetFileName(downloadedPath)}";
                        status.Foreground = (Brush)FindResource("SuccessBrush");

                        var res = MessageBox.Show(
                            $"Download de '{driver.Name}' concluído com sucesso!\n\nSalvo em:\n{downloadedPath}\n\nDeseja executar o instalador agora?",
                            "Download Concluído",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Information
                        );
                        if (res == MessageBoxResult.Yes)
                        {
                            Process.Start(new ProcessStartInfo(downloadedPath) { UseShellExecute = true });
                        }
                        ShowDrivers();
                    }
                    catch
                    {
                        pbar.Visibility = Visibility.Collapsed;
                        status.Text = $"Falha no download direto. Abrindo página oficial no navegador...";
                        status.Foreground = (Brush)FindResource("WarningBrush");
                        Process.Start(new ProcessStartInfo(driver.OfficialDownloadUrl) { UseShellExecute = true });
                    }
                    finally
                    {
                        directBtn.IsEnabled = true;
                        officialBtn.IsEnabled = true;
                    }
                };
                btnRow.Children.Add(directBtn);
            }

            // Run Installer Button (if file exists)
            if (isDownloaded)
            {
                var runBtn = new Button
                {
                    Content = "▶ Executar Instalador",
                    FontWeight = FontWeights.SemiBold,
                    Padding = new Thickness(12, 6, 12, 6),
                    FontSize = 12
                };
                runBtn.Click += (_, _) =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(existingFile!) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Falha ao iniciar o instalador: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };
                btnRow.Children.Add(runBtn);

                var folderBtn = new Button
                {
                    Content = " Pasta",
                    Padding = new Thickness(10, 6, 10, 6),
                    FontSize = 12
                };
                folderBtn.Click += (_, _) =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{existingFile}\"") { UseShellExecute = true });
                    }
                    catch { }
                };
                btnRow.Children.Add(folderBtn);
            }

            cardContent.Children.Add(btnRow);
            card.Child = cardContent;
            cardsWrap.Children.Add(card);
        }
    }
    #endregion

    #region Tools Page (Including "Barra de tarefas all black")
    private void ShowTools()
    {
        PageTitle.Text = "Ferramentas";
        PageBadge.Visibility = Visibility.Collapsed;
        var panel = new WrapPanel();

        // 1. Card: "Barra de tarefas all black" (Command requested by User)
        var isAbActive = IsAllBlackTaskbarActive();
        var allBlackPanel = new StackPanel { Width = 440 };

        var abHeaderRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        var abTitle = new TextBlock { Text = "⬛  Barra de Tarefas All Black", FontSize = 17, FontWeight = FontWeights.SemiBold };
        abTitle.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        abHeaderRow.Children.Add(abTitle);

        var abBadge = new Border
        {
            Background = isAbActive ? (Brush)FindResource("PanelHoverBrush") : Brushes.Transparent,
            BorderBrush = isAbActive ? (Brush)FindResource("SuccessBrush") : (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7, 2, 7, 2),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var abBadgeText = new TextBlock
        {
            Text = isAbActive ? "ATIVO" : "PADRÃO",
            FontSize = 10.5,
            FontWeight = FontWeights.Bold,
            Foreground = isAbActive ? (Brush)FindResource("SuccessBrush") : (Brush)FindResource("MutedBrush")
        };
        abBadge.Child = abBadgeText;
        abHeaderRow.Children.Add(abBadge);

        var abDesc = new TextBlock
        {
            Text = "Aplica a paleta de acentuação customizada e desativa a transparência, deixando a barra de tarefas do Windows em tom preto puro absoluto (#000000) no Explorer.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            LineHeight = 17,
            Margin = new Thickness(0, 4, 0, 10)
        };
        abDesc.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        var abStatus = new TextBlock
        {
            Text = isAbActive ? "● Barra de tarefas 100% preta (All Black) ativa no sistema" : "○ Visual padrão de tema do Windows ativo",
            Foreground = isAbActive ? (Brush)FindResource("SuccessBrush") : (Brush)FindResource("MutedBrush"),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 14)
        };

        var abBtns = new WrapPanel();
        var applyAbBtn = new Button
        {
            Content = " Aplicar All Black",
            FontWeight = FontWeights.SemiBold
        };
        applyAbBtn.Click += async (_, _) => await ApplyAllBlackTaskbarAsync(enable: true);

        var revertAbBtn = new Button
        {
            Content = "Restaurar Padrão"
        };
        revertAbBtn.Click += async (_, _) => await ApplyAllBlackTaskbarAsync(enable: false);

        var copyAbBtn = new Button
        {
            Content = " Copiar Comando"
        };
        copyAbBtn.Click += (_, _) =>
        {
            const string cmd = @"$p = [byte[]](0xB0,0xB2,0xB4,0xFF,0xCC,0xCE,0xD0,0xFF,0x00,0x00,0x00,0xFF,0x90,0x92,0x94,0xFF,0x00,0x00,0x00,0xFF,0x00,0x00,0x00,0xFF,0x00,0x00,0x00,0xFF,0x00,0x00,0x00,0xFF); Set-ItemProperty -Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Accent' -Name 'AccentPalette' -Value $p; Set-ItemProperty -Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Accent' -Name 'AccentColorMenu' -Type DWord -Value 0xFF000000; Set-ItemProperty -Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Accent' -Name 'StartColorMenu' -Type DWord -Value 0xFF000000; Set-ItemProperty -Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize' -Name 'ColorPrevalence' -Type DWord -Value 1; Set-ItemProperty -Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize' -Name 'EnableTransparency' -Type DWord -Value 0";
            Clipboard.SetText(cmd);
            MessageBox.Show("Comando PowerShell copiado com sucesso para a área de transferência!", "All Black Taskbar", MessageBoxButton.OK, MessageBoxImage.Information);
        };

        abBtns.Children.Add(applyAbBtn);
        abBtns.Children.Add(revertAbBtn);
        abBtns.Children.Add(copyAbBtn);

        allBlackPanel.Children.Add(abHeaderRow);
        allBlackPanel.Children.Add(abDesc);
        allBlackPanel.Children.Add(abStatus);
        allBlackPanel.Children.Add(abBtns);

        var abCard = new Border
        {
            Width = 470,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20),
            Margin = new Thickness(0, 0, 16, 16),
            Child = allBlackPanel
        };
        abCard.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        abCard.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        panel.Children.Add(abCard);

        // 2. Quick clean card (Fixed shortcuts)
        var quickClean = new StackPanel { Width = 430 };
        var qcTitle = new TextBlock { Text = "  Limpeza Rápida de Cache", FontSize = 17, FontWeight = FontWeights.SemiBold };
        qcTitle.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

        var qcDesc = new TextBlock
        {
            Text = "Cria atalhos na Área de Trabalho e no Menu Iniciar para limpar arquivos temporários com um clique.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            Margin = new Thickness(0, 8, 0, 12)
        };
        qcDesc.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        var state = new TextBlock
        {
            Text = _cleaner.IsShortcutConfigured() ? "● Atalhos configurados (Área de Trabalho & Menu Iniciar)" : "○ Atalhos não configurados",
            Foreground = _cleaner.IsShortcutConfigured() ? (Brush)FindResource("SuccessBrush") : (Brush)FindResource("MutedBrush"),
            Margin = new Thickness(0, 0, 0, 14)
        };

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
        var create = new Button
        {
            Content = _cleaner.IsShortcutConfigured() ? "Remover atalhos" : "Criar atalho de limpeza",
            Tag = _cleaner.IsShortcutConfigured(),
            FontWeight = FontWeights.SemiBold
        };
        create.Click += (_, _) =>
        {
            if ((bool)create.Tag)
            {
                _cleaner.RemoveShortcut();
                MessageBox.Show("Atalhos de Limpeza Rápida removidos com sucesso.", "Limpeza Rápida", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                _cleaner.CreateShortcut();
                MessageBox.Show("Atalho 'Limpeza Rápida' criado com sucesso na sua Área de Trabalho e no Menu Iniciar!\n\nVocê também pode clicar com o botão direito nele e selecionar 'Fixar na barra de tarefas'.", "Atalho Criado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            ShowTools();
        };

        var test = new Button { Content = "Executar Limpeza Agora", FontWeight = FontWeights.SemiBold };
        test.Click += async (_, _) => await PrepareOperationAsync("quickclean");

        btnRow.Children.Add(create);
        btnRow.Children.Add(test);

        quickClean.Children.Add(qcTitle);
        quickClean.Children.Add(qcDesc);
        quickClean.Children.Add(state);
        quickClean.Children.Add(btnRow);

        var qcCard = new Border
        {
            Width = 470,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20),
            Margin = new Thickness(0, 0, 16, 16),
            Child = quickClean
        };
        qcCard.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        qcCard.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        panel.Children.Add(qcCard);

        // 3. Windows Native Tools
        var tools = new[]
        {
            ("Gerenciador de Tarefas", "taskmgr.exe"),
            ("Gerenciador de Dispositivos", "devmgmt.msc"),
            ("Serviços do Windows", "services.msc"),
            ("Configurações do Sistema", "ms-settings:"),
            ("Prompt de Comando (CMD)", "cmd.exe"),
            ("PowerShell", "powershell.exe"),
            ("Editor do Registro (Regedit)", "regedit.exe"),
            ("Informações do Sistema", "msinfo32.exe"),
            ("Gerenciamento de Disco", "diskmgmt.msc"),
            ("Visualizador de Eventos", "eventvwr.msc")
        };

        foreach (var tool in tools)
        {
            var button = new Button { Content = "Abrir", Tag = tool.Item2, HorizontalAlignment = HorizontalAlignment.Left };
            button.Click += (_, _) => Process.Start(new ProcessStartInfo(tool.Item2) { UseShellExecute = true });

            var cardContent = new StackPanel();
            var title = new TextBlock { Text = tool.Item1, FontSize = 14.5, FontWeight = FontWeights.SemiBold };
            title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

            var sub = new TextBlock { Text = "Utilitário nativo do Windows", FontSize = 11.5, Margin = new Thickness(0, 4, 0, 12) };
            sub.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

            cardContent.Children.Add(title);
            cardContent.Children.Add(sub);
            cardContent.Children.Add(button);

            var toolCard = new Border
            {
                Width = 220,
                Height = 140,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 12, 12),
                Child = cardContent
            };
            toolCard.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
            toolCard.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            panel.Children.Add(toolCard);
        }

        ContentHost.Children.Clear();
        ContentHost.Children.Add(panel);
    }

    private bool IsAllBlackTaskbarActive()
    {
        try
        {
            using var keyThemes = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            using var keyAccent = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Accent");
            var colorPrev = keyThemes?.GetValue("ColorPrevalence");
            var enableTrans = keyThemes?.GetValue("EnableTransparency");
            var accentMenu = keyAccent?.GetValue("AccentColorMenu");

            return (colorPrev is int cp && cp == 1) &&
                   (enableTrans is int et && et == 0) &&
                   (accentMenu != null);
        }
        catch
        {
            return false;
        }
    }

    private async Task ApplyAllBlackTaskbarAsync(bool enable)
    {
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            string ps;
            if (enable)
            {
                // Exact command provided by user + explorer restart
                ps = @"
if (-not (Test-Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Accent')) {
    New-Item -Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Accent' -Force | Out-Null
}
$p = [byte[]](0xB0,0xB2,0xB4,0xFF,0xCC,0xCE,0xD0,0xFF,0x00,0x00,0x00,0xFF,0x90,0x92,0x94,0xFF,0x00,0x00,0x00,0xFF,0x00,0x00,0x00,0xFF,0x00,0x00,0x00,0xFF,0x00,0x00,0x00,0xFF);
Set-ItemProperty -Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Accent' -Name 'AccentPalette' -Value $p -Force;
Set-ItemProperty -Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Accent' -Name 'AccentColorMenu' -Type DWord -Value 0xFF000000 -Force;
Set-ItemProperty -Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Accent' -Name 'StartColorMenu' -Type DWord -Value 0xFF000000 -Force;
Set-ItemProperty -Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize' -Name 'ColorPrevalence' -Type DWord -Value 1 -Force;
Set-ItemProperty -Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize' -Name 'EnableTransparency' -Type DWord -Value 0 -Force;
Stop-Process -Name explorer -Force
";
            }
            else
            {
                ps = @"
Set-ItemProperty -Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize' -Name 'ColorPrevalence' -Type DWord -Value 0 -Force;
Set-ItemProperty -Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize' -Name 'EnableTransparency' -Type DWord -Value 1 -Force;
Remove-ItemProperty -Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Accent' -Name 'AccentPalette' -ErrorAction SilentlyContinue;
Remove-ItemProperty -Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Accent' -Name 'AccentColorMenu' -ErrorAction SilentlyContinue;
Remove-ItemProperty -Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Accent' -Name 'StartColorMenu' -ErrorAction SilentlyContinue;
Stop-Process -Name explorer -Force
";
            }

            var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command \"{ps}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p != null) await p.WaitForExitAsync();

            MessageBox.Show(
                enable 
                    ? "Barra de tarefas All Black aplicada com sucesso!\nO Windows Explorer foi reiniciado para atualizar o visual." 
                    : "Barra de tarefas padrão restaurada com sucesso!\nO Windows Explorer foi reiniciado.",
                "Barra de Tarefas All Black",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );

            ShowTools();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Falha ao aplicar alteração: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }
    #endregion

    #region About Page
    private const string AuthorSiteUrl = "https://pqueiroz.vercel.app/";

    private void ShowAbout()
    {
        PageTitle.Text = _loc.T("Sobre", "About");
        PageBadge.Visibility = Visibility.Collapsed;
        var root = new StackPanel { MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };

        var aboutCard = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(24),
            BorderThickness = new Thickness(1)
        };
        aboutCard.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        aboutCard.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var content = new StackPanel();

        var appTitle = new TextBlock
        {
            Text = "PQueiroz Optimizer " + AppVersion,
            FontSize = 22,
            FontWeight = FontWeights.Bold
        };
        appTitle.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");

        var desc = new TextBlock
        {
            Text = _loc.T(
                "Software profissional de otimização, manutenção e gerenciamento de imagens do Windows. Projetado com interface limpa, controle de modos e perfis personalizáveis, troca instantânea de tema e repositório integrado de ISOs.\n\nDesenvolvido por Pedro Queiroz.",
                "Professional software for Windows optimization, maintenance and image management. Designed with a clean interface, customizable modes and profiles, instant theme switching and an integrated ISO repository.\n\nDeveloped by Pedro Queiroz."),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13.5,
            LineHeight = 22,
            Margin = new Thickness(0, 12, 0, 18)
        };
        desc.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

        var disclaimer = new TextBlock
        {
            Text = _loc.T(
                "Segurança em primeiro lugar: Os ajustes possuem backups de configuração. Remoções de arquivos e aplicativos não são desfeitas pelo snapshot; consulte os detalhes antes de aplicar.",
                "Safety first: Configuration changes have backups. File and application removal cannot be undone by a snapshot; review the details before applying."),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };
        disclaimer.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        content.Children.Add(appTitle);
        content.Children.Add(desc);
        content.Children.Add(disclaimer);

        aboutCard.Child = content;
        root.Children.Add(aboutCard);

        // Promotion card (author site)
        var promoCard = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(24),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 20, 0, 0)
        };
        promoCard.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        promoCard.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");

        var promoStack = new StackPanel();

        var promoTitle = new TextBlock
        {
            Text = " " + _loc.T("Divulgação", "Promotion"),
            FontSize = 16,
            FontWeight = FontWeights.Bold
        };
        promoTitle.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");

        var promoMsg = new TextBlock
        {
            Text = _loc.T(
                "Se gostou do app e gostaria de solicitar um serviço ou automação, só entrar no link:",
                "If you liked the app and would like to request a service or automation, just visit the link:"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            LineHeight = 20,
            Margin = new Thickness(0, 8, 0, 6)
        };
        promoMsg.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

        var promoLink = new TextBlock
        {
            Text = AuthorSiteUrl,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand
        };
        promoLink.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        promoLink.MouseLeftButtonUp += (_, _) => OpenAuthorSite();

        var promoBtnRow = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var promoBtn = new Button
        {
            Content = _loc.T(" Visitar Site", " Visit Website"),
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(14, 7, 14, 7)
        };
        promoBtn.Click += (_, _) => OpenAuthorSite();
        promoBtnRow.Children.Add(promoBtn);

        promoStack.Children.Add(promoTitle);
        promoStack.Children.Add(promoMsg);
        promoStack.Children.Add(promoLink);
        promoStack.Children.Add(promoBtnRow);

        promoCard.Child = promoStack;
        root.Children.Add(promoCard);

        ContentHost.Children.Clear();
        ContentHost.Children.Add(root);
    }

    private void OpenAuthorSite()
    {
        try
        {
            Process.Start(new ProcessStartInfo(AuthorSiteUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Falha ao abrir navegador: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
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





