namespace PQueirozOptimizer.Models;

public class DriverInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Vendor { get; set; } = ""; // NVIDIA, AMD, Intel, Realtek, Utilitário, Microsoft
    public string Category { get; set; } = ""; // GPU, Chipset, Áudio, Rede, Utilitários
    public string Version { get; set; } = "";
    public string Description { get; set; } = "";
    public string OfficialDownloadUrl { get; set; } = "";
    public string? DirectDownloadUrl { get; set; }
    public string? SecondaryDownloadUrl { get; set; }
    public string? SecondaryDownloadLabel { get; set; }
    public string? ExpectedFileName { get; set; }
    public string? AlternateLocalPath { get; set; }
    public bool IsRecommendedForCurrentHardware { get; set; }
}
