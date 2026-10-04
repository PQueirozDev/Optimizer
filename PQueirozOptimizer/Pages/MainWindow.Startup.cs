using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PQueirozOptimizer.Models;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;
public partial class MainWindow
{
    // Página no estilo do Autoruns: itens de logon, tarefas agendadas e serviços automáticos
    private AutorunsService? _autorunsService;
    private AutorunsService Autoruns => _autorunsService ??= new AutorunsService(_log);
    private List<AutorunEntry>? _autoruns;
    private bool _autorunsLoading, _signaturesPending;
    private AutorunCategory? _autorunsCategory;
    private bool _hideWindowsEntries = true;
    private string _startupFilter = "";
    private StackPanel? _autorunsList;
    private WrapPanel? _autorunsTabs;
    private TextBlock? _autorunsSummary, _signatureStatus;

    private void ShowStartup()
    {
        PageTitle.Text = "Inicialização do Windows";
        PageBadge.Visibility = Visibility.Collapsed;
        var root = new StackPanel();

        var hero = new DockPanel();
        var refresh = IconButton(Glyphs.Refresh, "Atualizar");
        refresh.VerticalAlignment = VerticalAlignment.Center; refresh.Margin = new Thickness(16, 0, 0, 0);
        refresh.Click += async (_, _) => await LoadAutorunsAsync();
        DockPanel.SetDock(refresh, Dock.Right); hero.Children.Add(refresh);
        var chip = IconChip(Glyphs.Power, "Accent", 52); DockPanel.SetDock(chip, Dock.Left); hero.Children.Add(chip);
        var heroText = new StackPanel { Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var title = Label("Tudo o que inicia com o Windows", 18); title.Margin = new Thickness(0, 0, 0, 4);
        heroText.Children.Add(title);
        var sub = Label("Itens de logon, tarefas agendadas e serviços, como no Autoruns. Desmarcar desativa sem apagar nada: marque de novo para reativar.", 12.5, true); sub.Margin = new Thickness(0, 0, 0, 4);
        heroText.Children.Add(sub);
        _autorunsSummary = Label("", 12.5); _autorunsSummary.Margin = new Thickness(0); _autorunsSummary.FontWeight = FontWeights.SemiBold;
        heroText.Children.Add(_autorunsSummary);
        hero.Children.Add(heroText);
        var heroCard = Surface(hero); heroCard.SetResourceReference(Border.BackgroundProperty, "HeroBrush");
        root.Children.Add(heroCard);

        // Abas por origem + busca
        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var searchField = new Grid { Width = 280, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        var search = new TextBox { Text = _startupFilter, Height = 38 };
        var placeholder = new TextBlock { Text = "Filtrar por nome, editor ou caminho…", IsHitTestVisible = false, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5, Visibility = string.IsNullOrEmpty(_startupFilter) ? Visibility.Visible : Visibility.Collapsed };
        placeholder.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        searchField.Children.Add(search); searchField.Children.Add(placeholder);
        DockPanel.SetDock(searchField, Dock.Right); toolbar.Children.Add(searchField);
        _autorunsTabs = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        toolbar.Children.Add(_autorunsTabs);
        root.Children.Add(toolbar);

        var options = new DockPanel { Margin = new Thickness(2, 0, 0, 12) };
        var hideWindows = new CheckBox { Content = "Ocultar itens do Windows", IsChecked = _hideWindowsEntries, Margin = new Thickness(0), ToolTip = "Esconde o que é assinado pela Microsoft como parte do Windows, como a opção do Autoruns." };
        hideWindows.Click += (_, _) => { _hideWindowsEntries = hideWindows.IsChecked == true; RefreshAutorunsList(); };
        DockPanel.SetDock(hideWindows, Dock.Left); options.Children.Add(hideWindows);
        _signatureStatus = Label("", 12, true); _signatureStatus.Margin = new Thickness(18, 0, 0, 0); _signatureStatus.VerticalAlignment = VerticalAlignment.Center;
        options.Children.Add(_signatureStatus);
        root.Children.Add(options);

        _autorunsList = new StackPanel();
        root.Children.Add(_autorunsList);

        // A busca espera a pessoa parar de digitar: com os itens do Windows visíveis, a lista passa de 200 linhas
        var debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        debounce.Tick += (_, _) => { debounce.Stop(); RefreshAutorunsList(); };
        search.TextChanged += (_, _) =>
        {
            _startupFilter = search.Text;
            placeholder.Visibility = string.IsNullOrEmpty(search.Text) ? Visibility.Visible : Visibility.Collapsed;
            debounce.Stop(); debounce.Start();
        };

        ContentHost.Children.Clear();
        ContentHost.Children.Add(root);
        RefreshAutorunsList();
        if (_autoruns is null) _ = LoadAutorunsAsync();
    }

    private async Task LoadAutorunsAsync()
    {
        if (_autorunsLoading) return;
        _autorunsLoading = true;
        if (_signatureStatus != null) _signatureStatus.Text = "Lendo o que inicia com o Windows…";
        try
        {
            var entries = await Autoruns.ScanAsync();
            _autoruns = entries;
            _signaturesPending = true;
            RefreshAutorunsList();
            try { await Autoruns.VerifySignaturesAsync(entries); }
            catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or Win32Exception or System.Text.Json.JsonException)
            {
                _log.Write("WARN", "Inicialização: não foi possível verificar as assinaturas digitais: " + ex.Message);
            }
        }
        catch (Exception ex)
        {
            // Ponto de entrada da página: qualquer falha vira aviso na tela em vez de uma lista vazia sem explicação
            _log.Write("ERROR", "Inicialização: " + ex.Message);
            if (_autorunsList != null && ContentHost.IsAncestorOf(_autorunsList))
            {
                _autorunsList.Children.Clear();
                _autorunsList.Children.Add(Card("NÃO FOI POSSÍVEL LER A INICIALIZAÇÃO", ex.Message, Glyphs.Warning, "WarningBrush"));
            }
        }
        finally
        {
            _signaturesPending = false;
            _autorunsLoading = false;
            RefreshAutorunsList();
        }
    }

    private bool AutorunVisible(AutorunEntry entry)
    {
        if (_hideWindowsEntries && entry.IsWindows) return false;
        var query = _startupFilter.Trim();
        if (query.Length == 0) return true;
        return new[] { entry.Name, entry.Description, entry.Company, entry.Signer, entry.ImagePath ?? "", entry.Command, entry.Location, entry.Detail }
            .Any(text => text.Contains(query, StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>Redesenha só a lista e as contagens: a busca e as opções continuam como estão.</summary>
    private void RefreshAutorunsList()
    {
        if (_autorunsList is null || _autorunsTabs is null || _autorunsSummary is null || _signatureStatus is null || !ContentHost.IsAncestorOf(_autorunsList)) return;
        if (_autoruns is null)
        {
            _autorunsList.Children.Clear();
            var loading = new StackPanel();
            loading.Children.Add(SectionHeader("Lendo o que inicia com o Windows…", "Itens de logon, tarefas agendadas e serviços."));
            loading.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 5 });
            _autorunsList.Children.Add(Surface(loading));
            return;
        }

        var notHidden = _autoruns.Where(e => !_hideWindowsEntries || !e.IsWindows).ToList();
        var hiddenWindows = _autoruns.Count - notHidden.Count;
        var active = notHidden.Count(e => e.Enabled);
        var summary = $"{Plural(active, "ativo", "ativos")} · {Plural(notHidden.Count - active, "desativado", "desativados")}";
        if (hiddenWindows > 0) summary += $" · {Plural(hiddenWindows, "item do Windows oculto", "itens do Windows ocultos")}";
        _autorunsSummary.Text = summary;

        var unverified = notHidden.Count(e => e.Signature is SignatureStatus.NotSigned or SignatureStatus.NotVerified);
        var missing = notHidden.Count(e => e.ImagePath != null && !e.FileExists);
        _signatureStatus.Text = _signaturesPending ? "Verificando assinaturas digitais…"
            : unverified + missing == 0 ? "Assinaturas verificadas"
            : string.Join(" · ", new[] { unverified > 0 ? Plural(unverified, "sem assinatura válida", "sem assinatura válida") : null, missing > 0 ? Plural(missing, "arquivo não encontrado", "arquivos não encontrados") : null }.Where(t => t != null));

        var visible = _autoruns.Where(AutorunVisible).ToList();
        _autorunsTabs.Children.Clear();
        foreach (var (category, label) in new (AutorunCategory?, string)[] { (null, "Tudo"), (AutorunCategory.Logon, "Logon"), (AutorunCategory.Tasks, "Tarefas agendadas"), (AutorunCategory.Services, "Serviços") })
        {
            var count = category is null ? visible.Count : visible.Count(e => e.Category == category);
            var tab = new Button { Content = $"{label} ({count})", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 8), FontSize = 12.5 };
            if (category == _autorunsCategory) Primary(tab);
            else
            {
                tab.SetResourceReference(Button.BackgroundProperty, "CardBgBrush");
                tab.SetResourceReference(Button.ForegroundProperty, "MutedBrush");
                tab.SetResourceReference(Button.BorderBrushProperty, "BorderSubtleBrush");
            }
            tab.Click += (_, _) => { _autorunsCategory = category; RefreshAutorunsList(); };
            _autorunsTabs.Children.Add(tab);
        }

        _autorunsList.Children.Clear();
        var shown = visible.Where(e => _autorunsCategory is null || e.Category == _autorunsCategory).ToList();
        if (shown.Count == 0)
        {
            _autorunsList.Children.Add(Label(_hideWindowsEntries && _startupFilter.Trim().Length == 0 ? "Nenhum item fora do Windows nesta categoria." : "Nenhum item encontrado com esses filtros.", 13, true));
            return;
        }
        // Grupos na ordem da leitura (como o Autoruns), com as pastas de tarefas em ordem alfabética
        var list = new StackPanel();
        var groups = shown.GroupBy(e => (e.Category, e.Location))
            .OrderBy(g => g.Key.Category)
            .ThenBy(g => g.Key.Category == AutorunCategory.Tasks ? 0 : _autoruns.IndexOf(g.First()))
            .ThenBy(g => g.Key.Location, StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            var header = new TextBlock { Text = group.Key.Location, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, list.Children.Count == 0 ? 0 : 16, 0, 6), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = group.Key.Location };
            header.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            header.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            list.Children.Add(header);
            foreach (var entry in group.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)) list.Children.Add(AutorunRow(entry));
        }
        var card = Surface(list); card.Padding = new Thickness(14, 16, 14, 12);
        _autorunsList.Children.Add(card);
    }

    private static string Plural(int count, string one, string many) => $"{count} {(count == 1 ? one : many)}";

    private Border AutorunRow(AutorunEntry entry)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.8, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var check = new CheckBox { IsChecked = entry.Enabled, IsEnabled = entry.CanToggle, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        check.ToolTip = entry.CanToggle ? "Ativo ao iniciar o Windows" : "Este item não pode ser desativado por aqui";
        check.Click += async (_, _) => await ToggleAutorunAsync(entry, check);
        grid.Children.Add(check);

        FrameworkElement icon = entry.Icon is { } source
            ? new Image { Source = source, Width = 22, Height = 22, VerticalAlignment = VerticalAlignment.Center }
            : GlyphIcon(entry.Category == AutorunCategory.Tasks ? Glyphs.Clock : entry.Category == AutorunCategory.Services ? Glyphs.Settings : Glyphs.Document, 16, "MutedBrush");
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        icon.Width = 22; icon.Margin = new Thickness(0, 0, 12, 0);
        Grid.SetColumn(icon, 1); grid.Children.Add(icon);

        // Nomes, descrições dos fabricantes, editores e caminhos vêm do sistema: o tradutor não mexe neles
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        var name = new TextBlock { Text = entry.Name, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Tag = Translator.SystemDataTag };
        names.Children.Add(name);
        var about = entry.Description.Length > 0 ? entry.Description : entry.Detail;
        if (about.Length > 0)
        {
            var aboutText = new TextBlock { Text = about, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0), Tag = entry.Description.Length > 0 ? Translator.SystemDataTag : null };
            aboutText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            names.Children.Add(aboutText);
        }
        names.ToolTip = string.Join("\n", new[] { entry.Name, entry.Description, entry.Detail }.Where(t => t.Length > 0).Distinct());
        Grid.SetColumn(names, 2); grid.Children.Add(names);

        // Editor: o assinante verificado vale mais que o nome gravado no arquivo, que qualquer um pode preencher
        var publisher = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        var publisherName = entry.Signature == SignatureStatus.Verified && entry.Signer.Length > 0 ? entry.Signer : entry.Company;
        if (publisherName.Length > 0)
            publisher.Children.Add(new TextBlock { Text = publisherName, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = publisherName, Tag = Translator.SystemDataTag });
        var (badge, tone) = entry.ImagePath != null && !entry.FileExists ? ("Arquivo não encontrado", "Warning")
            : entry.Signature switch
            {
                SignatureStatus.Verified => ("Verificado", "Success"),
                SignatureStatus.NotSigned => ("Sem assinatura", "Danger"),
                SignatureStatus.NotVerified => ("Assinatura inválida", "Danger"),
                SignatureStatus.Pending => ("Verificando…", "Info"),
                _ => ("", ""),
            };
        if (badge.Length > 0)
        {
            var pill = Pill(badge, tone); pill.HorizontalAlignment = HorizontalAlignment.Left; pill.Margin = new Thickness(0, 3, 0, 0);
            publisher.Children.Add(pill);
        }
        Grid.SetColumn(publisher, 3); grid.Children.Add(publisher);

        var path = new TextBlock { Text = entry.ImagePath ?? entry.Command, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), ToolTip = entry.Command.Length > 0 ? entry.Command : entry.ImagePath, Tag = Translator.SystemDataTag };
        path.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        path.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        Grid.SetColumn(path, 4); grid.Children.Add(path);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var folder = SmallAction(Glyphs.Folder, "Abrir local do arquivo");
        folder.IsEnabled = entry.FileExists && entry.ImagePath != null;
        folder.Click += (_, _) => ShowInFolder(entry.ImagePath!);
        var jump = SmallAction(Glyphs.OpenInNew, "Ir para a entrada");
        jump.Click += (_, _) =>
        {
            try { AutorunsService.JumpTo(entry); }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or UnauthorizedAccessException or SecurityException)
            { _log.Write("WARN", "Não foi possível abrir a entrada: " + ex.Message); OperationStatus.Text = "Não foi possível abrir a entrada: " + ex.Message; }
        };
        var web = SmallAction(Glyphs.Search, "Pesquisar na internet");
        web.Click += (_, _) => OpenUrl("https://www.google.com/search?q=" + Uri.EscapeDataString($"{entry.Name} {Path.GetFileName(entry.ImagePath ?? "")}".Trim()));
        actions.Children.Add(folder); actions.Children.Add(jump); actions.Children.Add(web);
        Grid.SetColumn(actions, 5); grid.Children.Add(actions);

        // Cores do Autoruns: amarelo = arquivo sumiu, vermelho = sem assinatura válida
        var row = new Border { Child = grid, Padding = new Thickness(10, 7, 8, 7), CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 0, 3) };
        if (entry.ImagePath != null && !entry.FileExists) row.SetResourceReference(Border.BackgroundProperty, "WarningSoftBrush");
        else if (entry.Signature is SignatureStatus.NotSigned or SignatureStatus.NotVerified) row.SetResourceReference(Border.BackgroundProperty, "DangerSoftBrush");
        if (!entry.Enabled) foreach (UIElement part in new UIElement[] { icon, names, publisher, path }) part.Opacity = 0.5;
        return row;
    }

    private static Button SmallAction(string glyph, string tip)
    {
        var button = new Button { Content = GlyphIcon(glyph, 13, "MutedBrush"), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(2, 0, 0, 0), ToolTip = tip };
        button.SetResourceReference(StyleProperty, "GhostButton");
        return button;
    }

    private async Task ToggleAutorunAsync(AutorunEntry entry, CheckBox check)
    {
        var enable = check.IsChecked == true;
        if (!enable && !ConfirmDisableAutorun(entry)) { check.IsChecked = true; return; }
        check.IsEnabled = false;
        try
        {
            await Autoruns.SetEnabledAsync(entry, enable);
            entry.Enabled = enable;
            OperationStatus.Text = $"{entry.Name}: {(enable ? "ativado" : "desativado")} na inicialização.";
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or COMException or Win32Exception or InvalidOperationException or IOException or SecurityException)
        {
            check.IsChecked = !enable;
            _log.Write("ERROR", $"Inicialização: {entry.Name}: {ex.Message}");
            // Acesso negado mesmo como administrador: o item é protegido pelo Windows ou pelo próprio programa
            var denied = ex is UnauthorizedAccessException || ex.HResult == unchecked((int)0x80070005) || ex is Win32Exception { NativeErrorCode: 5 };
            if (denied && Msg($"O Windows protege \"{entry.Name}\": ele pertence ao sistema ou a um programa que trava as próprias permissões, então nem o administrador pode alterá-lo por aqui.\n\nDá para desligar pelas configurações do próprio programa ou em Configurações do Windows → Aplicativos → Inicialização. Abrir essa tela agora?",
                    "Inicialização do Windows", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                OpenUrl("ms-settings:startupapps");
            else if (!denied)
                Msg("Não foi possível alterar este item: " + ex.Message, "Inicialização do Windows", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            check.IsEnabled = entry.CanToggle;
            RefreshAutorunsList();
        }
    }

    /// <summary>Serviços e itens do Windows pedem confirmação antes de desativar; os demais são como no Gerenciador de Tarefas.</summary>
    private static bool ConfirmDisableAutorun(AutorunEntry entry)
    {
        var question = entry.Category == AutorunCategory.Services
            ? $"Desativar o início automático de \"{entry.Name}\"?\n\nO serviço passa para Manual: só inicia quando algum programa precisar dele. Drivers, antitrapaças e jogos podem depender de serviços; se algo parar de funcionar, marque o item de novo."
            : entry.IsWindows ? $"\"{entry.Name}\" faz parte do Windows. Desativar pode afetar recursos do sistema.\n\nDeseja continuar?" : null;
        return question is null || Msg(question, "Inicialização do Windows", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
    }
}
