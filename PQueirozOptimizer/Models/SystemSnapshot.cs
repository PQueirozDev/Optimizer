namespace PQueirozOptimizer.Models;

public sealed record SystemSnapshot(
    string OperatingSystem,
    string Build,
    string Architecture,
    string Processor,
    string Graphics,
    string Memory,
    string Storage,
    string FreeSpace,
    string Uptime,
    bool IsAdministrator);
