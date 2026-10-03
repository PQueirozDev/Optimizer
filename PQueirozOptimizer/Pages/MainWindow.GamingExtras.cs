using System.IO;
using System.Windows;
using System.Windows.Controls;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>Seções da página Modo Jogo: configurações dos jogos, perfil NVIDIA e Windows Defender.</summary>
public partial class MainWindow
{
    private int _gamingTab;
    private GameConfigService? _gameConfigs;
    private GameConfigService GameConfigs => _gameConfigs ??= new GameConfigService(_log);

    // ================= Capas dos jogos =================
    /// <summary>
    /// Banner com a capa do jogo. Começa com um gradiente e o nome do jogo e troca pela imagem quando ela
    /// carrega; ícones (imagens quadradas) aparecem centralizados sobre o gradiente em vez de esticados.
    /// </summary>
    private Border GameBanner(string title, double height, double radius, Func<Task<System.Windows.Media.Imaging.BitmapSource?>> load)
    {
        var host = new Grid();
        var fallback = new TextBlock { Text = title, FontSize = height > 60 ? 22 : 13, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.9, Tag = Translator.SystemDataTag, TextTrimming = TextTrimming.CharacterEllipsis };
        fallback.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        host.Children.Add(fallback);
        var banner = new Border { Height = height, CornerRadius = new CornerRadius(radius), Child = host, ClipToBounds = true };
        banner.SetResourceReference(Border.BackgroundProperty, "AccentGradientBrush");
        _ = FillBannerAsync(banner, host, fallback, height, load);
        return banner;
    }

    private static async Task FillBannerAsync(Border banner, Grid host, TextBlock fallback, double height, Func<Task<System.Windows.Media.Imaging.BitmapSource?>> load)
    {
        System.Windows.Media.Imaging.BitmapSource? image;
        try { image = await load(); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException) { image = null; }
        if (image is null) return;
        if (image.PixelWidth < image.PixelHeight * 1.3)
        {
            // Ícone: centralizado, com o nome ao lado em banners grandes
            fallback.Visibility = height > 60 ? Visibility.Visible : Visibility.Collapsed;
            if (height > 60) { fallback.HorizontalAlignment = HorizontalAlignment.Left; fallback.Margin = new Thickness(height + 8, 0, 12, 0); }
            host.Children.Add(new Image { Source = image, Width = height * 0.7, Height = height * 0.7, HorizontalAlignment = height > 60 ? HorizontalAlignment.Left : HorizontalAlignment.Center, Margin = new Thickness(height > 60 ? height * 0.2 : 0, 0, 0, 0) });
            return;
        }
        fallback.Visibility = Visibility.Collapsed;
        banner.Background = new System.Windows.Media.ImageBrush(image) { Stretch = System.Windows.Media.Stretch.UniformToFill, AlignmentY = System.Windows.Media.AlignmentY.Center };
        // Escurece a base para o texto do cartão abaixo não "brigar" com a imagem
        var shade = new System.Windows.Media.LinearGradientBrush(System.Windows.Media.Color.FromArgb(0, 0, 0, 0), System.Windows.Media.Color.FromArgb(0x70, 0, 0, 0), 90);
        host.Children.Add(new Border { Background = shade });
        if (Application.Current?.MainWindow is { IsVisible: true })
            banner.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350)));
    }

    // ================= Configurações dos jogos =================
    private Border GameConfigsCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionHeader("Configurações dos jogos", "Preset competitivo gravado direto no arquivo de configuração do jogo: menos efeitos pesados, sem V-Sync e sem desfoque. O original é guardado e volta com um clique."));
        var grid = Responsive(new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -12, 0) }, 360, 2);
        foreach (var preset in GameConfigService.Presets)
        {
            var path = GameConfigService.ConfigPath(preset);
            var applied = path != null && GameConfigService.HasBackup(preset);
            var body = new DockPanel();

            var actions = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            var apply = IconButton(Glyphs.Lightning, applied ? "Reaplicar" : "Aplicar preset", primary: path != null && !applied);
            apply.IsEnabled = path != null;
            apply.Click += (_, _) =>
            {
                try
                {
                    var changes = GameConfigs.Apply(preset);
                    ShowToast(preset.Name, changes == 0 ? "O arquivo já estava com o preset." : $"Preset aplicado: {changes} ajustes.", "Success");
                    ShowGaming();
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException) { ShowToast(preset.Name, ex.Message, "Danger"); }
            };
            actions.Children.Add(apply);
            if (applied)
            {
                var restore = IconButton(Glyphs.Undo, "Restaurar original");
                restore.Click += (_, _) =>
                {
                    try { GameConfigs.Restore(preset); ShowToast(preset.Name, "Configurações originais restauradas.", "Success"); ShowGaming(); }
                    catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException) { ShowToast(preset.Name, ex.Message, "Danger"); }
                };
                actions.Children.Add(restore);
            }
            DockPanel.SetDock(actions, Dock.Bottom); body.Children.Add(actions);

            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var (status, tone) = path is null ? ("Não encontrado", "Warning") : applied ? ("Preset ativo", "Success") : ("Pronto", "Info");
            var pill = Pill(status, tone); DockPanel.SetDock(pill, Dock.Right); pill.VerticalAlignment = VerticalAlignment.Top; head.Children.Add(pill);
            var chip = IconChip(Glyphs.Game, applied ? "Success" : "Accent", 40); DockPanel.SetDock(chip, Dock.Left); head.Children.Add(chip);
            var titles = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var name = Label(preset.Name, 15); name.FontWeight = FontWeights.SemiBold; name.Margin = new Thickness(0);
            titles.Children.Add(name);
            var count = Label($"{preset.Values.Length} ajustes", 11.5, true); count.Margin = new Thickness(0, 1, 0, 0);
            titles.Children.Add(count);
            head.Children.Add(titles);
            var art = GameBanner(preset.Name, 104, 12, () => GameArtService.PresetArtAsync(preset.Id));
            art.Margin = new Thickness(0, 0, 0, 14);
            DockPanel.SetDock(art, Dock.Top); body.Children.Add(art);
            DockPanel.SetDock(head, Dock.Top); body.Children.Add(head);

            var text = new StackPanel();
            var description = Label(preset.Description, 12, true); description.Margin = new Thickness(0, 0, 0, 6);
            text.Children.Add(description);
            var note = Label(path is null ? "Abra o jogo uma vez e feche para ele criar o arquivo de configuração." : preset.Notes[0], 11.5, true);
            note.Margin = new Thickness(0); note.SetResourceReference(TextBlock.ForegroundProperty, path is null ? "WarningBrush" : "MutedBrush");
            text.Children.Add(note);
            body.Children.Add(text);

            var card = new Border { Child = body, Padding = new Thickness(18), Margin = new Thickness(0, 0, 12, 12), CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1) };
            card.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
            card.SetResourceReference(Border.BorderBrushProperty, applied ? "SuccessSoftBrush" : "BorderSubtleBrush");
            grid.Children.Add(card);
        }
        panel.Children.Add(grid);
        return Surface(panel);
    }

    // ================= Perfil NVIDIA =================
    private Border NvidiaCard()
    {
        var panel = new StackPanel();
        if (!NvidiaProfileService.IsAvailable())
        {
            panel.Children.Add(FeatureHeader(Glyphs.Monitor, "Warning", "Perfil NVIDIA", "Nenhuma placa de vídeo NVIDIA com driver instalado foi encontrada neste PC.", "Indisponível", "Warning"));
            return Surface(panel);
        }
        Dictionary<uint, uint?> current;
        try { current = NvidiaProfileService.ReadCurrent(); }
        catch (InvalidOperationException ex)
        {
            panel.Children.Add(FeatureHeader(Glyphs.Monitor, "Danger", "Perfil NVIDIA", ex.Message, "Erro", "Danger"));
            return Surface(panel);
        }
        var applied = NvidiaProfileService.IsApplied();
        panel.Children.Add(FeatureHeader(Glyphs.Monitor, "Success", "Perfil NVIDIA para jogos",
            "Grava as configurações globais do Painel de Controle NVIDIA direto no driver, sem programas extras. Cada valor anterior é guardado e volta com Restaurar.",
            applied ? "Aplicado" : "Padrão do driver", applied ? "Success" : "Accent"));

        var checks = new List<(CheckBox Check, uint Id)>();
        var list = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        foreach (var s in NvidiaProfileService.Settings)
        {
            var now = current.TryGetValue(s.Id, out var v) ? v : null;
            var already = now == s.Value;
            var check = new CheckBox { IsChecked = !already };
            var row = ChoiceRow(check, s.Title, $"{s.Description}\nAtual: {s.Describe(now)}  →  Novo: {s.ValueLabel}", already ? "Já aplicado" : s.ValueLabel, already ? "Success" : "Accent");
            list.Children.Add(row);
            checks.Add((check, s.Id));
        }
        panel.Children.Add(list);

        var apply = IconButton(Glyphs.Check, "Aplicar perfil", primary: true);
        apply.Click += async (_, _) =>
        {
            var ids = checks.Where(c => c.Check.IsChecked == true).Select(c => c.Id).ToList();
            if (ids.Count == 0) { ShowToast("Perfil NVIDIA", "Marque pelo menos uma configuração.", "Warning"); return; }
            await ExecuteTrackedAsync("Aplicando perfil NVIDIA", async _ => await Task.Run(() => new NvidiaProfileService(_log).Apply(ids)));
            if (_currentPage == "gaming") ShowGaming();
        };
        var buttons = new List<Button> { apply };
        if (applied)
        {
            var restore = IconButton(Glyphs.Undo, "Restaurar");
            restore.Click += async (_, _) =>
            {
                await ExecuteTrackedAsync("Restaurando configurações NVIDIA", async _ => await Task.Run(() => new NvidiaProfileService(_log).Restore()));
                if (_currentPage == "gaming") ShowGaming();
            };
            buttons.Insert(0, restore);
        }
        var summary = Label("Vale para todos os jogos. Perfis específicos de jogo no painel da NVIDIA continuam tendo prioridade.", 12, true);
        summary.Margin = new Thickness(0); summary.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(ActionBar(summary, buttons.ToArray()));
        var card = Surface(panel);
        card.SetResourceReference(Border.BackgroundProperty, "HeroBrush");
        return card;
    }

    // ================= Windows Defender =================
    private Border DefenderCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionHeader("Windows Defender", "Exclua as pastas dos seus jogos da verificação em tempo real (seguro e recomendado) ou desligue a proteção temporariamente."));
        var body = new StackPanel();
        body.Children.Add(Label("Lendo o estado do Defender...", 12.5, true));
        panel.Children.Add(body);
        _ = FillDefenderAsync(body);
        return Surface(panel);
    }

    private async Task FillDefenderAsync(StackPanel body)
    {
        var status = await DefenderService.ReadStatusAsync();
        body.Children.Clear();
        if (!status.Available)
        {
            body.Children.Add(Label("O Windows Defender não está ativo neste PC (outro antivírus pode estar no lugar dele).", 12.5, true));
            return;
        }
        var defender = new DefenderService(_log);

        var rtp = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var toggle = IconButton(status.RealTimeOn ? Glyphs.Shield : Glyphs.Check, status.RealTimeOn ? "Desligar" : "Ligar", primary: !status.RealTimeOn);
        toggle.Click += async (_, _) =>
        {
            if (status.RealTimeOn && Msg("Desligar a proteção em tempo real deixa o PC sem verificação de vírus até ela ser religada (o Windows a religa sozinho depois de um tempo). Continuar?",
                    "Windows Defender", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            await ExecuteTrackedAsync(status.RealTimeOn ? "Desligando a proteção em tempo real" : "Ligando a proteção em tempo real", async _ => await defender.SetRealTimeAsync(!status.RealTimeOn));
            await FillDefenderAsync(body);
        };
        DockPanel.SetDock(toggle, Dock.Right); rtp.Children.Add(toggle);
        if (status.TamperProtected)
        {
            var open = IconButton(Glyphs.OpenInNew, "Segurança do Windows");
            open.Click += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("windowsdefender://threatsettings") { UseShellExecute = true }); } catch (System.ComponentModel.Win32Exception) { } };
            DockPanel.SetDock(open, Dock.Right); rtp.Children.Add(open);
        }
        rtp.Children.Add(BoostText("Proteção em tempo real", (status.RealTimeOn ? "Ligada." : "Desligada.") +
            (status.TamperProtected ? " A Proteção contra Adulteração está ativa: desative-a na Segurança do Windows para mudar por aqui." : "")));
        body.Children.Add(rtp);

        var exHead = new DockPanel { Margin = new Thickness(0, 4, 0, 8) };
        var add = IconButton(Glyphs.Add, "Adicionar pasta");
        add.Click += async (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Escolha a pasta do jogo" };
            if (dialog.ShowDialog(this) != true) return;
            await ExecuteTrackedAsync("Adicionando exclusão no Defender", async _ => await defender.AddExclusionAsync(dialog.FolderName));
            await FillDefenderAsync(body);
        };
        DockPanel.SetDock(add, Dock.Right); exHead.Children.Add(add);
        exHead.Children.Add(BoostText("Pastas excluídas", "O Defender não escaneia esses arquivos enquanto o jogo carrega."));
        body.Children.Add(exHead);
        if (status.Exclusions.Length == 0) body.Children.Add(Label("Nenhuma pasta excluída.", 12, true));
        foreach (var folder in status.Exclusions)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var remove = new Button { Content = GlyphIcon(Glyphs.Delete, 12, "DangerBrush"), Padding = new Thickness(8, 5, 8, 5), ToolTip = "Remover exclusão" };
            remove.SetResourceReference(StyleProperty, "GhostButton");
            remove.Click += async (_, _) =>
            {
                await ExecuteTrackedAsync("Removendo exclusão do Defender", async _ => await defender.RemoveExclusionAsync(folder));
                await FillDefenderAsync(body);
            };
            DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove);
            var chip = IconChip(Glyphs.Folder, "Info", 28); DockPanel.SetDock(chip, Dock.Left); row.Children.Add(chip);
            var path = Label(folder, 12); path.Margin = new Thickness(10, 0, 0, 0); path.VerticalAlignment = VerticalAlignment.Center; path.Tag = Translator.SystemDataTag; path.TextTrimming = TextTrimming.CharacterEllipsis; path.TextWrapping = TextWrapping.NoWrap;
            row.Children.Add(path);
            body.Children.Add(row);
        }
    }
}
