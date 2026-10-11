using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using PQueirozOptimizer.Controls;
using PQueirozOptimizer.Models;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>
/// Meu PC: o computador por inteiro — sistema, indicadores de saúde com a explicação de cada um, especificações
/// (processador, placa de vídeo, memória, placa-mãe e BIOS, armazenamento, rede), uso ao vivo e atalhos de
/// manutenção. Só leitura: nada é alterado nesta página. "Copiar resumo" ajuda a pedir suporte.
/// </summary>
public partial class MainWindow
{
    private sealed record MyPcData(SystemSnapshot Snapshot, CpuInfo Cpu, MemoryInfo Memory, IReadOnlyList<DriveStat> Drives, NetworkInfo Network,
        BoardInfo Board, IReadOnlyList<GpuAdapter> Gpus, OsInfo Os);

    private MyPcData? _myPcData;

    private void ShowMyPc() => _ = RenderMyPcAsync();

    private async Task RenderMyPcAsync()
    {
        PageTitle.Text = "Meu PC";
        PageBadge.Visibility = Visibility.Collapsed;
        var root = new StackPanel();
        ContentHost.Children.Clear(); ContentHost.Children.Add(root);
        _homeLive.Clear();
        _homeRoot = root;
        AddPageAction(Glyphs.Copy, "Copiar resumo", () => { if (_myPcData is { } d) CopyText(MyPcSummary(d), Tr("Resumo do PC copiado. Cole no ticket de suporte ou onde precisar.")); })
            .ToolTip = Tr("Copia processador, placa de vídeo, memória, placa-mãe, sistema e armazenamento em texto");
        AddPageAction(Glyphs.Refresh, "Atualizar", () => { HomeData.Invalidate(); _snapshotReadAt = default; _myPcData = null; ShowMyPc(); });
        try
        {
            var read = Task.Run(() => (Cpu: HomeData.Cpu(), Memory: HomeData.Memory(), Drives: HomeData.Drives(), Network: HomeData.Network(), Board: HomeData.Board(), Gpus: HomeData.Gpus(), Os: HomeData.Os()));
            var snapshotTask = ReadSnapshotAsync();
            var both = Task.WhenAll(read, snapshotTask);
            if (await Task.WhenAny(both, Task.Delay(150)) != both) root.Children.Add(HomeSkeleton());
            var snapshot = await snapshotTask; var x = await read;
            if (_currentPage != "mypc" || !ContentHost.Children.Contains(root)) return;
            var data = _myPcData = new MyPcData(snapshot, x.Cpu, x.Memory, x.Drives, x.Network, x.Board, x.Gpus, x.Os);
            root.Children.Clear();
            var (score, headline, tone) = HealthScore(snapshot);

            // 1. Resumo do computador e indicadores explicados
            var top = HomeGrid(420);
            top.AllowedColumns = new[] { 1, 2 };
            top.Children.Add(MyPcOverview(data, score, headline));
            top.Children.Add(MyPcHealth(snapshot, score, tone));
            root.Children.Add(top);

            // 2. Especificações
            root.Children.Add(HomeSectionTitle("Especificações", "Lidas agora do Windows e do driver de cada componente."));
            var specs = HomeGrid(300);
            specs.AllowedColumns = new[] { 1, 2, 3 };
            specs.MaxColumns = 3;
            specs.Children.Add(CpuSpecs(data));
            specs.Children.Add(GpuSpecs(data));
            specs.Children.Add(MemorySpecs(data));
            specs.Children.Add(BoardSpecs(data));
            specs.Children.Add(SystemSpecs(data));
            specs.Children.Add(NetworkSpecs(data));
            root.Children.Add(specs);

            // 3. Armazenamento e manutenção
            var bottom = HomeGrid(300);
            bottom.AllowedColumns = new[] { 1, 2 };
            bottom.Children.Add(StorageCard(data.Drives));
            bottom.Children.Add(MaintenanceCard());
            root.Children.Add(bottom);

            if (HardwareMonitorService.Shared.Latest is { } latest) foreach (var update in _homeLive) update(latest);
            AnimatePageIn();
        }
        catch (Exception ex)
        {
            root.Children.Clear();
            root.Children.Add(Card("NÃO FOI POSSÍVEL CONSULTAR O SISTEMA", ex.Message, Glyphs.Warning, "WarningBrush"));
            _log.Write("ERROR", "Meu PC: " + ex.Message);
        }
    }

    private Border MyPcOverview(MyPcData data, int score, string headline)
    {
        var grid = new Grid();
        grid.Children.Add(new QrzArt { Kind = ArtKind.Squares, Anchor = 1, Margin = new Thickness(-20) });
        var dock = new DockPanel();
        var ring = ScoreRing(score, Tr("SAÚDE"), 104 * AppearanceService.CardScale);
        ring.VerticalAlignment = VerticalAlignment.Top; ring.Margin = new Thickness(16, 0, 0, 0);
        ring.ToolTip = string.Format(Tr("Pontuação de saúde: {0} de 100"), score);
        DockPanel.SetDock(ring, Dock.Right); dock.Children.Add(ring);
        var stack = new StackPanel();
        var welcome = new TextBlock { Text = Tr("Seu computador"), FontSize = 13 };
        welcome.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        stack.Children.Add(welcome);
        var name = new TextBlock { Text = Environment.MachineName, FontSize = 28 * AppearanceService.CardScale, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis, Tag = Translator.SystemDataTag };
        name.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        stack.Children.Add(name);
        var line = new TextBlock { Text = Tr(headline), FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 14), TextWrapping = TextWrapping.Wrap };
        line.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextBrush");
        stack.Children.Add(line);
        var s = data.Snapshot;
        stack.Children.Add(DetailRow(Glyphs.Monitor, Tr("Sistema"), s.OperatingSystem.Replace("Microsoft ", "") + (data.Os.DisplayVersion is { } dv ? $" {dv}" : "")));
        stack.Children.Add(DetailRow(Glyphs.Tag, Tr("Build"), $"{s.Build} · {s.Architecture}"));
        stack.Children.Add(DetailRow(Glyphs.Clock, Tr("Tempo ligado"), s.UptimeText));
        stack.Children.Add(DetailRow(Glyphs.Shield, Tr("Permissão"), s.IsAdministrator ? Tr("Administrador") : Tr("Usuário"), s.IsAdministrator ? "SuccessBrush" : "WarningBrush", systemValue: false));
        if (data.Os.InstalledAt is { } installed) stack.Children.Add(DetailRow(Glyphs.Download, Tr("Windows instalado em"), installed.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture)));
        dock.Children.Add(stack);
        grid.Children.Add(dock);
        return HomeCard(grid, "HeroBrush");
    }

    /// <summary>Os quatro indicadores do card Meu PC, agora com a explicação (evidência) de cada um.</summary>
    private Border MyPcHealth(SystemSnapshot snapshot, int score, string tone)
    {
        var stack = new StackPanel();
        stack.Children.Add(CardHeader(Glyphs.HeartPulse, Tr("Saúde"), Tr("Indicadores do computador"), OpenButton(Tr("Abrir a Central de diagnóstico"), () => NavigateTo("diagnostics")), titleIsData: false));
        var list = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        stack.Children.Add(list);
        var placeholder = Label("Verificando...", 12.5, true);
        list.Children.Add(placeholder);
        HomeData.HealthAsync(snapshot, score, tone).ContinueWith(task => Dispatcher.BeginInvoke(() =>
        {
            list.Children.Clear();
            if (task.IsFaulted || task.Result is not { } indicators) { list.Children.Add(Label("Não foi possível ler os indicadores agora.", 12.5, true)); return; }
            foreach (var indicator in indicators)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
                var dot = StatusDot(indicator.Tone, 9); dot.Margin = new Thickness(0, 5, 12, 0); dot.VerticalAlignment = VerticalAlignment.Top;
                row.Children.Add(dot);
                var texts = new StackPanel();
                var head = new DockPanel();
                var status = new TextBlock { Text = Tr(indicator.Status), FontSize = 12.5, FontWeight = FontWeights.SemiBold, Tag = Translator.SystemDataTag };
                status.SetResourceReference(TextBlock.ForegroundProperty, indicator.Tone is "Muted" ? "MutedBrush" : indicator.Tone + "Brush");
                DockPanel.SetDock(status, Dock.Right); head.Children.Add(status);
                var title = new TextBlock { Text = Tr(indicator.Title), FontSize = 13.5, FontWeight = FontWeights.SemiBold };
                title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                head.Children.Add(title);
                texts.Children.Add(head);
                var detail = new TextBlock { Text = Tr(indicator.Detail), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0), Tag = Translator.SystemDataTag };
                detail.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                texts.Children.Add(detail);
                row.Children.Add(texts);
                list.Children.Add(row);
            }
        }), TaskScheduler.Default);
        return HomeCard(stack);
    }

    private Border SpecCard(string glyph, string label, string title, IEnumerable<UIElement> rows)
    {
        var stack = new StackPanel();
        stack.Children.Add(CardHeader(glyph, label, title));
        var body = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        foreach (var row in rows) body.Children.Add(row);
        stack.Children.Add(body);
        return HomeCard(stack);
    }

    private Border CpuSpecs(MyPcData d)
    {
        var (_, now) = LiveRow(Glyphs.Speed, Tr("Frequência atual"), "--");
        var (_, use) = LiveRow(Glyphs.AreaChart, Tr("Uso agora"), "--");
        _homeLive.Add(s =>
        {
            now.Text = s.CpuMhz is { } mhz ? (mhz / 1000).ToString("0.00", CultureInfo.CurrentCulture) + " GHz" : Tr("Indisponível");
            use.Text = Percent(s.Cpu);
        });
        return SpecCard(Glyphs.Chip, Tr("Processador"), ShortCpuName(d.Snapshot.Processor), new UIElement[]
        {
            DetailRow(Glyphs.Info, Tr("Nome completo"), d.Snapshot.Processor),
            DetailRow(Glyphs.Chip, Tr("Núcleos / threads"), d.Cpu.Cores > 0 ? $"{d.Cpu.Cores} / {d.Cpu.Threads}" : d.Cpu.Threads.ToString()),
            DetailRow(Glyphs.Clock, Tr("Frequência base"), d.Cpu.BaseMhz is { } b ? (b / 1000).ToString("0.00", CultureInfo.CurrentCulture) + " GHz" : Tr("Indisponível")),
            (UIElement)now.Parent, (UIElement)use.Parent,
            DetailRow(Glyphs.Thermometer, Tr("Temperatura"), Tr("Indisponível")),
        });
    }

    private Border GpuSpecs(MyPcData d)
    {
        var main = d.Gpus.FirstOrDefault();
        var (_, vram) = LiveRow(Glyphs.Memory, Tr("Memória de vídeo"), "--");
        var (_, temp) = LiveRow(Glyphs.Thermometer, Tr("Temperatura"), "--");
        var (_, use) = LiveRow(Glyphs.AreaChart, Tr("Uso agora"), "--");
        _homeLive.Add(s =>
        {
            vram.Text = s.VramUsedGb is { } used && s.VramTotalGb is { } total ? $"{used.ToString("0.0", CultureInfo.CurrentCulture)} / {Gb(total)}" : s.VramTotalGb is { } t2 ? Gb(t2) : Tr("Indisponível");
            temp.Text = s.GpuTempC is { } t ? $"{t:0} °C" : Tr("Indisponível");
            use.Text = s.Gpu is { } g ? Percent(g) : Tr("Indisponível");
        });
        var rows = new List<UIElement>
        {
            DetailRow(Glyphs.Tag, Tr("Driver"), main?.DriverVersion ?? Tr("Indisponível")),
            DetailRow(Glyphs.Clock, Tr("Data do driver"), main?.DriverDate is { } date ? date.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture) : Tr("Indisponível")),
            (UIElement)vram.Parent, (UIElement)temp.Parent, (UIElement)use.Parent,
        };
        foreach (var other in d.Gpus.Skip(1)) rows.Add(DetailRow(Glyphs.Monitor, Tr("Outra placa"), other.Name));
        return SpecCard(Glyphs.Monitor, Tr("Placa de vídeo"), main is { } m ? FirstGpu(m.Name) : FirstGpu(d.Snapshot.Graphics), rows);
    }

    private Border MemorySpecs(MyPcData d)
    {
        var (_, used) = LiveRow(Glyphs.AreaChart, Tr("Em uso agora"), "--");
        _homeLive.Add(s => used.Text = $"{Gb(s.RamUsedGb)} ({Percent(s.Ram)})");
        return SpecCard(Glyphs.Memory, Tr("Memória"), $"{Gb(d.Snapshot.MemoryGb)}" + (d.Memory.Type is { } type ? " " + type : ""), new UIElement[]
        {
            DetailRow(Glyphs.Memory, Tr("Total instalado"), Gb(d.Snapshot.MemoryGb)),
            DetailRow(Glyphs.Tag, Tr("Tipo"), d.Memory.Type ?? Tr("Indisponível")),
            DetailRow(Glyphs.Speed, Tr("Velocidade"), d.Memory.SpeedMts is { } mts ? $"{mts} MT/s" : Tr("Indisponível")),
            DetailRow(Glyphs.Apps, Tr("Módulos"), d.Memory.Modules > 0 ? d.Memory.Modules.ToString(CultureInfo.CurrentCulture) : Tr("Indisponível")),
            (UIElement)used.Parent,
        });
    }

    private Border BoardSpecs(MyPcData d)
    {
        var b = d.Board;
        var title = string.Join(" ", new[] { b.Manufacturer, b.Product }.Where(v => !string.IsNullOrWhiteSpace(v)));
        return SpecCard(Glyphs.Chip, Tr("Placa-mãe e BIOS"), title.Length > 0 ? title : Tr("Indisponível"), new UIElement[]
        {
            DetailRow(Glyphs.Devices, Tr("Fabricante"), b.Manufacturer ?? Tr("Indisponível")),
            DetailRow(Glyphs.Tag, Tr("Modelo"), b.Product ?? Tr("Indisponível")),
            DetailRow(Glyphs.Chip, Tr("BIOS"), string.Join(" ", new[] { b.BiosVendor, b.BiosVersion }.Where(v => !string.IsNullOrWhiteSpace(v))) is { Length: > 0 } bios ? bios : Tr("Indisponível")),
            DetailRow(Glyphs.Clock, Tr("Data da BIOS"), b.BiosDate is { } date ? date.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture) : Tr("Indisponível")),
        });
    }

    private Border SystemSpecs(MyPcData d)
    {
        var s = d.Snapshot;
        return SpecCard(Glyphs.Laptop, Tr("Sistema"), s.OperatingSystem.Replace("Microsoft ", ""), new UIElement[]
        {
            DetailRow(Glyphs.Info, Tr("Versão"), d.Os.DisplayVersion ?? Tr("Indisponível")),
            DetailRow(Glyphs.Tag, Tr("Build"), s.Build),
            DetailRow(Glyphs.Code, Tr("Arquitetura"), s.Architecture),
            DetailRow(Glyphs.Person, Tr("Nome do computador"), Environment.MachineName),
            DetailRow(Glyphs.Clock, Tr("Tempo ligado"), s.UptimeText),
        });
    }

    private Border NetworkSpecs(MyPcData d)
    {
        var (_, down) = LiveRow(Glyphs.Download, Tr("Download agora"), "--");
        var (_, up) = LiveRow(Glyphs.Upload, Tr("Upload agora"), "--");
        var (_, ping) = LiveRow(Glyphs.Globe, Tr("Latência (1.1.1.1)"), "--");
        _homeLive.Add(s =>
        {
            down.Text = FormatRate(s.DownBytesPerSec); up.Text = FormatRate(s.UpBytesPerSec);
            ping.Text = s.PingMs is { } p ? $"{p:0} ms" : Tr("Indisponível");
        });
        return SpecCard(d.Network.Kind == "Wi-Fi" ? Glyphs.Wifi : Glyphs.Ethernet, Tr("Rede"), d.Network.Name ?? Tr("Nenhum adaptador conectado"), new UIElement[]
        {
            DetailRow(Glyphs.Network, Tr("Tipo"), d.Network.Kind.Length > 0 ? d.Network.Kind : Tr("Indisponível")),
            DetailRow(Glyphs.Check, Tr("Estado"), d.Network.Connected ? Tr("Conectado") : Tr("Sem conexão"), d.Network.Connected ? "SuccessBrush" : "DangerBrush", systemValue: false),
            (UIElement)down.Parent, (UIElement)up.Parent, (UIElement)ping.Parent,
        });
    }

    private Border MaintenanceCard()
    {
        var stack = new StackPanel();
        stack.Children.Add(CardHeader(Glyphs.Repair, Tr("Manutenção"), Tr("Atalhos de manutenção"), titleIsData: false));
        var list = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        AddHealthLink(list, "Diagnóstico inteligente", Glyphs.Diagnostic, "diagnostics", "Diagnóstico com evidências e grau de confiança");
        AddHealthLink(list, "Drivers", Glyphs.Devices, "drivers", "Verificar dispositivos e drivers");
        AddHealthLink(list, "Serviços", Glyphs.Services, "services", "Abrir grupos de serviços");
        AddHealthLink(list, "Windows Update", Glyphs.Refresh, "fixes", "Reparar o Windows Update", "windows-update");
        AddHealthLink(list, "Bluetooth", Glyphs.Bluetooth, "fixes", "Reparar o Bluetooth", "bluetooth");
        AddHealthLink(list, "Pontos de restauração", Glyphs.Restore2, "restore", "Criar ou restaurar um ponto de restauração");
        stack.Children.Add(list);
        return HomeCard(stack);
    }

    /// <summary>Resumo em texto (para colar num ticket de suporte), no idioma da interface.</summary>
    private static string MyPcSummary(MyPcData d)
    {
        var s = d.Snapshot;
        var text = new StringBuilder();
        void Line(string label, string? value) { if (!string.IsNullOrWhiteSpace(value)) text.AppendLine($"{Translator.Tr(label)}: {value}"); }
        text.AppendLine($"Qrztweaks · {Environment.MachineName}");
        Line("Sistema", $"{s.OperatingSystem} {d.Os.DisplayVersion} (build {s.Build}, {s.Architecture})".Replace("  ", " "));
        Line("Processador", $"{s.Processor} · {d.Cpu.Cores}/{d.Cpu.Threads}");
        foreach (var g in d.Gpus) Line("Placa de vídeo", $"{g.Name}" + (g.DriverVersion is { } v ? $" · driver {v}" : "") + (g.DriverDate is { } dd ? $" ({dd:dd/MM/yyyy})" : ""));
        Line("Memória", $"{s.MemoryGb:0.#} GB" + (d.Memory.Type is { } t ? $" {t}" : "") + (d.Memory.SpeedMts is { } mts ? $" · {mts} MT/s" : "") + (d.Memory.Modules > 0 ? $" · {d.Memory.Modules} " + Translator.Tr("módulo(s)") : ""));
        Line("Placa-mãe", string.Join(" ", new[] { d.Board.Manufacturer, d.Board.Product }.Where(x => !string.IsNullOrWhiteSpace(x))));
        Line("BIOS", string.Join(" ", new[] { d.Board.BiosVendor, d.Board.BiosVersion, d.Board.BiosDate?.ToString("dd/MM/yyyy") }.Where(x => !string.IsNullOrWhiteSpace(x))));
        foreach (var drive in d.Drives) Line("Unidade", $"{drive.Name} {drive.MediaType} · {drive.FreeGb:0} GB {Translator.Tr("livres de")} {drive.TotalGb:0} GB".Replace("  ", " "));
        Line("Rede", d.Network.Name is { } n ? $"{d.Network.Kind} {n}".Trim() : null);
        return text.ToString();
    }
}
