using System.IO;

namespace PQueirozOptimizer.Services;

public sealed record QuickCleanResult(int Found, int Removed, int Ignored, long BytesFreed);
public sealed record CleanFile(string Path, long Length, DateTime LastWriteUtc);
/// <summary>
/// Categoria da limpeza. <paramref name="Extended"/> marca as que só aparecem na tela de análise (o atalho
/// de Limpeza Rápida continua só nos temporários); <paramref name="OptIn"/> começa desmarcada.
/// A Lixeira não tem lista de arquivos: o tamanho vem do Windows e ela é esvaziada pela API do shell.
/// </summary>
public sealed record CleanCategory(string Name, string Root, IReadOnlyList<CleanFile> Files, int Ignored, bool Extended = false, bool OptIn = false, long RecycleBinBytes = -1, long RecycleBinItems = 0)
{
    public bool IsRecycleBin => RecycleBinBytes >= 0;
    public long Bytes => IsRecycleBin ? RecycleBinBytes : Files.Sum(f => f.Length);
    public long Count => IsRecycleBin ? RecycleBinItems : Files.Count;
}

public sealed class QuickCleanService
{
    private readonly ActivityLog _log;
    public QuickCleanService(ActivityLog log) => _log = log;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RecycleBinInfo { public int Size; public long Bytes; public long Items; }
    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? rootPath, ref RecycleBinInfo info);
    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? rootPath, uint flags);

    public Task<IReadOnlyList<CleanCategory>> AnalyzeAsync(CancellationToken token = default) => Task.Run<IReadOnlyList<CleanCategory>>(() =>
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var categories = new List<CleanCategory>();
        var roots = new[] { ("Temporários do usuário", Path.GetTempPath()), ("Temporários do Windows", Path.Combine(windows, "Temp")) };
        foreach (var (name, root) in roots.DistinctBy(r => Path.GetFullPath(r.Item2).TrimEnd(Path.DirectorySeparatorChar), StringComparer.OrdinalIgnoreCase))
            categories.Add(ScanFolder(name, root, extended: false, token));

        // Categorias extras da tela de análise: arquivos que o Windows recria ou não usa mais
        categories.Add(ScanFolder("Sobras do Windows Update", Path.Combine(windows, "SoftwareDistribution", "Download"), extended: true, token));
        categories.Add(ScanFolder("Cache de entrega de atualizações", Path.Combine(windows, "ServiceProfiles", "NetworkService", "AppData", "Local", "Microsoft", "Windows", "DeliveryOptimization", "Cache"), extended: true, token));
        categories.Add(ScanFolder("Relatórios de erro do Windows", Path.Combine(programData, "Microsoft", "Windows", "WER"), extended: true, token));
        var dumps = ScanFolder("Despejos de travamento", Path.Combine(windows, "Minidump"), extended: true, token);
        var appDumps = ScanFolder("", Path.Combine(localAppData, "CrashDumps"), extended: true, token);
        categories.Add(dumps with { Files = dumps.Files.Concat(appDumps.Files).ToList(), Ignored = dumps.Ignored + appDumps.Ignored });
        var memoryDump = Path.Combine(windows, "MEMORY.DMP");
        if (File.Exists(memoryDump) && new FileInfo(memoryDump) is { } dump && dump.LastWriteTimeUtc <= DateTime.UtcNow.AddDays(-2))
            categories.Add(new CleanCategory("Despejo de memória completo", windows, new[] { new CleanFile(dump.FullName, dump.Length, dump.LastWriteTimeUtc) }, 0, Extended: true));

        var bin = new RecycleBinInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<RecycleBinInfo>() };
        if (SHQueryRecycleBin(null, ref bin) == 0)
            categories.Add(new CleanCategory("Lixeira", "Todas as unidades", Array.Empty<CleanFile>(), 0, Extended: true, OptIn: true, RecycleBinBytes: bin.Bytes, RecycleBinItems: bin.Items));
        return categories;
    }, token);

    /// <summary>Arquivos de uma pasta (com subpastas) modificados há mais de 48 horas, sem seguir links.</summary>
    private static CleanCategory ScanFolder(string name, string root, bool extended, CancellationToken token)
    {
        var files = new List<CleanFile>();
        var ignored = 0;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var folder))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (!Directory.Exists(folder) || HasReparsePoint(folder)) { if (folder != root) ignored++; continue; }
                foreach (var path in Directory.EnumerateFileSystemEntries(folder))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var attrs = File.GetAttributes(path);
                        if ((attrs & FileAttributes.ReparsePoint) != 0) { ignored++; continue; }
                        if ((attrs & FileAttributes.Directory) != 0) { pending.Push(path); continue; }
                        var info = new FileInfo(path);
                        if (info.LastWriteTimeUtc > DateTime.UtcNow.AddDays(-2)) { ignored++; continue; }
                        files.Add(new CleanFile(path, info.Length, info.LastWriteTimeUtc));
                    }
                    catch (IOException) { ignored++; }
                    catch (UnauthorizedAccessException) { ignored++; }
                }
            }
            catch (IOException) { ignored++; }
            catch (UnauthorizedAccessException) { ignored++; }
        }
        return new CleanCategory(name, Path.GetFullPath(root), files, ignored, extended);
    }

    private static bool HasReparsePoint(string path)
    {
        for (var current = new DirectoryInfo(path); current != null; current = current.Parent)
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) return true;
        return false;
    }

    public Task<QuickCleanResult> CleanAsync(IEnumerable<CleanCategory> categories, IProgress<string>? progress = null, CancellationToken token = default) => Task.Run(() =>
    {
        int found = 0, removed = 0, ignored = 0; long bytes = 0;
        foreach (var category in categories)
        {
            ignored += category.Ignored;
            progress?.Report(category.Name);
            if (category.IsRecycleBin)
            {
                found += (int)category.RecycleBinItems;
                // SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND: o usuário já escolheu na tela
                if (SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x2 | 0x4) == 0) { removed += (int)category.RecycleBinItems; bytes += category.RecycleBinBytes; }
                else ignored += (int)category.RecycleBinItems;
                continue;
            }
            var root = Path.GetFullPath(category.Root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (var file in category.Files)
            {
                token.ThrowIfCancellationRequested();
                found++;
                try
                {
                    var path = Path.GetFullPath(file.Path);
                    if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || HasReparsePoint(Path.GetDirectoryName(path)!))
                    { ignored++; continue; }
                    var info = new FileInfo(path);
                    if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LastWriteTimeUtc != file.LastWriteUtc || info.Length != file.Length || info.LastWriteTimeUtc > DateTime.UtcNow.AddDays(-2))
                    { ignored++; continue; }
                    File.Delete(path);
                    bytes += file.Length; removed++;
                }
                catch (IOException) { ignored++; }
                catch (UnauthorizedAccessException) { ignored++; }
            }
        }
        _log.Write("SUCCESS", $"Limpeza: {removed} removidos, {ignored} preservados/ignorados, {bytes / 1048576d:N1} MB liberados.");
        return new QuickCleanResult(found, removed, ignored, bytes);
    }, token);

    /// <summary>Atalho de Limpeza Rápida: só os temporários, sem as categorias extras da tela de análise.</summary>
    public async Task<QuickCleanResult> RunAsync(IProgress<string>? progress = null)
        => await CleanAsync((await AnalyzeAsync()).Where(c => !c.Extended), progress);

    public static string DesktopShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Limpeza Rápida.lnk");
    public static string StartMenuShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Start Menu", "Programs", "Limpeza Rápida.lnk");
    public static string TaskbarShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar", "Limpeza Rápida.lnk");

    public bool IsShortcutConfigured() => File.Exists(DesktopShortcut) || File.Exists(StartMenuShortcut);

    public void CreateShortcut()
    {
        var exe = ShortcutFile.AppExecutable;
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        if (!File.Exists(iconPath)) iconPath = exe;
        foreach (var target in new[] { DesktopShortcut, StartMenuShortcut })
            ShortcutFile.Create(target, exe, "--quick-clean", AppContext.BaseDirectory, iconPath + ",0", "Limpeza Rápida - Qrztweaks");
        _log.Write("SUCCESS", "Atalho de Limpeza Rápida configurado");
    }

    public void RemoveShortcut()
    {
        foreach (var shortcut in new[] { DesktopShortcut, StartMenuShortcut, TaskbarShortcut })
        {
            try { if (File.Exists(shortcut)) File.Delete(shortcut); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _log.Write("WARN", $"Não foi possível remover {shortcut}: {ex.Message}"); }
        }
        _log.Write("INFO", "Atalhos de Limpeza Rápida removidos");
    }
}

