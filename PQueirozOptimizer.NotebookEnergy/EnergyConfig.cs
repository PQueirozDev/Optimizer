using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace PQueirozOptimizer.NotebookEnergy;

public sealed class Config
{
    private static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PQueirozOptimizer", "NotebookEnergy");
    internal static readonly string DefaultPath = Path.Combine(DirectoryPath, "config.json");
    internal const string RunName = "Qrztweaks.NotebookEnergy";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private string _path = DefaultPath;
    internal static string? StartupCommand { get; set; }
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public Mode Mode { get; set; }
    public bool StartWithWindows { get; set; }
    public bool Notify { get; set; } = true;
    public bool ControlWallpaper { get; set; }
    public bool LimitBrightness { get; set; }
    public bool TransparentTaskbar { get; set; } = true;
    public int? SavedBrightness { get; set; }

    public static Config Load() => LoadFrom(DefaultPath, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModoEnergia", "config.json"));

    internal static Config LoadFrom(string path, string? legacyPath = null)
    {
        var own = File.Exists(path);
        var source = own ? path : legacyPath is not null && File.Exists(legacyPath) ? legacyPath : null;
        var config = source is null ? new Config() : JsonSerializer.Deserialize<Config>(File.ReadAllText(source), Options)
            ?? throw new InvalidDataException("Não foi possível ler as preferências do modo de energia.");
        if (!Enum.IsDefined(config.Mode)) throw new InvalidDataException("Modo de energia desconhecido nas preferências.");
        if (config.SavedBrightness is < 0 or > 100) config.SavedBrightness = null;
        if (!own)
        {
            // Importa escolhas, sem assumir a inicialização ou uma alteração de brilho feita pelo programa anterior.
            config.StartWithWindows = false;
            config.SavedBrightness = null;
        }
        config._path = path;
        return config;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, Options));
            File.Move(temporary, _path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void ApplyStartup()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (StartWithWindows)
        {
            if (string.IsNullOrWhiteSpace(StartupCommand)) throw new InvalidOperationException("O caminho de inicialização do modo de energia não foi configurado.");
            key.SetValue(RunName, StartupCommand);
        }
        else key.DeleteValue(RunName, false);
    }
}
