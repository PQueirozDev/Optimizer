using System.IO;
namespace PQueirozOptimizer.Services;
public sealed class ActivityLog
{
    private readonly object _gate = new();
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PQueirozOptimizer", "optimizer.log");
    public ActivityLog(string? path = null) { if (path != null) _path = path; }
    public event Action<string>? EntryAdded;
    public void Write(string level, string message)
    {
        var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                if (File.Exists(_path) && new FileInfo(_path).Length > 5 * 1024 * 1024)
                    File.Move(_path, _path + ".previous", true);
                File.AppendAllText(_path, entry + Environment.NewLine);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        EntryAdded?.Invoke(entry);
    }
    public string Export() => _path;
    public string[] Recent()
    {
        lock (_gate)
        {
            try { return File.Exists(_path) ? File.ReadLines(_path).TakeLast(150).ToArray() : Array.Empty<string>(); }
            catch (IOException) { return Array.Empty<string>(); }
            catch (UnauthorizedAccessException) { return Array.Empty<string>(); }
        }
    }
}

