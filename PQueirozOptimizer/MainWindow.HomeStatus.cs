using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using PQueirozOptimizer.Engine;
using PQueirozOptimizer.Models;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>
/// Terceira linha do Início: o Windows e o espaço de cada disco, e o estado das otimizações (modo ativo, última
/// execução, ajustes confirmados na última análise e backup pendente). Tudo lido de arquivos e do Windows;
/// o que nunca aconteceu aparece como "Nenhuma ainda", não como zero inventado.
/// </summary>
public partial class MainWindow
{
    internal sealed record HomeOptimizationStatus(string Mode, DateTime? LastRun, int? LastRunVerified, int? LastRunTotal, DateTime? AnalysisAt, int? Applied, int? Recommended, int PendingBackupItems, DateTime? BackupAt);

    private UIElement AuroraStatusRow(SystemSnapshot snapshot)
    {
        var grid = HomeGrid(300);
        grid.MaxColumns = 2;
        _ = FillStatusRowAsync(grid, snapshot);
        return grid;
    }

    /// <summary>Discos e arquivos do histórico são lidos fora da interface; os cards entram quando a leitura termina.</summary>
    private async Task FillStatusRowAsync(Panel grid, SystemSnapshot snapshot)
    {
        var mode = _configService.Config.ActiveProfile;
        var (drives, status) = await Task.Run(() => (FixedDrives(), ReadOptimizationStatus(mode, SnapshotDirectory, SmartHistory.Default)));
        // O Início pode ter sido redesenhado (ou a página trocada) durante a leitura
        if (!IsInContent(grid)) return;
        grid.Children.Add(AuroraSystemCard(snapshot, drives));
        grid.Children.Add(Mark(AuroraOptimizationCard(status), "home.status"));
    }

    // ================= Este PC =================
    private Border AuroraSystemCard(SystemSnapshot snapshot, List<(string Name, double Free, double Total)> drives)
    {
        var body = new StackPanel();
        body.Children.Add(HomeBandTitle("Este PC", false));
        var windows = string.IsNullOrWhiteSpace(snapshot.Build) ? snapshot.OperatingSystem : $"{snapshot.OperatingSystem} · build {snapshot.Build}";
        body.Children.Add(StatusLine("Windows", windows, data: true));
        body.Children.Add(StatusLine("Processador", ShortCpuName(snapshot.Processor), data: true));
        body.Children.Add(StatusLine("Placa de vídeo", FirstGpu(snapshot.Graphics), data: true));
        body.Children.Add(StatusLine("Memória", Gb(snapshot.MemoryGb), data: true));

        var disks = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        foreach (var drive in drives)
        {
            var used = drive.Total > 0 ? 100 - drive.Free * 100 / drive.Total : 0;
            var line = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var free = new TextBlock { Text = Tr($"{FormatGb(drive.Free)} livres de {FormatGb(drive.Total)}"), FontSize = 12, Tag = Translator.SystemDataTag };
            free.SetResourceReference(TextBlock.ForegroundProperty, used >= 90 ? "WarningBrush" : "MutedBrush");
            DockPanel.SetDock(free, Dock.Right); line.Children.Add(free);
            var name = new TextBlock { Text = drive.Name, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Tag = Translator.SystemDataTag };
            name.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            line.Children.Add(name);
            disks.Children.Add(line);
            var bar = new ProgressBar { Value = used, Maximum = 100, Height = 4, Margin = new Thickness(0, 4, 0, 0) };
            bar.SetResourceReference(ForegroundProperty, used >= 90 ? "WarningBrush" : "AccentBrush");
            disks.Children.Add(bar);
        }
        if (disks.Children.Count == 0) disks.Children.Add(Label("Nenhum disco local lido.", 12, true));
        body.Children.Add(disks);
        return HomeCard(body);
    }

    private static string FormatGb(double gb) => gb >= 1000 ? (gb / 1024).ToString("0.0", CultureInfo.CurrentCulture) + " TB" : gb.ToString("0", CultureInfo.CurrentCulture) + " GB";

    /// <summary>Discos fixos prontos (rede e removíveis ficam de fora: podem demorar a responder).</summary>
    private static List<(string Name, double Free, double Total)> FixedDrives()
    {
        var list = new List<(string, double, double)>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady || drive.TotalSize <= 0) continue;
                list.Add((drive.Name.TrimEnd('\\'), drive.AvailableFreeSpace / 1073741824d, drive.TotalSize / 1073741824d));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return list;
    }

    // ================= Otimizações =================
    /// <summary>Lê o estado das otimizações dos arquivos do app. Arquivo ausente ou ilegível = sem dado.</summary>
    internal static HomeOptimizationStatus ReadOptimizationStatus(string activeMode, string snapshotDirectory, SmartHistory history)
    {
        SmartHistory.Run? lastRun = null; SmartHistory.AnalysisMark? analysis = null;
        try { lastRun = history.Runs.OrderByDescending(r => r.At).FirstOrDefault(); analysis = history.LastAnalysis; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        var pending = 0; DateTime? backupAt = null;
        try
        {
            var latest = Path.Combine(snapshotDirectory, "ultima_otimizacao.json");
            if (File.Exists(latest))
            {
                using var json = JsonDocument.Parse(File.ReadAllText(latest));
                if (json.RootElement.TryGetProperty("Itens", out var items) && items.ValueKind == JsonValueKind.Array) pending = items.GetArrayLength();
                if (json.RootElement.TryGetProperty("Data", out var date) && date.ValueKind == JsonValueKind.String
                    && DateTime.TryParse(date.GetString(), CultureInfo.GetCultureInfo("pt-BR"), DateTimeStyles.AssumeLocal, out var parsed)) backupAt = parsed;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return new(activeMode, lastRun?.At, lastRun?.Verified.Length, lastRun?.Selected.Length, analysis?.At, analysis?.Applied, analysis?.Recommended, pending, backupAt);
    }

    private Border AuroraOptimizationCard(HomeOptimizationStatus status)
    {
        var body = new StackPanel();
        var history = RoundIcon(Glyphs.History, "Histórico e reversão", () => NavigateTo("history"));
        body.Children.Add(HomeBandTitle("Otimizações", false, history));
        body.Children.Add(StatusLine("Modo ativo", ProfileDisplayName(status.Mode)));
        body.Children.Add(StatusLine("Última otimização", status.LastRun is { } at
            ? Tr($"{at:dd/MM/yyyy HH:mm} · {status.LastRunVerified} de {status.LastRunTotal} confirmados")
            : Tr("Nenhuma ainda"), data: status.LastRun is not null));
        body.Children.Add(StatusLine("Ajustes em vigor", status.AnalysisAt is { } analyzed
            ? Tr($"{status.Applied} aplicados · {status.Recommended} recomendados (análise de {analyzed:dd/MM})")
            : Tr("Analise o PC para saber"), data: status.AnalysisAt is not null));
        body.Children.Add(StatusLine("Backup para desfazer", status.PendingBackupItems > 0
            ? Tr($"{status.PendingBackupItems} itens" + (status.BackupAt is { } b ? $" · {b:dd/MM/yyyy}" : ""))
            : Tr("Nenhum pendente"), data: status.PendingBackupItems > 0));
        var analyze = IconButton(Glyphs.Lightning, "Otimização inteligente");
        analyze.Margin = new Thickness(0, 14, 0, 0); analyze.HorizontalAlignment = HorizontalAlignment.Left;
        analyze.Click += (_, _) => NavigateTo("smart");
        body.Children.Add(analyze);
        return HomeCard(body);
    }

    private DockPanel StatusLine(string label, string value, bool data = false)
    {
        var line = new DockPanel { Margin = new Thickness(0, 7, 0, 0) };
        var name = new TextBlock { Text = Tr(label), FontSize = 12.5, Margin = new Thickness(0, 0, 12, 0) };
        name.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        DockPanel.SetDock(name, Dock.Left); line.Children.Add(name);
        var text = new TextBlock { Text = value, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Right, TextTrimming = TextTrimming.CharacterEllipsis };
        if (data) text.Tag = Translator.SystemDataTag;
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        line.Children.Add(text);
        return line;
    }
}
