using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>Pontos de restauração: criar, listar e restaurar (e os componentes de lista usados pelas páginas de manutenção).</summary>
public partial class MainWindow
{
    // ================= Pontos de restauração =================
    private void ShowRestorePoints()
    {
        PageTitle.Text = "Pontos de restauração";
        PageBadge.Visibility = Visibility.Collapsed;
        var root = new StackPanel();

        // Criar
        var create = new StackPanel();
        create.Children.Add(SectionHeader("Criar ponto de restauração", "Um retrato das configurações do Windows para voltar caso algo dê errado."));
        var name = LabeledBox("Nome", "Ex.: Antes de otimizar");
        var description = LabeledBox("Descrição (opcional)", "O que você vai mudar");
        var tags = LabeledBox("Etiquetas (separadas por vírgula)", "Ex.: drivers, jogos");
        var fields = Responsive(new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -12, 0) }, 260, 3);
        fields.Children.Add(name.Host); fields.Children.Add(description.Host); fields.Children.Add(tags.Host);
        create.Children.Add(fields);
        var colorRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        var colorLabel = Label("Cor:", 12, true); colorLabel.Margin = new Thickness(0, 0, 10, 0); colorLabel.VerticalAlignment = VerticalAlignment.Center;
        colorRow.Children.Add(colorLabel);
        var color = "Accent";
        var swatches = new List<Border>();
        foreach (var tone in new[] { "Accent", "Info", "Success", "Warning", "Danger" })
        {
            var swatch = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 8, 0), BorderThickness = new Thickness(2), Cursor = System.Windows.Input.Cursors.Hand, Tag = tone, ToolTip = tone };
            swatch.SetResourceReference(Border.BackgroundProperty, tone + "Brush");
            swatch.BorderBrush = System.Windows.Media.Brushes.Transparent;
            var selected = new TextBlock { Text = "✓", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = tone == color ? Visibility.Visible : Visibility.Collapsed };
            swatch.Child = selected;
            swatch.MouseLeftButtonUp += (_, _) =>
            {
                color = tone;
                foreach (var s in swatches)
                {
                    s.BorderBrush = s.Tag as string == tone ? (Brush)FindResource("AccentBrush") : Brushes.Transparent;
                    if (s.Child is TextBlock mark) mark.Visibility = s.Tag as string == tone ? Visibility.Visible : Visibility.Collapsed;
                }
            };
            swatches.Add(swatch); colorRow.Children.Add(swatch);
        }
        swatches[0].BorderBrush = (Brush)FindResource("AccentBrush");
        var createBtn = IconButton(Glyphs.Add, "Criar ponto", primary: true);
        createBtn.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(name.Box.Text)) { ShowToast("Ponto de restauração", "Dê um nome antes de criar.", "Warning"); return; }
            var tagList = tags.Box.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (await ExecuteTrackedAsync("Criando ponto de restauração", async _ => await new RestorePointService(_log).CreateAsync(name.Box.Text, description.Box.Text, tagList, color)) && _currentPage == "restore")
                ShowRestorePoints();
        };
        create.Children.Add(ActionBar(colorRow, createBtn));

        // Por que criar
        var learn = new StackPanel();
        learn.Children.Add(SectionHeader("Por que criar um ponto de restauração?"));
        foreach (var benefit in new[]
        {
            "Desfazer qualquer mudança no sistema, inclusive as feitas pelo app, a qualquer momento.",
            "Proteger as configurações caso a energia caia no meio de uma alteração.",
            "Testar drivers e ajustes novos sabendo que dá para voltar ao estado anterior.",
        })
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            row.Children.Add(GlyphIcon(Glyphs.Check, 13, "SuccessBrush"));
            var t = Label(benefit, 12.5); t.Margin = new Thickness(10, 0, 0, 0); t.MaxWidth = 380;
            row.Children.Add(t);
            learn.Children.Add(row);
        }
        root.Children.Add(TwoColumns(Surface(create), Surface(learn)));

        // Catálogo
        var catalog = new StackPanel();
        catalog.Children.Add(SectionHeader("Catálogo", "Pontos de restauração existentes neste PC, do mais recente ao mais antigo."));
        var list = new StackPanel();
        list.Children.Add(Label("Lendo os pontos de restauração...", 12.5, true));
        catalog.Children.Add(list);
        root.Children.Add(Surface(catalog));
        _ = FillRestoreCatalogAsync(list);

        ContentHost.Children.Clear();
        ContentHost.Children.Add(root);
    }

    private (Grid Host, TextBox Box) LabeledBox(string label, string placeholder)
    {
        var host = new Grid { Margin = new Thickness(0, 0, 12, 10) };
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var l = Label(label, 11.5, true); l.Margin = new Thickness(0, 0, 0, 5);
        host.Children.Add(l);
        var box = new TextBox { Height = 38 };
        var hint = new TextBlock { Text = placeholder, IsHitTestVisible = false, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5 };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        box.TextChanged += (_, _) => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var field = new Grid(); field.Children.Add(box); field.Children.Add(hint);
        Grid.SetRow(field, 1); host.Children.Add(field);
        return (host, box);
    }

    private async Task FillRestoreCatalogAsync(StackPanel list)
    {
        IReadOnlyList<RestorePoint> points;
        try { points = await RestorePointService.ListAsync(); }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or System.Text.Json.JsonException)
        {
            list.Children.Clear();
            list.Children.Add(Label("Não foi possível ler os pontos de restauração: " + ex.Message, 12.5, true));
            return;
        }
        list.Children.Clear();
        if (points.Count == 0) { list.Children.Add(EmptyState(Glyphs.Restore2, "Nenhum ponto de restauração ainda", "Crie o primeiro acima. A proteção do sistema é ativada automaticamente no disco do Windows.")); return; }
        foreach (var p in points)
        {
            var row = new DockPanel();
            var restore = IconButton(Glyphs.Undo, "Restaurar");
            restore.Click += async (_, _) =>
            {
                if (Msg($"Restaurar o sistema para \"{p.Description}\"?\n\nO PC vai reiniciar agora para concluir. Salve seus trabalhos antes.", "Restaurar sistema", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                await ExecuteTrackedAsync("Restaurando o sistema", async _ => await new RestorePointService(_log).RestoreAsync(p.Sequence));
            };
            DockPanel.SetDock(restore, Dock.Right); row.Children.Add(restore);
            var bar = new Border { Width = 4, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 14, 0) };
            bar.SetResourceReference(Border.BackgroundProperty, p.Color + "Brush");
            DockPanel.SetDock(bar, Dock.Left); row.Children.Add(bar);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var title = Label(p.Description, 13); title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(0); title.Tag = Translator.SystemDataTag;
            text.Children.Add(title);
            var meta = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            var when = Label($"{p.CreatedAt:dd/MM/yyyy HH:mm} · nº {p.Sequence}", 11.5, true); when.Margin = new Thickness(0, 0, 10, 0); when.Tag = Translator.SystemDataTag;
            meta.Children.Add(when);
            foreach (var tag in p.Tags) { var pill = Pill(tag, p.Color); pill.Margin = new Thickness(0, 0, 6, 0); ((TextBlock)pill.Child).Tag = Translator.SystemDataTag; meta.Children.Add(pill); }
            text.Children.Add(meta);
            row.Children.Add(text);
            list.Children.Add(ListRow(row));
        }
    }

    private Border ListRow(UIElement content)
    {
        var border = new Border { Child = content, Padding = new Thickness(16, 12, 16, 12), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 8) };
        border.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderSubtleBrush");
        return border;
    }

    /// <summary>Estado vazio padrão: ícone, título e explicação centralizados.</summary>
    private StackPanel EmptyState(string glyph, string title, string detail)
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 16) };
        var chip = IconChip(glyph, "Accent", 48); chip.Margin = new Thickness(0, 0, 0, 12);
        panel.Children.Add(chip);
        var t = Label(title, 14); t.FontWeight = FontWeights.SemiBold; t.HorizontalAlignment = HorizontalAlignment.Center; t.Margin = new Thickness(0, 0, 0, 4);
        panel.Children.Add(t);
        var d = Label(detail, 12, true); d.HorizontalAlignment = HorizontalAlignment.Center; d.TextAlignment = TextAlignment.Center; d.MaxWidth = 460;
        panel.Children.Add(d);
        return panel;
    }
}
