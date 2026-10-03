using System.IO;
using System.Windows;
using System.Windows.Controls;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>Editor de BIOS pelo SCEWIN: leitura, busca, recomendações, gravação dos itens alterados e restauração.</summary>
public partial class MainWindow
{
    private List<BiosSetting>? _biosSettings;
    private string _biosSearch = "";
    private string _biosFilter = "recomendados";
    private BiosService? _bios;
    private BiosService Bios => _bios ??= new BiosService(_log);

    private Border BiosEditorCard()
    {
        var panel = new StackPanel();
        var tool = BiosService.ToolPath();
        panel.Children.Add(FeatureHeader(Glyphs.Chip, "Accent", "Editor de BIOS (SCEWIN)",
            "Lê e grava as configurações da BIOS pelo Windows com o SCEWIN, a ferramenta oficial da AMI. A primeira leitura é guardada como cópia original e só os itens que você alterar são gravados. Funciona em placas com BIOS AMI (ASUS, MSI, Gigabyte, ASRock e a maioria das outras).",
            tool is null ? "SCEWIN não configurado" : _biosSettings is null ? "Pronto para ler" : $"{_biosSettings.Count} configurações", tool is null ? "Warning" : "Success"));

        var actions = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        var choose = IconButton(Glyphs.Folder, tool is null ? "Escolher SCEWIN_64.exe" : "Trocar SCEWIN", primary: tool is null);
        choose.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "SCEWIN (SCEWIN_64.exe)|SCEWIN*.exe", Title = "Escolha o SCEWIN_64.exe (deixe o amifldrv64.sys na mesma pasta)" };
            if (dialog.ShowDialog(this) != true) return;
            try { BiosService.SetToolPath(dialog.FileName); ShowBios(); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException) { ShowToast("SCEWIN", ex.Message, "Danger"); }
        };
        actions.Children.Add(choose);
        if (tool != null)
        {
            var read = IconButton(Glyphs.Download, _biosSettings is null ? "Ler configurações da BIOS" : "Ler de novo", primary: _biosSettings is null);
            read.Click += async (_, _) =>
            {
                await ExecuteTrackedAsync("Lendo a BIOS pelo SCEWIN", async _ => _biosSettings = await Bios.ExportAsync());
                if (_currentPage == "bios") ShowBios();
            };
            actions.Children.Add(read);
            if (File.Exists(BiosService.OriginalPath))
            {
                var restore = IconButton(Glyphs.Undo, "Restaurar original");
                restore.Click += async (_, _) =>
                {
                    if (Msg("Gravar de volta na BIOS a cópia original feita na primeira leitura? Vale após reiniciar.", "Restaurar BIOS", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                    if (!await ExecuteTrackedAsync("Restaurando a BIOS original", async _ => await Bios.RestoreOriginalAsync())) return;
                    _biosSettings = null;
                    OfferRestart();
                };
                actions.Children.Add(restore);
            }
        }
        panel.Children.Add(actions);

        var warning = new Border { Padding = new Thickness(14, 10, 14, 10), CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 14, 0, 0) };
        warning.SetResourceReference(Border.BackgroundProperty, "WarningSoftBrush");
        var warningText = Label("Mude apenas o que você conhece. Se o PC não iniciar depois de gravar, limpe a CMOS (botão Clear CMOS ou retirando a bateria da placa-mãe por alguns minutos) para voltar ao padrão de fábrica.", 12);
        warningText.Margin = new Thickness(0); warningText.SetResourceReference(TextBlock.ForegroundProperty, "WarningBrush");
        warning.Child = warningText;
        panel.Children.Add(warning);

        if (_biosSettings != null) panel.Children.Add(BiosSettingsList());
        return Surface(panel);
    }

    private UIElement BiosSettingsList()
    {
        var settings = _biosSettings!;
        var host = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };

        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var searchField = new Grid { Width = 280, Margin = new Thickness(12, 0, 0, 0) };
        var search = new TextBox { Text = _biosSearch, Height = 38 };
        var placeholder = new TextBlock { Text = "Buscar configuração…", IsHitTestVisible = false, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5, Visibility = _biosSearch.Length == 0 ? Visibility.Visible : Visibility.Collapsed };
        placeholder.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        searchField.Children.Add(search); searchField.Children.Add(placeholder);
        DockPanel.SetDock(searchField, Dock.Right); bar.Children.Add(searchField);
        var filters = new WrapPanel();
        foreach (var (id, title) in new[] { ("recomendados", "Recomendados"), ("alterados", "Alterados"), ("todos", "Todas") })
        {
            var count = id switch { "recomendados" => settings.Count(s => BiosService.RecommendationFor(s) != null), "alterados" => settings.Count(s => s.Changed), _ => settings.Count };
            var pill = new Button { Content = $"{title}  {count}", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0), FontSize = 12 };
            if (_biosFilter == id) Primary(pill);
            pill.Click += (_, _) => { _biosFilter = id; ShowBios(); };
            filters.Children.Add(pill);
        }
        bar.Children.Add(filters);
        host.Children.Add(bar);

        var list = new StackPanel();
        host.Children.Add(list);
        void Render()
        {
            list.Children.Clear();
            var query = _biosSearch.Trim();
            var visible = settings.Where(s => _biosFilter switch { "recomendados" => BiosService.RecommendationFor(s) != null, "alterados" => s.Changed, _ => true })
                .Where(s => query.Length == 0 || s.Question.Contains(query, StringComparison.OrdinalIgnoreCase) || s.Help.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (visible.Count == 0) list.Children.Add(Label(_biosFilter == "recomendados" ? "Nenhuma das configurações recomendadas existe nesta BIOS." : "Nada encontrado.", 12.5, true));
            // A BIOS pode ter mais de mil itens: mostra os primeiros e pede para refinar a busca
            foreach (var s in visible.Take(120)) list.Children.Add(BiosRow(s));
            if (visible.Count > 120) list.Children.Add(Label($"Mostrando 120 de {visible.Count}. Refine a busca para ver as outras.", 12, true));
        }
        search.TextChanged += (_, _) =>
        {
            _biosSearch = search.Text;
            placeholder.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            Render();
        };
        Render();

        var changed = settings.Count(s => s.Changed);
        var recommend = IconButton(Glyphs.Lightning, "Aplicar recomendados");
        recommend.Click += (_, _) =>
        {
            var n = BiosService.ApplyRecommendations(settings);
            ShowToast("BIOS", n == 0 ? "As recomendações já estão aplicadas (ou não existem nesta BIOS)." : $"{n} configuração(ões) marcadas. Revise e grave.", n == 0 ? "Info" : "Success");
            _biosFilter = "alterados";
            ShowBios();
        };
        var write = IconButton(Glyphs.Check, changed == 0 ? "Gravar alterações" : $"Gravar {changed} alteração(ões)", primary: changed > 0);
        write.IsEnabled = changed > 0;
        write.Click += async (_, _) =>
        {
            var lines = string.Join("\n", settings.Where(s => s.Changed).Select(s => $"• {s.Question}: {s.SelectedLabel}"));
            if (Msg($"Gravar na BIOS?\n\n{lines}\n\nAs mudanças valem depois de reiniciar.", "Gravar na BIOS", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            if (!await ExecuteTrackedAsync("Gravando na BIOS", async _ => await Bios.ImportChangesAsync(settings))) return;
            _biosSettings = null;
            OfferRestart();
        };
        var summary = Label(changed == 0 ? "Altere uma opção para gravá-la." : "Só os itens alterados serão gravados.", 12, true);
        summary.Margin = new Thickness(0); summary.VerticalAlignment = VerticalAlignment.Center;
        host.Children.Add(ActionBar(summary, recommend, write));
        return host;
    }

    private Border BiosRow(BiosSetting s)
    {
        var row = new DockPanel();
        FrameworkElement editor;
        if (s.Options.Count > 0)
        {
            var combo = new ComboBox { Width = 220, VerticalAlignment = VerticalAlignment.Center };
            foreach (var o in s.Options) combo.Items.Add(new TextBlock { Text = o.Label, Tag = Translator.SystemDataTag });
            combo.SelectedIndex = s.SelectedIndex;
            combo.SelectionChanged += (_, _) => { s.SelectedIndex = combo.SelectedIndex; ShowBiosChangedCount(); };
            editor = combo;
        }
        else
        {
            var box = new TextBox { Width = 120, Text = s.NumericValue ?? "", VerticalAlignment = VerticalAlignment.Center };
            box.TextChanged += (_, _) => { s.NumericValue = box.Text.Trim(); ShowBiosChangedCount(); };
            editor = box;
        }
        DockPanel.SetDock(editor, Dock.Right); row.Children.Add(editor);
        var text = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        var title = Label(s.Question, 13); title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(0); title.Tag = Translator.SystemDataTag;
        titleRow.Children.Add(title);
        if (BiosService.RecommendationFor(s) is { } r)
        {
            var pill = Pill("Recomendado", "Success"); pill.Margin = new Thickness(10, 0, 0, 0); pill.ToolTip = r.Why;
            titleRow.Children.Add(pill);
        }
        text.Children.Add(titleRow);
        var help = BiosService.RecommendationFor(s)?.Why ?? s.Help;
        if (!string.IsNullOrWhiteSpace(help))
        {
            var h = Label(help, 11.5, true); h.Margin = new Thickness(0, 2, 0, 0); h.MaxHeight = 34; h.TextTrimming = TextTrimming.CharacterEllipsis;
            if (BiosService.RecommendationFor(s) is null) h.Tag = Translator.SystemDataTag;
            text.Children.Add(h);
        }
        row.Children.Add(text);
        var border = new Border { Child = row, Padding = new Thickness(16, 12, 16, 12), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 8) };
        border.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        border.SetResourceReference(Border.BorderBrushProperty, s.Changed ? "AccentBrush" : "BorderSubtleBrush");
        return border;
    }

    private void ShowBiosChangedCount()
    {
        var n = _biosSettings?.Count(s => s.Changed) ?? 0;
        OperationStatus.Text = n == 0 ? "Nenhuma alteração pendente na BIOS." : $"{n} alteração(ões) pendente(s) na BIOS. Clique em Gravar para salvar.";
    }

    private void OfferRestart()
    {
        if (_currentPage == "bios") ShowBios();
        if (Msg("Reiniciar agora para aplicar as mudanças na BIOS?", "BIOS", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown.exe", "/r /t 5") { UseShellExecute = true, CreateNoWindow = true }); }
            catch (System.ComponentModel.Win32Exception ex) { ShowToast("BIOS", ex.Message, "Danger"); }
        }
    }
}
