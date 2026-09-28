using System.IO;

namespace PQueirozOptimizer.Services;

public sealed record QuickCleanResult(int Found, int Removed, int Ignored, long BytesFreed);
public sealed record CleanFile(string Path, long Length, DateTime LastWriteUtc);
public sealed record CleanCategory(string Name, string Root, IReadOnlyList<CleanFile> Files, int Ignored)
{
    public long Bytes => Files.Sum(f => f.Length);
}

public sealed class QuickCleanService
{
    private readonly ActivityLog _log;
    public QuickCleanService(ActivityLog log) => _log = log;

    public Task<IReadOnlyList<CleanCategory>> AnalyzeAsync(CancellationToken token = default) => Task.Run<IReadOnlyList<CleanCategory>>(() =>
    {
        var categories = new List<CleanCategory>();
        var roots = new[] { ("Temporários do usuário", Path.GetTempPath()), ("Temporários do Windows", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp")) };
        foreach (var (name, root) in roots.DistinctBy(r => Path.GetFullPath(r.Item2).TrimEnd(Path.DirectorySeparatorChar), StringComparer.OrdinalIgnoreCase))
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
                    if (!Directory.Exists(folder) || HasReparsePoint(folder)) { ignored++; continue; }
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
            categories.Add(new CleanCategory(name, Path.GetFullPath(root), files, ignored));
        }
        return categories;
    }, token);

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

    public async Task<QuickCleanResult> RunAsync(IProgress<string>? progress = null)
        => await CleanAsync(await AnalyzeAsync(), progress);

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
            ShortcutFile.Create(target, exe, "--quick-clean", AppContext.BaseDirectory, iconPath + ",0", "Limpeza Rápida - PQueiroz Optimizer");
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

