using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PQueirozOptimizer.Models;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;
public partial class MainWindow
{
    #region Drivers Page
    private const string AllDriversCategory = "Todos";

    private void ShowDrivers()
    {
        PageTitle.Text = "Drivers & Utilitários";
        PageBadge.Visibility = Visibility.Collapsed;
        // A recomendação depende da placa de vídeo; sem leitura recente, lê em segundo plano e redesenha
        if (_snapshot is null) _ = RefreshDriversWhenSnapshotReadyAsync();

        var allDrivers = _driverService.GetAllDrivers(_snapshot, _loc.IsEnglish);
        if (!IsAdminLicense)
        {
            allDrivers = allDrivers.Where(driver => !driver.Name.StartsWith("Driver Clean", StringComparison.OrdinalIgnoreCase)).ToList();
        }
        // Filtros saem das categorias que existem de fato, com a mesma regra da contagem
        var categories = allDrivers.Select(d => d.Category).Distinct().ToList();
        if (_driverCategory != AllDriversCategory && !categories.Contains(_driverCategory)) _driverCategory = AllDriversCategory;

        var root = new StackPanel();
        root.Children.Add(DriverCleanCard());
        var subHeader = Label(!string.IsNullOrEmpty(_snapshot?.Graphics)
            ? $"Placa de vídeo detectada: {_snapshot.Graphics} • Todos os drivers com links oficiais e opção de download."
            : "Baixe e atualize os drivers essenciais de vídeo, chipset, rede e áudio para máxima taxa de quadros e menor latência.", 12.5, true);
        subHeader.Margin = new Thickness(0, 0, 0, 14);
        root.Children.Add(subHeader);

        var filterBar = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var searchField = new Grid { Width = 280, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        var searchBox = new TextBox { Text = _driverSearch, Height = 38 };
        var placeholder = new TextBlock { Text = "Filtrar por nome, fabricante ou categoria…", IsHitTestVisible = false, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5, Visibility = string.IsNullOrEmpty(_driverSearch) ? Visibility.Visible : Visibility.Collapsed };
        placeholder.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        searchField.Children.Add(searchBox); searchField.Children.Add(placeholder);
        DockPanel.SetDock(searchField, Dock.Right);
        filterBar.Children.Add(searchField);

        var categoriesPanel = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var category in categories.Prepend(AllDriversCategory))
        {
            var count = category == AllDriversCategory ? allDrivers.Count : allDrivers.Count(d => d.Category == category);
            var pill = new Button { Content = $"{category} ({count})", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 8), FontSize = 12 };
            if (category == _driverCategory) Primary(pill);
            else
            {
                pill.SetResourceReference(Button.BackgroundProperty, "CardBgBrush");
                pill.SetResourceReference(Button.ForegroundProperty, "MutedBrush");
                pill.SetResourceReference(Button.BorderBrushProperty, "BorderSubtleBrush");
            }
            pill.Click += (_, _) => { _driverCategory = category; ShowDrivers(); };
            categoriesPanel.Children.Add(pill);
        }
        filterBar.Children.Add(categoriesPanel);
        root.Children.Add(filterBar);

        var cards = Responsive(new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -14, 0) }, 360, 2);
        var emptyHost = new StackPanel();
        root.Children.Add(cards);
        root.Children.Add(emptyHost);
        searchBox.TextChanged += (_, _) =>
        {
            _driverSearch = searchBox.Text.Trim();
            placeholder.Visibility = string.IsNullOrEmpty(searchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            RenderDriverCards(cards, emptyHost, allDrivers);
        };
        RenderDriverCards(cards, emptyHost, allDrivers);

        ContentHost.Children.Clear();
        ContentHost.Children.Add(root);
    }

    private async Task RefreshDriversWhenSnapshotReadyAsync()
    {
        try
        {
            await ReadSnapshotAsync();
            // Não redesenha enquanto a pessoa digita na busca (perderia o foco do campo)
            if (_currentPage == "drivers" && System.Windows.Input.Keyboard.FocusedElement is not TextBox) ShowDrivers();
        }
        catch (Exception ex) { _log.Write("WARN", "Não foi possível detectar a placa de vídeo: " + ex.Message); }
    }

    private void RenderDriverCards(UniformGrid cards, StackPanel emptyHost, List<DriverInfo> allDrivers)
    {
        cards.Children.Clear();
        emptyHost.Children.Clear();

        var filtered = allDrivers.Where(d =>
            (_driverCategory == AllDriversCategory || d.Category == _driverCategory) &&
            (string.IsNullOrEmpty(_driverSearch)
             || d.Name.Contains(_driverSearch, StringComparison.CurrentCultureIgnoreCase)
             || d.Vendor.Contains(_driverSearch, StringComparison.CurrentCultureIgnoreCase)
             || d.Description.Contains(_driverSearch, StringComparison.CurrentCultureIgnoreCase)
             || d.Category.Contains(_driverSearch, StringComparison.CurrentCultureIgnoreCase))).ToList();

        if (filtered.Count == 0)
        {
            emptyHost.Children.Add(Label("Nenhum driver encontrado para os filtros selecionados.", 13, true));
            return;
        }

        foreach (var driver in filtered)
        {
            var body = new DockPanel();

            var badges = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            foreach (var (badgeText, tone) in new[] { (driver.Vendor, "Accent"), (driver.Category, "Info") })
            {
                var badge = Pill(badgeText, tone); badge.Margin = new Thickness(0, 0, 6, 6);
                badges.Children.Add(badge);
            }
            if (driver.IsRecommendedForCurrentHardware)
            {
                var recommended = Pill("Recomendado para o seu PC", "Success"); recommended.Margin = new Thickness(0, 0, 6, 6);
                badges.Children.Add(recommended);
            }
            DockPanel.SetDock(badges, Dock.Top);
            body.Children.Add(badges);

            var existingFile = _driverService.GetExistingInstallerPath(driver);
            var buttons = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            var official = IconButton(Glyphs.Download, "Baixar (Site Oficial)", primary: existingFile is null);
            official.Click += (_, _) => OpenUrl(driver.OfficialDownloadUrl);
            buttons.Children.Add(official);
            if (!string.IsNullOrEmpty(driver.SecondaryDownloadUrl))
            {
                var secondary = IconButton(Glyphs.Download, driver.SecondaryDownloadLabel ?? "Download Alternativo");
                secondary.Click += (_, _) => OpenUrl(driver.SecondaryDownloadUrl);
                buttons.Children.Add(secondary);
            }

            var status = Label(existingFile is null ? "Disponível para download oficial" : $"Instalador disponível: {Path.GetFileName(existingFile)}", 12, existingFile is null);
            status.Margin = new Thickness(0, 12, 0, 0);
            if (existingFile != null) status.SetResourceReference(TextBlock.ForegroundProperty, "SuccessBrush");
            var progress = new ProgressBar { Height = 4, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };

            if (!string.IsNullOrEmpty(driver.DirectDownloadUrl))
            {
                var direct = IconButton(Glyphs.Download, "Download Direto");
                direct.Click += async (_, _) => await DownloadDriverAsync(driver, direct, official, status, progress);
                buttons.Children.Add(direct);
            }
            if (existingFile != null)
            {
                var run = IconButton(Glyphs.ChevronRight, "Executar Instalador", primary: true);
                run.Click += (_, _) =>
                {
                    try { Process.Start(new ProcessStartInfo(existingFile) { UseShellExecute = true }); }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                    { Msg($"Falha ao iniciar o instalador: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error); }
                };
                buttons.Children.Add(run);
                var folder = IconButton(Glyphs.Folder, "Abrir Pasta");
                folder.Click += (_, _) => ShowInFolder(existingFile);
                buttons.Children.Add(folder);
            }
            DockPanel.SetDock(buttons, Dock.Bottom);
            body.Children.Add(buttons);

            var text = new StackPanel();
            var title = Label(driver.Name, 15); title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(0, 0, 0, 2);
            text.Children.Add(title);
            var version = Label(driver.Version, 11.5, true); version.Margin = new Thickness(0, 0, 0, 8);
            text.Children.Add(version);
            var description = Label(driver.Description, 12, true); description.Margin = new Thickness(0); description.LineHeight = 18;
            text.Children.Add(description);
            text.Children.Add(status);
            text.Children.Add(progress);
            body.Children.Add(text);

            var card = Surface(body); card.Margin = new Thickness(0, 0, 14, 14); card.Padding = new Thickness(20, 18, 20, 18);
            if (driver.IsRecommendedForCurrentHardware) card.SetResourceReference(Border.BorderBrushProperty, "AccentSoftBrush");
            cards.Children.Add(card);
        }
    }

    private async Task DownloadDriverAsync(DriverInfo driver, Button direct, Button official, TextBlock status, ProgressBar progressBar)
    {
        direct.IsEnabled = false; official.IsEnabled = false;
        progressBar.Visibility = Visibility.Visible; progressBar.Value = 0;
        status.Text = "Iniciando download...";
        status.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        var progress = new Progress<(long read, long total)>(p =>
        {
            if (p.total > 0)
            {
                progressBar.Value = (double)p.read / p.total * 100.0;
                status.Text = $"Baixando... {p.read / 1048576d:N1} MB / {p.total / 1048576d:N1} MB ({progressBar.Value:N0}%)";
            }
            else status.Text = $"Baixando... {p.read / 1048576d:N1} MB";
        });
        try
        {
            var downloadedPath = await _driverService.DownloadDirectAsync(driver, progress);
            if (Msg($"Download de '{driver.Name}' concluído com sucesso!\n\nSalvo em:\n{downloadedPath}\n\nDeseja executar o instalador agora?", "Download Concluído", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                try { Process.Start(new ProcessStartInfo(downloadedPath) { UseShellExecute = true }); }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                { Msg($"Falha ao iniciar o instalador: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
            ShowDrivers();
        }
        catch (Exception ex)
        {
            _log.Write("ERROR", $"Download direto de {driver.Name} falhou: {ex.Message}");
            progressBar.Visibility = Visibility.Collapsed;
            status.Text = "Falha no download direto. Abrindo página oficial no navegador...";
            status.SetResourceReference(TextBlock.ForegroundProperty, "WarningBrush");
            OpenUrl(driver.OfficialDownloadUrl);
        }
        finally { direct.IsEnabled = true; official.IsEnabled = true; }
    }
    #endregion
}
