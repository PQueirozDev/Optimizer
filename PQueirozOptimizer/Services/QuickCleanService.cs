using System.IO;

namespace PQueirozOptimizer.Services;

public sealed record QuickCleanResult(int Found, int Removed, int Ignored, long BytesFreed);

public sealed class QuickCleanService
{
    private readonly ActivityLog _log;
    public QuickCleanService(ActivityLog log) => _log = log;

    public Task<QuickCleanResult> RunAsync(IProgress<string>? progress = null) => Task.Run(() =>
    {
        _log.Write("INFO", "Limpeza Rápida iniciada");
        var roots = new[] { Path.GetTempPath(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp") }.Distinct(StringComparer.OrdinalIgnoreCase);
        var found = 0; var removed = 0; var ignored = 0; long bytes = 0;
        foreach (var root in roots)
        {
            progress?.Report(root.Equals(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase) ? "Arquivos temporários do usuário" : "Windows Temp");
            if (!Directory.Exists(root)) continue;
            try { foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) { found++; try { var size = new FileInfo(file).Length; File.Delete(file); bytes += size; removed++; } catch { ignored++; } } }
            catch { ignored++; }
        }
        progress?.Report("Cache temporário");
        _log.Write("INFO", $"{found} arquivos encontrados"); _log.Write("INFO", $"{removed} arquivos removidos"); _log.Write("INFO", $"{ignored} arquivos ignorados"); _log.Write("SUCCESS", $"Limpeza concluída: {bytes / 1024d / 1024d:N1} MB liberados");
        return new QuickCleanResult(found, removed, ignored, bytes);
    });

    public static string DesktopShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Limpeza Rápida.lnk");
    public static string StartMenuShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Start Menu", "Programs", "Limpeza Rápida.lnk");
    public static string TaskbarShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar", "Limpeza Rápida.lnk");

    public bool IsShortcutConfigured() => File.Exists(DesktopShortcut) || File.Exists(StartMenuShortcut);

    public void CreateShortcut()
    {
        var exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "PQueirozOptimizer.exe");
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        if (!File.Exists(iconPath))
        {
            iconPath = exe;
        }

        var startMenuDir = Path.GetDirectoryName(StartMenuShortcut) ?? "";
        var ps = $@"
$sh = New-Object -ComObject WScript.Shell
$targets = @('{DesktopShortcut.Replace("'", "''")}', '{StartMenuShortcut.Replace("'", "''")}')

foreach ($t in $targets) {{
    $dir = [System.IO.Path]::GetDirectoryName($t)
    if (-not (Test-Path $dir)) {{ New-Item -ItemType Directory -Path $dir -Force | Out-Null }}
    $s = $sh.CreateShortcut($t)
    $s.TargetPath = '{exe.Replace("'", "''")}'
    $s.Arguments = '--quick-clean'
    $s.WorkingDirectory = '{AppContext.BaseDirectory.Replace("'", "''")}'
    $s.IconLocation = '{iconPath.Replace("'", "''")},0'
    $s.Save()
}}

try {{
    $shell = New-Object -ComObject Shell.Application
    $folder = $shell.NameSpace('{startMenuDir.Replace("'", "''")}')
    $item = $folder.ParseName('{Path.GetFileName(StartMenuShortcut)}')
    if ($item) {{
        $verbs = $item.Verbs()
        foreach ($v in $verbs) {{
            if ($v.Name.Replace('&','') -match '(taskbar|barra de tarefas|Fixar)') {{
                $v.DoIt()
                break
            }}
        }}
    }}
}} catch {{ }}
";
        var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe", $"-NoProfile -WindowStyle Hidden -Command \"{ps}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        System.Diagnostics.Process.Start(psi)?.WaitForExit();
        _log.Write("SUCCESS", "Atalho de Limpeza Rápida configurado");
    }

    public void RemoveShortcut()
    {
        try { if (File.Exists(DesktopShortcut)) File.Delete(DesktopShortcut); } catch { }
        try { if (File.Exists(StartMenuShortcut)) File.Delete(StartMenuShortcut); } catch { }
        try { if (File.Exists(TaskbarShortcut)) File.Delete(TaskbarShortcut); } catch { }
        _log.Write("INFO", "Atalhos de Limpeza Rápida removidos");
    }
}
