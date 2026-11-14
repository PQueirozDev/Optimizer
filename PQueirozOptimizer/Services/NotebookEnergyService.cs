using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace PQueirozOptimizer.Services;

/// <summary>Abre o módulo completo de notebooks com as permissões do usuário da área de trabalho.</summary>
public static class NotebookEnergyService
{
    public static string ShortcutPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Energia para notebooks - Qrztweaks.lnk");
    public static bool IsShortcutCreated() => File.Exists(ShortcutPath);

    public static string Arguments(string executable, bool tray = false)
    {
        if (!Path.IsPathFullyQualified(executable) || executable.Contains('"')) throw new ArgumentException("Caminho do aplicativo inválido.", nameof(executable));
        return $"/c set \"__COMPAT_LAYER=RunAsInvoker\" && start \"\" \"{executable}\" --notebook-energy{(tray ? " --tray" : "")}";
    }

    public static string StartupCommand() => $"\"{SystemTools.Resolve("cmd.exe")}\" {Arguments(ShortcutFile.AppExecutable, tray: true)}";

    public static void CreateShortcut(ActivityLog log)
    {
        ShortcutFile.Create(ShortcutPath, SystemTools.Resolve("cmd.exe"), Arguments(ShortcutFile.AppExecutable),
            AppContext.BaseDirectory, ShortcutFile.AppExecutable + ",0", "Modo de energia completo para notebooks - Qrztweaks", windowStyle: 7);
        log.Write("SUCCESS", "Atalho de energia para notebooks criado na Área de Trabalho");
    }

    public static void RemoveShortcut(ActivityLog log)
    {
        if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
        log.Write("INFO", "Atalho de energia para notebooks removido");
    }

    public static void Open()
    {
        var command = SystemTools.Resolve("cmd.exe");
        var arguments = Arguments(ShortcutFile.AppExecutable);
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        {
            Process.Start(new ProcessStartInfo(command, arguments) { UseShellExecute = false, CreateNoWindow = true })?.Dispose();
            return;
        }

        // O Explorer pertence ao usuário da sessão. O controlador não herda a elevação do aplicativo.
        object? windows = null, desktop = null, document = null, shell = null;
        try
        {
            windows = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"), throwOnError: true)!);
            object location = 0, root = 0;
            desktop = ((dynamic)windows!).FindWindowSW(ref location, ref root, 8, out int _, 1);
            if (desktop is null) throw new InvalidOperationException("O Explorer não está disponível para abrir o modo de energia.");
            document = ((dynamic)desktop).Document;
            shell = ((dynamic)document).Application;
            ((dynamic)shell).ShellExecute(command, arguments, AppContext.BaseDirectory, "open", 0);
        }
        finally
        {
            foreach (var value in new[] { shell, document, desktop, windows })
                if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
        }
    }
}
