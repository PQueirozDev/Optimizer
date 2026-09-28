using System.IO;
using System.Runtime.InteropServices;

namespace PQueirozOptimizer.Services;

/// <summary>Cria atalhos .lnk pelo WScript.Shell (COM), sem montar scripts PowerShell.</summary>
public static class ShortcutFile
{
    /// <summary>Estilo da janela ao abrir: 1 = normal, 7 = minimizada sem foco.</summary>
    public static void Create(string path, string target, string arguments, string workingDirectory, string iconLocation, string description, int windowStyle = 1)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell não está disponível neste Windows.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            dynamic link = shell.CreateShortcut(path);
            try
            {
                link.TargetPath = target;
                link.Arguments = arguments;
                link.WorkingDirectory = workingDirectory;
                link.IconLocation = iconLocation;
                link.Description = description;
                link.WindowStyle = windowStyle;
                link.Save();
            }
            finally { Marshal.FinalReleaseComObject(link); }
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }

    /// <summary>Destino e argumentos de um atalho existente (null se não for possível ler).</summary>
    public static (string Target, string Arguments)? Read(string path)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) return null;
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic link = shell.CreateShortcut(path);
            try { return ((string)link.TargetPath, (string)link.Arguments); }
            finally { Marshal.FinalReleaseComObject(link); }
        }
        catch (COMException) { return null; }
        finally { Marshal.FinalReleaseComObject(shell); }
    }

    /// <summary>Caminho do executável deste aplicativo, para os atalhos apontarem para ele.</summary>
    public static string AppExecutable => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "PQueirozOptimizer.exe");
}
