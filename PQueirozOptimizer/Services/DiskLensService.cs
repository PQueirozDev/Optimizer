using System.IO;
using System.Security.Cryptography;

namespace PQueirozOptimizer.Services;

public sealed record DiskLensFile(string Path, long Size, DateTime ModifiedUtc)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public string SizeText => DiskLensService.FormatSize(Size);
}
public sealed class DiskLensFolder(string path)
{
    public string Path { get; } = path;
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\')) is { Length: > 0 } name ? name : Path;
    public long Size { get; internal set; }
    public string SizeText => DiskLensService.FormatSize(Size);
    public List<DiskLensFolder> Folders { get; } = new();
    public List<DiskLensFile> Files { get; } = new();
}
public sealed record DiskLensScan(DiskLensFolder Root, IReadOnlyList<DiskLensFile> Files, IReadOnlyList<DiskLensFolder> Folders, int Skipped);
public sealed record DiskLensProgress(int Files, long Bytes, string Current);
public sealed record DiskLensDuplicates(string Hash, IReadOnlyList<DiskLensFile> Files)
{
    public long RedundantBytes => Files[0].Size * (Files.Count - 1);
}
public sealed record DiskLensRemoval(DiskLensFile File, DiskLensFile? Keep = null, string? Hash = null);
public sealed record DiskLensCleanup(int Recycled, long Bytes, IReadOnlyList<string> Errors);

/// <summary>Análise somente de leitura. Limpeza explícita, revalidada e sempre pela Lixeira.</summary>
public static class DiskLensService
{
    public static string FormatSize(long value)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = value; int index = 0;
        while (size >= 1024 && index < units.Length - 1) { size /= 1024; index++; }
        return $"{size:0.##} {units[index]}";
    }

    public static DiskLensScan Scan(string root, CancellationToken token, IProgress<DiskLensProgress>? progress = null)
    {
        root = System.IO.Path.GetFullPath(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("A pasta selecionada não existe.");
        if (HasLinkAncestor(root)) throw new IOException("Escolha uma pasta real, sem links ou junções no caminho.");
        var tree = new DiskLensFolder(root);
        var folders = new List<DiskLensFolder> { tree };
        var files = new List<DiskLensFile>();
        var pending = new Stack<DiskLensFolder>(); pending.Push(tree);
        int skipped = 0; long bytes = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false, RecurseSubdirectories = false };
        while (pending.TryPop(out var current))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                foreach (var item in new DirectoryInfo(current.Path).EnumerateFileSystemInfos("*", options))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        if ((item.Attributes & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                        if (item is DirectoryInfo)
                        {
                            var child = new DiskLensFolder(item.FullName);
                            current.Folders.Add(child); folders.Add(child); pending.Push(child);
                        }
                        else if (item is FileInfo file)
                        {
                            var entry = new DiskLensFile(file.FullName, file.Length, file.LastWriteTimeUtc);
                            current.Files.Add(entry); files.Add(entry); current.Size += entry.Size; bytes += entry.Size;
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; }
                    if (watch.ElapsedMilliseconds >= 250)
                    {
                        progress?.Report(new(files.Count, bytes, current.Path)); watch.Restart();
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; }
        }
        // Cada pai precede seus filhos na lista; agrega tamanhos sem recursão na pilha.
        for (int i = folders.Count - 1; i >= 0; i--)
            folders[i].Size += folders[i].Folders.Sum(child => child.Size);
        progress?.Report(new(files.Count, bytes, root));
        return new(tree, files, folders, skipped);
    }

    public static IReadOnlyList<DiskLensDuplicates> FindDuplicates(DiskLensScan scan, CancellationToken token, IProgress<string>? progress = null)
    {
        var result = new List<DiskLensDuplicates>();
        // Tamanho, amostra e só então SHA-256 integral: arquivos únicos não são lidos por inteiro.
        foreach (var sameSize in scan.Files.Where(f => f.Size > 0).GroupBy(f => f.Size).Where(g => g.Skip(1).Any()))
        {
            token.ThrowIfCancellationRequested();
            var samples = new Dictionary<string, List<DiskLensFile>>();
            foreach (var file in sameSize)
            {
                token.ThrowIfCancellationRequested(); progress?.Report(file.Path);
                try
                {
                    using var stream = OpenUnchanged(file);
                    var buffer = new byte[(int)Math.Min(65536, file.Size)];
                    stream.ReadExactly(buffer);
                    var sample = Convert.ToHexString(SHA256.HashData(buffer));
                    if (!samples.TryGetValue(sample, out var list)) samples[sample] = list = new();
                    list.Add(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            foreach (var sample in samples.Values.Where(g => g.Count > 1))
            {
                var hashes = new Dictionary<string, List<DiskLensFile>>();
                foreach (var file in sample)
                {
                    token.ThrowIfCancellationRequested(); progress?.Report(file.Path);
                    try
                    {
                        var hash = Hash(file, token);
                        if (!hashes.TryGetValue(hash, out var list)) hashes[hash] = list = new();
                        list.Add(file);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
                result.AddRange(hashes.Where(g => g.Value.Count > 1).Select(g => new DiskLensDuplicates(g.Key, g.Value.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToArray())));
            }
        }
        return result.OrderByDescending(g => g.RedundantBytes).ToArray();
    }

    private static FileStream OpenUnchanged(DiskLensFile file)
    {
        if (HasLinkAncestor(file.Path)) throw new IOException("O caminho virou um link ou junção.");
        var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
        if (stream.Length != file.Size || File.GetLastWriteTimeUtc(file.Path) != file.ModifiedUtc)
        { stream.Dispose(); throw new IOException("O arquivo foi alterado desde a análise."); }
        return stream;
    }

    private static string Hash(DiskLensFile file, CancellationToken token)
    {
        using var stream = OpenUnchanged(file);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024]; int read;
        while ((read = stream.Read(buffer)) != 0) { token.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, read); }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    internal static bool HasLinkAncestor(string path)
    {
        for (string? current = System.IO.Path.GetFullPath(path); current is not null; current = Directory.GetParent(current)?.FullName)
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
        return false;
    }

    public static bool IsProtected(string path)
    {
        path = System.IO.Path.GetFullPath(path);
        // Unidades de rede e removíveis podem não oferecer Lixeira; permanecem disponíveis para análise.
        if (new DriveInfo(System.IO.Path.GetPathRoot(path)!).DriveType != DriveType.Fixed) return true;
        string[] roots = { Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), AppContext.BaseDirectory };
        if (roots.Where(r => !string.IsNullOrWhiteSpace(r)).Any(r => IsWithin(path, r))) return true;
        if (path.Split(System.IO.Path.DirectorySeparatorChar).Any(part => part.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase)
            || part.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase))) return true;
        return (File.GetAttributes(path) & (FileAttributes.System | FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0;
    }

    private static bool IsWithin(string path, string root) => path.Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    internal static void ValidateRemoval(DiskLensRemoval removal, IReadOnlySet<string> selected, CancellationToken token)
    {
        if (IsProtected(removal.File.Path)) throw new IOException("Arquivo protegido: a limpeza não é permitida neste local.");
        using (OpenUnchanged(removal.File)) { }
        if (removal.Keep is { } keep)
        {
            if (selected.Contains(keep.Path) || removal.Hash is null) throw new IOException("Mantenha ao menos uma cópia do grupo.");
            if (Hash(keep, token) != removal.Hash || Hash(removal.File, token) != removal.Hash)
                throw new IOException("O conteúdo mudou; refaça a busca de duplicados.");
        }
    }

    public static DiskLensCleanup Recycle(IReadOnlyList<DiskLensRemoval> removals, CancellationToken token)
    {
        var unique = removals.DistinctBy(r => r.File.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        var selected = unique.Select(r => r.File.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int count = 0; long bytes = 0; var errors = new List<string>();
        foreach (var removal in unique)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var preservedCopy = removal.Keep is null ? null : OpenUnchanged(removal.Keep);
                ValidateRemoval(removal, selected, token);
                // A API falha quando a Lixeira não está disponível; não há fallback para exclusão permanente.
                DiskLensRecycle.File(removal.File.Path);
                count++; bytes += removal.File.Size;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or System.Runtime.InteropServices.COMException)
            { errors.Add(removal.File.Path + ": " + ex.Message); }
        }
        return new(count, bytes, errors);
    }
}
