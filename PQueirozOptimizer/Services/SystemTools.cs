using System.IO;

namespace PQueirozOptimizer.Services;

/// <summary>
/// Caminhos absolutos das ferramentas do Windows. O app roda como administrador e o CreateProcess procura um
/// nome solto ("sc.exe") primeiro na pasta do próprio app e na pasta atual: numa cópia portátil em uma pasta
/// gravável, um executável plantado com o mesmo nome rodaria elevado.
/// </summary>
public static class SystemTools
{
    public static string PowerShell => Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    /// <summary>Nome solto que existe em System32 vira o caminho completo; caminhos e ferramentas de fora (winget) ficam como estão.</summary>
    public static string Resolve(string file)
    {
        if (string.IsNullOrWhiteSpace(file) || Path.IsPathRooted(file) || file.IndexOfAny(['\\', '/']) >= 0) return file;
        var system = Path.Combine(Environment.SystemDirectory, file);
        return File.Exists(system) ? system : file;
    }
}
