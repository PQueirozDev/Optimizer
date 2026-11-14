using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PQueirozOptimizer;
using PQueirozOptimizer.Services;

internal static class DiskLensTests
{
    private static void Check(bool condition, string text)
    {
        if (!condition) throw new Exception(text);
        Console.WriteLine("PASS DiskLens: " + text);
    }
    private static bool Rejects(Action work)
    {
        try { work(); return false; } catch (IOException) { return true; }
    }
    public static void Run(string output, bool render = false)
    {
        // Pasta exclusiva para este teste; nenhuma limpeza de arquivos do usuário.
        var root = Path.Combine(output, "fixture-" + Guid.NewGuid().ToString("N"));
        var projects = Path.Combine(root, "Projetos"); var media = Path.Combine(root, "Vídeos");
        Directory.CreateDirectory(projects); Directory.CreateDirectory(media);
        var data = new byte[256 * 1024]; Random.Shared.NextBytes(data);
        File.WriteAllBytes(Path.Combine(projects, "arquivo.zip"), data);
        File.WriteAllBytes(Path.Combine(root, "copia.zip"), data);
        var different = (byte[])data.Clone(); different[^1] ^= 1;
        File.WriteAllBytes(Path.Combine(root, "mesma-amostra.zip"), different);
        File.WriteAllBytes(Path.Combine(media, "video.mp4"), new byte[2 * 1024 * 1024]);
        File.WriteAllText(Path.Combine(root, "vazio.txt"), "");
        var scan = DiskLensService.Scan(root, default);
        Check(scan.Files.Count == 5 && scan.Folders.Count == 3 && scan.Root.Size == data.Length * 3L + 2 * 1024 * 1024,
            "agrega arquivos e subpastas, incluindo vazios");
        var groups = DiskLensService.FindDuplicates(scan, default);
        Check(groups.Count == 1 && groups[0].Files.Count == 2 && groups[0].RedundantBytes == data.Length,
            "só agrupa conteúdo integralmente igual; mesmo tamanho e mesma amostra não bastam");
        Check(Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length == 5,
            "análise e busca não criam nem removem arquivos");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        bool scanCancelled = false, duplicatesCancelled = false;
        try { DiskLensService.Scan(root, cancel.Token); } catch (OperationCanceledException) { scanCancelled = true; }
        try { DiskLensService.FindDuplicates(scan, cancel.Token); } catch (OperationCanceledException) { duplicatesCancelled = true; }
        Check(scanCancelled && duplicatesCancelled, "análise e hashes aceitam cancelamento");
        var group = groups[0]; var keep = group.Files[0]; var remove = group.Files[1];
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { remove.Path };
        var removal = new DiskLensRemoval(remove, keep, group.Hash);
        DiskLensService.ValidateRemoval(removal, selected, default);
        Check(true, "limpeza valida a cópia preservada e o conteúdo antes de agir");
        selected.Add(keep.Path);
        Check(Rejects(() => DiskLensService.ValidateRemoval(removal, selected, default)), "recusa remover também a cópia preservada");
        selected.Remove(keep.Path);
        // Conteúdo mudou mantendo tamanho e data: a revalidação por hash ainda deve recusar.
        File.WriteAllBytes(keep.Path, different); File.SetLastWriteTimeUtc(keep.Path, keep.ModifiedUtc);
        Check(Rejects(() => DiskLensService.ValidateRemoval(removal, selected, default)), "recusa cópia preservada cujo hash mudou");
        File.WriteAllBytes(keep.Path, data); File.SetLastWriteTimeUtc(keep.Path, keep.ModifiedUtc);
        File.AppendAllText(remove.Path, "alterado");
        Check(Rejects(() => DiskLensService.ValidateRemoval(removal, selected, default)), "recusa arquivo alterado desde a análise");
        Check(DiskLensService.IsProtected(Environment.GetFolderPath(Environment.SpecialFolder.Windows)), "protege o diretório do Windows");
        var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length;
        var rejected = DiskLensService.Recycle(new[] { removal }, default);
        Check(rejected.Recycled == 0 && rejected.Errors.Count == 1 && File.Exists(remove.Path)
            && Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length == before,
            "o fluxo de limpeza preserva o arquivo quando a revalidação falha");
        if (!render) return;
        File.WriteAllBytes(remove.Path, data); File.SetLastWriteTimeUtc(remove.Path, remove.ModifiedUtc);
        Render(output, DiskLensService.Scan(root, default));
    }

    public static void RecycleProbe(string output)
    {
        Directory.CreateDirectory(output);
        var path = Path.Combine(output, "disklens-recycle-test-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "Arquivo temporário exclusivo do teste de integração DiskLens.");
        var entry = new FileInfo(path);
        var file = new DiskLensFile(path, entry.Length, entry.LastWriteTimeUtc);
        var result = DiskLensService.Recycle(new[] { new DiskLensRemoval(file) }, default);
        Check(result.Recycled == 1 && result.Errors.Count == 0 && !File.Exists(path), "Windows aceitou o envio do arquivo de teste à Lixeira: " + string.Join("; ", result.Errors));
        var root = Path.GetPathRoot(path)!;
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var recycle = Path.Combine(root, "$Recycle.Bin", identity.User!.Value);
        // O registro $I contém o caminho original UTF-16; confirma reciclagem real, sem exclusão permanente.
        var found = Directory.EnumerateFiles(recycle, "$I*").Any(info =>
        {
            try { return System.Text.Encoding.Unicode.GetString(File.ReadAllBytes(info)).Contains(path, StringComparison.OrdinalIgnoreCase); }
            catch (IOException) { return false; }
        });
        Check(found, "registro da Lixeira preserva o caminho original para restauração");
    }

    private static void Render(string output, DiskLensScan scan)
    {
        var app = Program.CreateTestApp();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        AppearanceService.Apply(new PQueirozOptimizer.Models.AppearanceSettings { Theme = PQueirozOptimizer.Models.ThemeMode.Dark });
        var window = new DiskLensWindow(new ActivityLog(Path.Combine(output, "disklens.log")))
        { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false };
        window.Show(); window.LoadScan(scan);
        window.ShowDuplicates(DiskLensService.FindDuplicates(scan, default));
        foreach (var size in new[] { (1120d, 820d), (860d, 650d) })
        {
            window.Width = size.Item1; window.Height = size.Item2; window.UpdateLayout();
            foreach (var index in new[] { 0, 3 })
            {
                ((System.Windows.Controls.TabControl)window.FindName("Tabs")).SelectedIndex = index;
                window.UpdateLayout();
                var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
                var content = (FrameworkElement)window.Content;
                var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                var visual = new DrawingVisual();
                using (var drawing = visual.RenderOpen())
                {
                    drawing.DrawRectangle(window.Background, null, new Rect(0, 0, window.ActualWidth, window.ActualHeight));
                    drawing.DrawRectangle(new VisualBrush(content) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null,
                        new Rect(26, 26, content.ActualWidth, content.ActualHeight));
                }
                bitmap.Render(visual);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(output, $"disklens-{index}-{size.Item1}x{size.Item2}.png")); png.Save(file);
            }
        }
        window.Close(); Check(true, "janela renderizada nos tamanhos normal e compacto");
    }
}
