using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

public partial class DiskLensWindow : Window
{
    private readonly ActivityLog _log;
    private CancellationTokenSource? _operation;
    private DiskLensScan? _scan;
    private DiskLensFolder? _folder;
    private List<FileRow> _large = new(), _duplicates = new();
    private bool _closed, _cleaning;
    private sealed class FileRow(DiskLensFile file, int group = 0, DiskLensFile? keep = null, string? hash = null)
    {
        public DiskLensFile File { get; } = file;
        public bool Selected { get; set; }
        public int Group { get; } = group;
        public string KeepPath => Keep?.Path ?? "";
        public DiskLensFile? Keep { get; } = keep;
        public string? Hash { get; } = hash;
    }
    private sealed record Tile(string Name, long Size, DiskLensFolder? Folder, string Path);

    public DiskLensWindow(ActivityLog log)
    {
        InitializeComponent(); _log = log;
        PathBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_cleaning) { e.Cancel = true; StatusText.Text = "Aguarde o envio dos arquivos à Lixeira terminar."; return; }
        _closed = true; _operation?.Cancel();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Escolha a pasta ou unidade para analisar", Multiselect = false };
        if (dialog.ShowDialog(this) == true) PathBox.Text = dialog.FolderName;
    }
    private async void Scan_Click(object sender, RoutedEventArgs e) => await Analyze();
    private void Cancel_Click(object sender, RoutedEventArgs e) => _operation?.Cancel();

    private async Task Run(Func<CancellationToken, Task> work)
    {
        if (_operation is not null) return;
        using var cancellation = new CancellationTokenSource(); _operation = cancellation;
        SetBusy(true);
        try { await work(cancellation.Token); }
        catch (OperationCanceledException) { if (!_closed) StatusText.Text = "Operação cancelada. Nenhum resultado parcial foi usado para limpeza."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            _log.Write("ERROR", "DiskLens: " + ex.Message);
            if (!_closed) StatusText.Text = "Não foi possível concluir: " + ex.Message;
        }
        finally { _operation = null; if (!_closed) SetBusy(false); }
    }

    private void SetBusy(bool busy)
    {
        ScanButton.IsEnabled = BrowseButton.IsEnabled = PathBox.IsEnabled = !busy;
        DuplicateButton.IsEnabled = !busy && _scan is not null;
        CleanButton.IsEnabled = !busy && _scan is not null;
        FilesGrid.IsEnabled = DuplicatesGrid.IsEnabled = !busy;
        CancelButton.IsEnabled = busy && !_cleaning;
        ProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task Analyze()
    {
        var path = PathBox.Text.Trim();
        await Run(async token =>
        {
            _scan = null; _folder = null; _large.Clear(); _duplicates.Clear();
            FilesGrid.ItemsSource = DuplicatesGrid.ItemsSource = FoldersGrid.ItemsSource = null;
            MapCanvas.Children.Clear(); DuplicatesText.Text = "Inicie a busca após analisar. Nenhum arquivo é marcado automaticamente.";
            SummaryText.Text = "Analisando armazenamento…";
            var progress = new Progress<DiskLensProgress>(p =>
            {
                if (_closed || token.IsCancellationRequested) return;
                SummaryText.Text = $"{p.Files:N0} arquivos · {DiskLensService.FormatSize(p.Bytes)}";
                StatusText.Text = p.Current;
            });
            var scan = await Task.Run(() => DiskLensService.Scan(path, token, progress), token);
            if (_closed) return;
            LoadScan(scan);
            StatusText.Text = "Análise concluída. Duplo clique em um arquivo revela sua localização no Explorer.";
        });
    }

    internal void LoadScan(DiskLensScan scan)
    {
        _scan = scan; _folder = scan.Root;
        PathBox.Text = scan.Root.Path;
        _large = scan.Files.OrderByDescending(f => f.Size).Take(1500).Select(f => new FileRow(f)).ToList();
        FilesGrid.ItemsSource = _large;
        FoldersGrid.ItemsSource = scan.Folders.Where(f => f != scan.Root).OrderByDescending(f => f.Size).Take(500).ToList();
        SummaryText.Text = $"{DiskLensService.FormatSize(scan.Root.Size)} · {scan.Files.Count:N0} arquivos · {scan.Folders.Count:N0} pastas";
        DetailText.Text = $"Tamanhos lógicos; arquivos compactados e links físicos podem ocupar menos no disco. {scan.Skipped:N0} caminhos inacessíveis ou links ignorados. Listas: 1.500 maiores arquivos e 500 pastas.";
        DrawMap(); SetBusy(_operation is not null);
    }

    private async void Duplicates_Click(object sender, RoutedEventArgs e)
    {
        if (_scan is not { } scan) return;
        await Run(async token =>
        {
            _duplicates.Clear(); DuplicatesGrid.ItemsSource = null; Tabs.SelectedItem = DuplicatesTab;
            DuplicatesText.Text = "Comparando conteúdo…";
            var watch = Stopwatch.StartNew();
            IProgress<string> progress = new Progress<string>(path => { if (!_closed && !token.IsCancellationRequested) StatusText.Text = path; });
            // Limita avisos antes de entrar no Dispatcher.
            var throttled = new CallbackProgress<string>(path => { if (watch.ElapsedMilliseconds >= 250) { progress.Report(path); watch.Restart(); } });
            var groups = await Task.Run(() => DiskLensService.FindDuplicates(scan, token, throttled), token);
            if (_closed) return;
            ShowDuplicates(groups);
            StatusText.Text = "Busca concluída. Revise cada caminho: arquivos iguais podem ser necessários a programas diferentes.";
        });
    }

    internal void ShowDuplicates(IReadOnlyList<DiskLensDuplicates> groups)
    {
        _duplicates = groups.SelectMany((g, index) => g.Files.Skip(1).Select(f => new FileRow(f, index + 1, g.Files[0], g.Hash))).ToList();
        DuplicatesGrid.ItemsSource = _duplicates;
        DuplicatesText.Text = $"{groups.Count:N0} grupos · {_duplicates.Count:N0} cópias adicionais · {DiskLensService.FormatSize(groups.Sum(g => g.RedundantBytes))} lógicos. Uma cópia por grupo fica preservada; nada é marcado automaticamente.";
    }

    private sealed class CallbackProgress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }

    private void Map_SizeChanged(object sender, SizeChangedEventArgs e) => DrawMap();
    private void Up_Click(object sender, RoutedEventArgs e)
    {
        if (_folder is null || _scan is null || _folder == _scan.Root) return;
        _folder = _scan.Folders.FirstOrDefault(f => f.Folders.Contains(_folder)) ?? _scan.Root; DrawMap();
    }
    private void Folder_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FoldersGrid.SelectedItem is DiskLensFolder folder) { _folder = folder; Tabs.SelectedIndex = 0; DrawMap(); }
    }
    private void File_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source)
        {
            for (var item = source; item is not null; item = VisualTreeHelper.GetParent(item))
                if (item is CheckBox) return;
        }
        if (((DataGrid)sender).SelectedItem is not FileRow row) return;
        try
        {
            if (File.Exists(row.File.Path)) Process.Start(new ProcessStartInfo(SystemTools.Resolve("explorer.exe"), "/select,\"" + row.File.Path + "\"") { UseShellExecute = true })?.Dispose();
        }
        catch (Win32Exception ex) { StatusText.Text = ex.Message; }
    }

    private void DrawMap()
    {
        if (MapCanvas is null) return;
        MapCanvas.Children.Clear();
        if (_folder is null) return;
        MapPath.Text = _folder.Path; UpButton.IsEnabled = _folder != _scan?.Root;
        var all = _folder.Folders.Select(f => new Tile(f.Name + "/", f.Size, f, f.Path))
            .Concat(_folder.Files.Select(f => new Tile(f.Name, f.Size, null, f.Path))).Where(t => t.Size > 0).OrderByDescending(t => t.Size).ToList();
        var tiles = all.Take(40).ToList();
        if (all.Count > 40) tiles.Add(new("Outros itens", all.Skip(40).Sum(t => t.Size), null, _folder.Path));
        if (tiles.Count == 0) { MapCanvas.Children.Add(new TextBlock { Text = "Esta pasta está vazia ou os arquivos não puderam ser lidos.", Margin = new Thickness(20) }); return; }
        var width = MapCanvas.ActualWidth; var height = MapCanvas.ActualHeight;
        if (width < 1 || height < 1) return;
        Paint(tiles, new Rect(0, 0, width, height));
    }

    private void Paint(List<Tile> tiles, Rect area)
    {
        if (tiles.Count > 1)
        {
            double half = tiles.Sum(t => (double)t.Size) / 2, sum = 0; int split = 0;
            do { sum += tiles[split++].Size; } while (sum < half && split < tiles.Count - 1);
            double ratio = sum / (half * 2);
            bool horizontal = area.Width >= area.Height;
            var first = horizontal ? new Rect(area.X, area.Y, area.Width * ratio, area.Height) : new Rect(area.X, area.Y, area.Width, area.Height * ratio);
            var second = horizontal ? new Rect(first.Right, area.Y, area.Width - first.Width, area.Height) : new Rect(area.X, first.Bottom, area.Width, area.Height - first.Height);
            Paint(tiles.Take(split).ToList(), first); Paint(tiles.Skip(split).ToList(), second); return;
        }
        var tile = tiles[0];
        string[] colors = { "#344B78", "#385D6A", "#4E4977", "#3C655F", "#635078", "#52658B" };
        var color = colors[(int)((uint)StringComparer.OrdinalIgnoreCase.GetHashCode(tile.Path) % colors.Length)];
        var box = new Border { Width = Math.Max(0, area.Width - 3), Height = Math.Max(0, area.Height - 3), CornerRadius = new CornerRadius(7),
            Background = (Brush)new BrushConverter().ConvertFromString(color)!, ClipToBounds = true,
            ToolTip = tile.Path + "\n" + DiskLensService.FormatSize(tile.Size), Cursor = tile.Folder is null ? Cursors.Arrow : Cursors.Hand };
        if (area.Width > 75 && area.Height > 44)
        {
            var label = new StackPanel { Margin = new Thickness(12) };
            label.Children.Add(new TextBlock { Text = tile.Name, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            label.Children.Add(new TextBlock { Text = DiskLensService.FormatSize(tile.Size), Foreground = Brushes.White, Opacity = .75, Margin = new Thickness(0, 5, 0, 0) }); box.Child = label;
        }
        box.MouseLeftButtonUp += (_, _) => { if (tile.Folder is not null) { _folder = tile.Folder; DrawMap(); } };
        Canvas.SetLeft(box, area.X); Canvas.SetTop(box, area.Y); MapCanvas.Children.Add(box);
    }

    private async void Clean_Click(object sender, RoutedEventArgs e)
    {
        if ((Application.Current as App)?.ActiveLicense is null)
        { StatusText.Text = "Ative sua licença para limpar arquivos. A análise permanece disponível."; return; }
        FilesGrid.CommitEdit(); DuplicatesGrid.CommitEdit();
        var selected = _large.Concat(_duplicates).Where(r => r.Selected).ToArray();
        if (selected.Length == 0) { StatusText.Text = "Marque os arquivos em Arquivos grandes ou Duplicados antes de revisar."; return; }
        // A informação de cópia preservada prevalece se um arquivo foi marcado nas duas listas.
        var removals = selected.OrderByDescending(r => r.Keep is not null).DistinctBy(r => r.File.Path, StringComparer.OrdinalIgnoreCase)
            .Select(r => new DiskLensRemoval(r.File, r.Keep, r.Hash)).ToArray();
        if (!ConfirmCleanup(removals)) return;
        _cleaning = true;
        await Run(async token =>
        {
            StatusText.Text = "Enviando arquivos selecionados à Lixeira…";
            var result = await Task.Run(() => DiskLensService.Recycle(removals, token), token);
            _log.Write("INFO", $"DiskLens: {result.Recycled} arquivos enviados à Lixeira; {result.Errors.Count} falhas.");
            _scan = null; _folder = null; MapCanvas.Children.Clear(); _large.Clear(); _duplicates.Clear();
            FilesGrid.ItemsSource = DuplicatesGrid.ItemsSource = FoldersGrid.ItemsSource = null;
            SummaryText.Text = $"{result.Recycled:N0} arquivos enviados à Lixeira · {DiskLensService.FormatSize(result.Bytes)}";
            DetailText.Text = "Você pode restaurá-los pela Lixeira do Windows. O espaço só é liberado ao esvaziá-la. Analise novamente para atualizar os resultados.";
            StatusText.Text = result.Errors.Count == 0 ? "Limpeza concluída." : $"{result.Errors.Count} arquivos foram preservados por falha ou proteção. Detalhes no aviso e ao passar o mouse aqui.";
            StatusText.ToolTip = string.Join("\n", result.Errors.Take(30));
            if (result.Errors.Count > 0) MessageBox.Show(this, string.Join("\n", result.Errors.Take(10)), "Arquivos preservados", MessageBoxButton.OK, MessageBoxImage.Information);
        });
        _cleaning = false; SetBusy(false);
    }

    private bool ConfirmCleanup(IReadOnlyList<DiskLensRemoval> items)
    {
        var dialog = new Window { Owner = this, Title = "Revisar limpeza · DiskLens", Width = 720, Height = 470, MinWidth = 600, MinHeight = 350, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        dialog.SetResourceReference(BackgroundProperty, "BackgroundBrush"); dialog.SetResourceReference(ForegroundProperty, "TextBrush");
        var root = new DockPanel { Margin = new Thickness(24) }; dialog.Content = root;
        var text = new TextBlock { Text = $"Enviar {items.Count:N0} arquivos ({DiskLensService.FormatSize(items.Sum(i => i.File.Size))}) à Lixeira?\n\nRevise os caminhos. Arquivos protegidos ou alterados serão preservados.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
        DockPanel.SetDock(text, Dock.Top); root.Children.Add(text);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Content = "Cancelar", IsCancel = true, Margin = new Thickness(0, 0, 12, 0) };
        var confirm = new Button { Content = "Confirmar envio à Lixeira" };
        confirm.Click += (_, _) => dialog.DialogResult = true; buttons.Children.Add(cancel); buttons.Children.Add(confirm);
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        root.Children.Add(new ListBox { ItemsSource = items.Select(i => i.File.Path + " · " + i.File.SizeText), Background = Brushes.Transparent, Foreground = (Brush)FindResource("TextBrush") });
        return dialog.ShowDialog() == true;
    }
}
