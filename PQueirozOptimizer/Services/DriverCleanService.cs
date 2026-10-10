using System.IO;
using Microsoft.Win32;

namespace PQueirozOptimizer.Services;

public enum GpuVendor { Unknown, Nvidia, Amd, Intel }

/// <summary>
/// Instalação limpa de driver de vídeo com o DDU (Display Driver Uninstaller): cria um ponto de restauração,
/// impede o Windows Update de instalar um driver genérico no meio, remove o driver atual pelo DDU, reinicia e,
/// no próximo logon, abre o instalador do driver novo escolhido pelo usuário.
/// </summary>
public sealed class DriverCleanService
{
    private readonly ActivityLog _log;
    private const string DisplayClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
    private const string DriverSearchingKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching";
    private const string RunOnceKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";
    public const string DduWingetId = "Wagnardsoft.DisplayDriverUninstaller";
    public static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OtimizadorPC", "Drivers");

    public DriverCleanService(ActivityLog log) => _log = log;

    /// <summary>Placas de vídeo instaladas, lidas do registro (sem WMI).</summary>
    public static List<(string Name, GpuVendor Vendor)> DetectGpus()
    {
        var list = new List<(string, GpuVendor)>();
        using var cls = Registry.LocalMachine.OpenSubKey(DisplayClass);
        if (cls is null) return list;
        foreach (var sub in cls.GetSubKeyNames().Where(n => n.All(char.IsDigit)))
        {
            using var key = cls.OpenSubKey(sub);
            if (key?.GetValue("DriverDesc") is not string name) continue;
            var provider = $"{key.GetValue("ProviderName")} {name}";
            var vendor = provider.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Nvidia
                : provider.Contains("AMD", StringComparison.OrdinalIgnoreCase) || provider.Contains("Advanced Micro", StringComparison.OrdinalIgnoreCase) || provider.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Amd
                : provider.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Intel : GpuVendor.Unknown;
            if (vendor != GpuVendor.Unknown && !list.Any(g => g.Item1 == name)) list.Add((name, vendor));
        }
        return list;
    }

    public static string DriverPage(GpuVendor vendor) => vendor switch
    {
        GpuVendor.Nvidia => "https://www.nvidia.com/pt-br/drivers/",
        GpuVendor.Amd => "https://www.amd.com/pt/support/download/drivers.html",
        GpuVendor.Intel => "https://www.intel.com.br/content/www/br/pt/download-center/home.html",
        _ => "https://www.nvidia.com/pt-br/drivers/",
    };

    public static string? DduPath()
    {
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
        {
            var path = Path.Combine(root, "Display Driver Uninstaller", "Display Driver Uninstaller.exe");
            if (File.Exists(path)) return path;
        }
        return null;
    }

    public async Task InstallDduAsync()
    {
        if (DduPath() != null) return;
        if (!GamingService.IsWingetAvailable()) throw new InvalidOperationException("O winget não está disponível para instalar o DDU. Instale o \"Instalador de Aplicativos\" pela Microsoft Store.");
        var code = await Task.Run(() => GamingService.RunTool("winget.exe", "install", "--id", DduWingetId, "-e", "--silent", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity", "--source", "winget"));
        if (DduPath() is null) throw new InvalidOperationException($"Não foi possível instalar o DDU pelo winget (código {code}).");
        _log.Write("SUCCESS", "DDU (Display Driver Uninstaller) instalado");
    }

    /// <summary>
    /// Agenda o instalador para o próximo logon, cria o ponto de restauração e executa o DDU, que remove o
    /// driver e reinicia o PC.
    /// </summary>
    public async Task RunCleanInstallAsync(GpuVendor vendor, string installerPath, IProgress<string>? progress = null)
    {
        if (vendor == GpuVendor.Unknown) throw new InvalidOperationException("Fabricante da placa de vídeo não reconhecido.");
        if (!File.Exists(installerPath) || !installerPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new FileNotFoundException("Escolha o instalador (.exe) do driver novo.", installerPath);
        await InstallDduAsync();
        var ddu = DduPath()!;

        progress?.Report("Copiando o instalador do driver...");
        RegistryTweakStore.EnsureProtectedDirectory(DataDirectory);
        // Cópia numa pasta só de administradores: o que roda elevado no logon não pode ser trocado por outro usuário
        var installer = Path.Combine(DataDirectory, "driver-novo.exe");
        // Arquivo antigo pode ter outro dono (e permissões próprias): apaga e cria um novo, herdando a proteção da pasta
        File.Delete(installer);
        File.Copy(installerPath, installer);

        progress?.Report("Criando ponto de restauração...");
        try { await PowerShellBridge.RunScriptAsync("Checkpoint-Computer -Description 'Qrztweaks - antes da instalação limpa de driver' -RestorePointType MODIFY_SETTINGS", timeout: TimeSpan.FromMinutes(5)); }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException) { progress?.Report("Ponto de restauração não criado: " + ex.Message); }

        progress?.Report("Bloqueando drivers do Windows Update até o driver novo ser instalado...");
        int? previousSearch;
        using (var key = Registry.LocalMachine.CreateSubKey(DriverSearchingKey, writable: true))
        {
            previousSearch = key.GetValue("SearchOrderConfig") as int?;
            key.SetValue("SearchOrderConfig", 0, RegistryValueKind.DWord);
        }

        // Desfaz o agendamento e a trava de drivers: usado se qualquer passo antes do DDU reiniciar o PC falhar
        void Rollback()
        {
            try
            {
                using (var runOnce = Registry.LocalMachine.OpenSubKey(RunOnceKey, writable: true)) runOnce?.DeleteValue("PQueirozDriverLimpo", false);
                using var key = Registry.LocalMachine.CreateSubKey(DriverSearchingKey, writable: true);
                if (previousSearch is { } p) key.SetValue("SearchOrderConfig", p, RegistryValueKind.DWord);
                else key.DeleteValue("SearchOrderConfig", false);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            { _log.Write("ERROR", "Não foi possível desfazer a trava de drivers do Windows Update: " + ex.Message); }
        }

        int code;
        try
        {
            // Script do próximo logon: abre o instalador, espera terminar e devolve a busca de drivers como estava
            var script = Path.Combine(DataDirectory, "apos-ddu.ps1");
            File.Delete(script);
            var restore = previousSearch is { } v
                ? $"Set-ItemProperty -Path 'HKLM:\\{DriverSearchingKey}' -Name SearchOrderConfig -Value {v} -Type DWord"
                : $"Remove-ItemProperty -Path 'HKLM:\\{DriverSearchingKey}' -Name SearchOrderConfig -ErrorAction SilentlyContinue";
            File.WriteAllText(script,
                "$ErrorActionPreference = 'Continue'\r\n" +
                $"Start-Process -FilePath '{installer}' -Wait\r\n" +
                restore + "\r\n" +
                $"Remove-Item -LiteralPath '{installer}' -Force -ErrorAction SilentlyContinue\r\n");
            using (var runOnce = Registry.LocalMachine.CreateSubKey(RunOnceKey, writable: true))
                runOnce.SetValue("PQueirozDriverLimpo", $"\"{SystemTools.PowerShell}\" -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"");

            var clean = vendor switch { GpuVendor.Nvidia => "-cleannvidia", GpuVendor.Amd => "-cleanamd", _ => "-cleanintel" };
            _log.Write("INFO", $"Instalação limpa: DDU {clean}, instalador agendado para o próximo logon");
            progress?.Report("Removendo o driver atual com o DDU (o PC reinicia sozinho)...");
            code = await Task.Run(() => GamingService.RunTool(ddu, "-silent", clean, "-nosafemodemsg", "-restart"));
        }
        catch
        {
            Rollback();
            throw;
        }
        if (code != 0)
        {
            // DDU falhou antes de reiniciar
            Rollback();
            throw new InvalidOperationException($"O DDU terminou com o código {code}; nada foi agendado.");
        }
    }
}
