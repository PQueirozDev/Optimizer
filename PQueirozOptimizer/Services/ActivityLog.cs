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

    /// <summary>
    /// Copia o log para enviar ao suporte sem dados pessoais: pasta do usuário, nome da conta e do PC, e-mails e
    /// chaves de licença são trocados por marcadores. O arquivo original continua intacto.
    /// </summary>
    public void ExportRedacted(string destination)
    {
        string[] lines;
        lock (_gate) lines = File.Exists(_path) ? File.ReadAllLines(_path) : Array.Empty<string>();
        File.WriteAllLines(destination, lines.Select(Redact));
    }

    private static readonly System.Text.RegularExpressions.Regex EmailPattern = new(@"[\w.+-]+@[\w-]+(\.[\w-]+)+", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex LicensePattern = new(@"PQO1-[A-Za-z0-9+/=_.-]{8,}", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static string Redact(string line)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length > 3) line = line.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        line = LicensePattern.Replace(line, "PQO1-<oculta>");
        line = EmailPattern.Replace(line, "<email>");
        foreach (var (value, marker) in new[] { (Environment.UserName, "<usuario>"), (Environment.MachineName, "<pc>") })
            if (value.Length > 0) line = System.Text.RegularExpressions.Regex.Replace(line, $@"(?<![\w]){System.Text.RegularExpressions.Regex.Escape(value)}(?![\w])", marker, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return line;
    }
    public string[] Recent()
    {
        lock (_gate)
        {
            // Versões anteriores gravavam o plano interno do script ("[PLANO] {json}"), que não é para o usuário
            try { return File.Exists(_path) ? File.ReadLines(_path).Where(l => !l.Contains("] [PLANO] ", StringComparison.Ordinal)).TakeLast(150).ToArray() : Array.Empty<string>(); }
            catch (IOException) { return Array.Empty<string>(); }
            catch (UnauthorizedAccessException) { return Array.Empty<string>(); }
        }
    }
}

