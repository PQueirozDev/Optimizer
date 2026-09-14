using System.IO;

namespace PQueirozOptimizer.Services;

public sealed class ActivityLog
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "PQueirozOptimizer", "optimizer.log");

    public event Action<string>? EntryAdded;

    public void Write(string level, string message)
    {
        var entry = $"[{DateTime.Now:HH:mm:ss}] [{level}] {message}";
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.AppendAllText(_path, entry + Environment.NewLine);
        EntryAdded?.Invoke(entry);
    }

    public string Export() => _path;
}
