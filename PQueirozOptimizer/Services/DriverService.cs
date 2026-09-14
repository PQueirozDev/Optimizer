using System.IO;
using System.Net.Http;
using PQueirozOptimizer.Models;

namespace PQueirozOptimizer.Services;

public class DriverService
{
    private static readonly string DownloadsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    private static readonly HttpClient httpClient = new();

    public const string UserDriverCleanLink = "https://drive.google.com/drive/folders/1aSDFpulseBvYAPQNjqc_ORAUZ6FDkgjc?usp=drive_link";
    public const string NvCleanInstallLink = "https://www.techpowerup.com/download/techpowerup-nvcleanstall/";
    public const string NvProfileInspectorLink = "https://github.com/Orbmu2k/nvidiaProfileInspector/releases";

    public List<DriverInfo> GetAllDrivers(SystemSnapshot? snapshot = null, bool isEnglish = false)
    {
        var gpu = snapshot?.Graphics ?? "";
        var isNvidia = gpu.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                       gpu.Contains("GeForce", StringComparison.OrdinalIgnoreCase);

        return new List<DriverInfo>
        {
            new DriverInfo
            {
                Id = "pedro-driver-clean",
                Name = "Driver Clean (Pedro Queiroz)",
                Vendor = isEnglish ? "Custom Clean Driver" : "Driver Clean Otimizado",
                Category = isEnglish ? "GPU / Video" : "Vídeo / GPU",
                Version = "Clean Edition (Google Drive)",
                Description = isEnglish
                    ? "Official clean driver package provided by Pedro Queiroz. Stripped down for minimum input lag, zero telemetry, and maximum gaming FPS."
                    : "Pacote oficial de driver limpo fornecido por Pedro Queiroz. Configurado sem telemetria, com foco em menor latência de quadros e máximo FPS em jogos.",
                OfficialDownloadUrl = GetOfficialVendorPage(snapshot),
                SecondaryDownloadUrl = UserDriverCleanLink,
                SecondaryDownloadLabel = "Baixar via Google Drive (Driver Clean)",
                AlternateLocalPath = Path.Combine(DownloadsFolder, "Driver clean NVIDIA.exe"),
                ExpectedFileName = "Driver clean NVIDIA.exe",
                IsRecommendedForCurrentHardware = true
            },
            new DriverInfo
            {
                Id = "nvcleanstall",
                Name = "NVCleanstall (NVIDIA Clean Install)",
                Vendor = "NVIDIA",
                Category = isEnglish ? "GPU / Video" : "Vídeo / GPU",
                Version = "Latest (TechPowerUp)",
                Description = isEnglish
                    ? "Builds a custom NVIDIA driver installer, stripping telemetry and unneeded components (e.g., GeForce Experience). Ideal for a truly clean install with lower latency."
                    : "Monta um instalador customizado do driver NVIDIA, removendo telemetria e componentes desnecessários (ex.: GeForce Experience). Ideal para instalação limpa de verdade e menor latência.",
                OfficialDownloadUrl = NvCleanInstallLink,
                ExpectedFileName = "NVCleanstall.exe",
                IsRecommendedForCurrentHardware = isNvidia
            },
            new DriverInfo
            {
                Id = "nvprofileinspector",
                Name = "NVIDIA Profile Inspector",
                Vendor = "NVIDIA",
                Category = isEnglish ? "Utilitários" : "Utilitários",
                Version = "3.x (GitHub)",
                Description = isEnglish
                    ? "Advanced tool to edit hidden NVIDIA driver profiles per game: latency modes, LOD bias, frame rate limits, SLR/compatibility flags and much more."
                    : "Ferramenta avançada para editar perfis ocultos do driver NVIDIA por jogo: modos de latência, LOD bias, limite de FPS, flags de compatibilidade e muito mais.",
                OfficialDownloadUrl = NvProfileInspectorLink,
                ExpectedFileName = "nvidiaProfileInspector.zip",
                IsRecommendedForCurrentHardware = isNvidia
            }
        };
    }

    /// <summary>
    /// Detecta a GPU atual e retorna a página oficial de download do fabricante.
    /// Usada como opção primária; o link do Google Drive (Driver Clean) fica como secundário.
    /// </summary>
    private static string GetOfficialVendorPage(SystemSnapshot? snapshot)
    {
        var gpu = snapshot?.Graphics ?? "";

        if (gpu.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
            gpu.Contains("GeForce", StringComparison.OrdinalIgnoreCase))
        {
            return "https://www.nvidia.com/pt-br/drivers/";
        }

        if (gpu.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
            gpu.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
        {
            return "https://www.amd.com/pt/support";
        }

        if (gpu.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
            gpu.Contains("Arc", StringComparison.OrdinalIgnoreCase) ||
            gpu.Contains("Iris", StringComparison.OrdinalIgnoreCase) ||
            gpu.Contains("UHD", StringComparison.OrdinalIgnoreCase))
        { 
            return "https://www.intel.com/content/www/us/en/download-center/home.html";
        }

        // Fallback: página de driver limpo no Google Drive
        return UserDriverCleanLink;
    }

    public async Task<string> DownloadDirectAsync(
        DriverInfo driver,
        IProgress<(long read, long total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var url = driver.DirectDownloadUrl
            ?? throw new InvalidOperationException("Este driver não possui URL de download direto.");

        var fileName = !string.IsNullOrEmpty(driver.ExpectedFileName)
            ? driver.ExpectedFileName!
            : GetFileNameFromUrl(url);

        var targetPath = Path.Combine(DownloadsFolder, fileName);

        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1;
        long read = 0;

        await using var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        var buffer = new byte[81920];
        int bytesRead;
        while ((bytesRead = await httpStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        { 
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            read += bytesRead;
            progress?.Report((read, total));
        }

        return targetPath;
    }

    private static string GetFileNameFromUrl(string url)
    { 
        try
        {
            var uri = new Uri(url);
            var name = Path.GetFileName(uri.AbsolutePath);
            return string.IsNullOrWhiteSpace(name) ? "driver_download.bin" : name;
        }
        catch
        { 
            return "driver_download.bin";
        }
    }

    public string? GetExistingInstallerPath(DriverInfo driver)
    {
        if (!string.IsNullOrEmpty(driver.AlternateLocalPath) && File.Exists(driver.AlternateLocalPath))
        {
            return driver.AlternateLocalPath;
        }

        if (!string.IsNullOrEmpty(driver.ExpectedFileName))
        {
            var inDownloads = Path.Combine(DownloadsFolder, driver.ExpectedFileName);
            if (File.Exists(inDownloads))
            {
                return inDownloads;
            }
        }

        return null;
    }
}
