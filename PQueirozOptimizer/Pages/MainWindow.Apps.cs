using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>Apps: otimizador de apps e desinstalador.</summary>
public partial class MainWindow
{
    private int _appsTab;

    private bool _appsStore;
    private string _appsSearch = "";

    // ================= Apps =================
    private void ShowApps()
    {
        PageTitle.Text = "Apps";
        PageBadge.Visibility = Visibility.Collapsed;
        var root = new StackPanel();
        root.Children.Add(Tabs(new[] { (Glyphs.Lightning, "Otimizar apps"), (Glyphs.Delete, "Desinstalar") }, _appsTab, tab => { _appsTab = tab; ShowApps(); AnimatePageIn(); }));
        if (_appsTab == 0) root.Children.Add(AppOptimizerCard());
        else root.Children.Add(UninstallCard());
        ContentHost.Children.Clear();
        ContentHost.Children.Add(root);
    }

    private Border AppOptimizerCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionHeader("Otimizador de apps", "Apps que ficam abertos enquanto você joga também usam GPU e CPU. Cada ajuste é reversível: desmarque para voltar ao que era."));
        var service = new AppOptimizerService(_log);
        foreach (var tweak in AppOptimizerService.Tweaks)
        {
            var installed = AppOptimizerService.IsInstalled(tweak);
            var applied = installed && service.IsApplied(tweak);
            var check = new CheckBox { IsChecked = applied, IsEnabled = installed };
            var row = ChoiceRow(check, $"{tweak.App} — {tweak.Title}", tweak.Description, !installed ? "Não instalado" : applied ? "Aplicado" : "Padrão", !installed ? "Warning" : applied ? "Success" : "Info");
            void Run(bool apply)
            {
                try
                {
                    if (apply) service.Apply(tweak); else service.Revert(tweak);
                    ShowToast(tweak.App, apply ? "Ajuste aplicado. Reabra o app para valer." : "Configuração original restaurada.", "Success");
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or System.Text.Json.JsonException)
                {
                    ShowToast(tweak.App, ex.Message, "Danger");
                }
                AfterToggleAnimation(() => { if (_currentPage == "apps") ShowApps(); });
            }
            check.Checked += (_, _) => Run(true);
            check.Unchecked += (_, _) => Run(false);
            panel.Children.Add(row);
        }
        return Surface(panel);
    }

    private Border UninstallCard()
    {
        var panel = new StackPanel();
        var head = new DockPanel();
        var auto = IconButton(Glyphs.Shield, "Debloat automático", primary: true);
        auto.VerticalAlignment = VerticalAlignment.Top;
        auto.Click += (_, _) => _ = PrepareOperationAsync("debloat");
        DockPanel.SetDock(auto, Dock.Right); head.Children.Add(auto);
        head.Children.Add(SectionHeader("Desinstalar programas", "Programas da área de trabalho abrem o desinstalador do fabricante; apps da Loja são removidos direto. O Debloat automático remove os apps que vêm com o Windows e quase ninguém usa."));
        panel.Children.Add(head);

        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var searchField = new Grid { Width = 280, Margin = new Thickness(12, 0, 0, 0) };
        var search = new TextBox { Text = _appsSearch, Height = 38 };
        var hint = new TextBlock { Text = "Buscar programa…", IsHitTestVisible = false, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5, Visibility = _appsSearch.Length == 0 ? Visibility.Visible : Visibility.Collapsed };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        searchField.Children.Add(search); searchField.Children.Add(hint);
        DockPanel.SetDock(searchField, Dock.Right); bar.Children.Add(searchField);
        var types = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (store, title) in new[] { (false, "Área de trabalho"), (true, "Microsoft Store") })
        {
            var b = new Button { Content = title, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0), FontSize = 12 };
            if (_appsStore == store) Primary(b);
            b.Click += (_, _) => { _appsStore = store; ShowApps(); };
            types.Children.Add(b);
        }
        bar.Children.Add(types);
        panel.Children.Add(bar);

        var list = new StackPanel();
        list.Children.Add(Label("Lendo os programas instalados...", 12.5, true));
        panel.Children.Add(list);
        List<InstalledApp>? apps = null;
        void Render()
        {
            list.Children.Clear();
            if (apps is null) return;
            var q = _appsSearch.Trim();
            var visible = apps.Where(a => q.Length == 0 || a.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || a.Publisher.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
            if (visible.Count == 0) { list.Children.Add(EmptyState(Glyphs.Search, "Nada encontrado", "Tente outro nome ou troque entre Área de trabalho e Microsoft Store.")); return; }
            foreach (var app in visible.Take(150))
            {
                var row = new DockPanel();
                var remove = IconButton(Glyphs.Delete, "Desinstalar");
                remove.Click += async (_, _) =>
                {
                    if (Msg($"Desinstalar \"{app.Name}\"?", "Desinstalar", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                    if (await ExecuteTrackedAsync("Desinstalando " + app.Name, async _ => await new AppOptimizerService(_log).UninstallAsync(app)) && _currentPage == "apps") ShowApps();
                };
                DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove);
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var t = Label(app.Name, 13); t.FontWeight = FontWeights.SemiBold; t.Margin = new Thickness(0); t.Tag = Translator.SystemDataTag;
                text.Children.Add(t);
                var d = Label(string.Join(" · ", new[] { app.Publisher, app.Version }.Where(s => s.Length > 0)), 11.5, true); d.Margin = new Thickness(0, 2, 0, 0); d.Tag = Translator.SystemDataTag;
                text.Children.Add(d);
                row.Children.Add(text);
                list.Children.Add(ListRow(row));
            }
            if (visible.Count > 150) list.Children.Add(Label($"Mostrando 150 de {visible.Count}. Use a busca para encontrar os outros.", 12, true));
        }
        search.TextChanged += (_, _) => { _appsSearch = search.Text; hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; Render(); };
        _ = LoadApps();
        async Task LoadApps()
        {
            try { apps = _appsStore ? await AppOptimizerService.StoreAppsAsync() : await Task.Run(AppOptimizerService.DesktopApps); }
            catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or System.Text.Json.JsonException) { list.Children.Clear(); list.Children.Add(Label("Não foi possível listar os programas: " + ex.Message, 12.5, true)); return; }
            Render();
        }
        return Surface(panel);
    }
}
