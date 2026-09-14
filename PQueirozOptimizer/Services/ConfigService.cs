using System.IO;
using System.Text.Json;
using PQueirozOptimizer.Models;

namespace PQueirozOptimizer.Services;

public class ConfigService
{
    public const string UserIsoDriveLink = "https://drive.google.com/drive/folders/17FdoBwGHv8tfk_zThO3GXdvLJxVL2Mpf?usp=drive_link";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static readonly List<OptimizationDef> AllOptimizations = new()
    {
        new() { Id = "padrao", Name = "Versão Padrão", Description = "Limpeza de temporários, plano de energia, DNS rápido e TRIM em SSDs.", Category = "Desempenho", Icon = "⚡", Operation = "padrao" },
        new() { Id = "gamer", Name = "Versão Gamer", Description = "Modo de jogo, desativação de throttling, otimização de latência e agendamento de GPU.", Category = "Desempenho", Icon = "🎮", Operation = "gamer" },
        new() { Id = "debloat", Name = "Debloat & Privacidade", Description = "Remove bloatware do Windows, aplicativos desnecessários e reduz telemetria.", Category = "Limpeza", Icon = "🛡️", Operation = "debloat" },
        new() { Id = "quickclean", Name = "Limpeza Rápida", Description = "Limpa arquivos temporários do usuário e do Windows (Temp, Prefetch e logs).", Category = "Limpeza", Icon = "🧹", Operation = "quickclean" },
        new() { Id = "analisar", Name = "Diagnóstico / Análise", Description = "Analisa a integridade de CPU, memória, armazenamento e saúde geral do sistema.", Category = "Diagnóstico", Icon = "🔍", Operation = "analisar" },
        new() { Id = "benchmark", Name = "Benchmark do Sistema", Description = "Testa velocidade do processador, tempo de resposta e latência do sistema operacional.", Category = "Diagnóstico", Icon = "📊", Operation = "benchmark" },
        new() { Id = "sfc", Name = "Verificador de Arquivos (SFC)", Description = "Examina e repara arquivos corrompidos ou ausentes do Windows (sfc /scannow).", Category = "Manutenção", Icon = "🔧", Operation = "sfc" },
        new() { Id = "dism", Name = "Reparo de Imagem (DISM)", Description = "Restaura e corrige a imagem do sistema usando o repositório oficial da Microsoft.", Category = "Manutenção", Icon = "🛠️", Operation = "dism" },
        new() { Id = "chkdsk", Name = "Verificação de Disco (CHKDSK)", Description = "Agenda varredura de setores e sistema de arquivos no disco principal.", Category = "Manutenção", Icon = "💾", Operation = "chkdsk" },
        new() { Id = "update", Name = "Limpeza de Windows Update", Description = "Verifica atualizações pendentes e limpa caches de download antigos.", Category = "Manutenção", Icon = "🔄", Operation = "update" },
        new() { Id = "reverter", Name = "Reverter Última Otimização", Description = "Restaura o snapshot de configurações para o estado anterior à última execução.", Category = "Segurança", Icon = "↩️", Operation = "reverter" }
    };

    private AppConfig _config = new();
    private readonly string _primaryPath;
    private readonly string? _secondaryPath;

    public AppConfig Config => _config;

    public ConfigService()
    {
        _primaryPath = Path.Combine(AppContext.BaseDirectory, "config.json");

        // If running from bin folder, locate source config.json
        var devPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\config.json"));
        if (File.Exists(devPath))
        {
            _secondaryPath = devPath;
        }

        Load();
    }

    public void Load()
    {
        string? loadedFrom = null;
        string? json = null;

        if (File.Exists(_primaryPath))
        {
            try { json = File.ReadAllText(_primaryPath); loadedFrom = _primaryPath; } catch { }
        }
        else if (_secondaryPath != null && File.Exists(_secondaryPath))
        {
            try { json = File.ReadAllText(_secondaryPath); loadedFrom = _secondaryPath; } catch { }
        }

        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<AppConfig>(json, JsonOpts);
                if (parsed != null)
                {
                    _config = parsed;
                }
            }
            catch
            {
                _config = new AppConfig();
            }
        }

        EnsureDefaults();
        Save();
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_config, JsonOpts);
            File.WriteAllText(_primaryPath, json);

            if (!string.IsNullOrEmpty(_secondaryPath) && File.Exists(Path.GetDirectoryName(_secondaryPath)))
            {
                File.WriteAllText(_secondaryPath, json);
            }
        }
        catch { }
    }

    private void EnsureDefaults()
    {
        if (_config.Profiles == null)
            _config.Profiles = new();

        var builtIns = GetDefaultProfiles();
        foreach (var def in builtIns)
        {
            var existing = _config.Profiles.FirstOrDefault(p => p.Name.Equals(def.Name, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                _config.Profiles.Add(def);
            }
            else
            {
                existing.IsBuiltIn = true;
                // Remove removed optimizations like "inteligente"
                existing.EnabledOptimizations.RemoveAll(id => id.Equals("inteligente", StringComparison.OrdinalIgnoreCase));
                if (existing.Name.Equals("Padrão", StringComparison.OrdinalIgnoreCase))
                {
                    existing.EnabledOptimizations = AllOptimizations.Select(o => o.Id).ToList();
                }
                else if (existing.EnabledOptimizations == null || existing.EnabledOptimizations.Count == 0)
                {
                    existing.EnabledOptimizations = def.EnabledOptimizations;
                }
            }
        }

        // Clean up any references to "inteligente" across all profiles
        foreach (var p in _config.Profiles)
        {
            p.EnabledOptimizations?.RemoveAll(id => id.Equals("inteligente", StringComparison.OrdinalIgnoreCase));
        }

        // Default to "Padrão" (which displays all optimizations)
        if (string.IsNullOrWhiteSpace(_config.ActiveProfile) || 
            !_config.Profiles.Any(p => p.Name.Equals(_config.ActiveProfile, StringComparison.OrdinalIgnoreCase)) ||
            _config.ActiveProfile.Equals("Modo Gamer", StringComparison.OrdinalIgnoreCase))
        {
            _config.ActiveProfile = "Padrão";
        }

        if (_config.IsoCatalog == null)
            _config.IsoCatalog = new();

        // Ensure the custom ISO entry is present without depending on a developer machine path.
        var customIsoPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            "Win11-Custom.iso");
        var existingIso = _config.IsoCatalog.FirstOrDefault(i => 
            i.LocalPath.Equals(customIsoPath, StringComparison.OrdinalIgnoreCase) ||
            i.Name.Contains("Win11-Custom", StringComparison.OrdinalIgnoreCase) ||
            i.Name.Contains("Pedro Queiroz", StringComparison.OrdinalIgnoreCase));

        if (existingIso == null)
        {
            _config.IsoCatalog.Insert(0, new IsoEntry
            {
                Name = "Windows 11 Custom (Pedro Queiroz)",
                Version = "24H2 Custom",
                Architecture = "x64",
                Description = "ISO personalizada do Windows 11 com ajustes de latência, telemetria desativada e máxima performance para jogos.",
                LocalPath = File.Exists(customIsoPath) ? customIsoPath : "",
                DownloadUrl = UserIsoDriveLink,
                Requirements = "64-bit CPU, 4GB+ RAM, 64GB SSD",
                Changelog = "Otimizações de registro, debloat de UWP, plano de alto desempenho e Game Mode pré-configurados."
            });
        }
        else
        {
            if (string.IsNullOrWhiteSpace(existingIso.LocalPath) || !File.Exists(existingIso.LocalPath))
                existingIso.LocalPath = File.Exists(customIsoPath) ? customIsoPath : "";
            // Garante que o link do Google Drive (opção de download) esteja sempre presente
            existingIso.DownloadUrl = UserIsoDriveLink;
            if (string.IsNullOrWhiteSpace(existingIso.Name))
                existingIso.Name = "Windows 11 Custom (Pedro Queiroz)";
        }
    }

    private static List<OptimizationProfile> GetDefaultProfiles()
    {
        return new List<OptimizationProfile>
        {
            new()
            {
                Name = "Padrão",
                Description = "Modo padrão completo: exibe todas as otimizações e ferramentas disponíveis no aplicativo.",
                IsBuiltIn = true,
                EnabledOptimizations = AllOptimizations.Select(o => o.Id).ToList()
            },
            new()
            {
                Name = "Modo Gamer",
                Description = "Foco em jogos: latência reduzida, Game Mode, energia de alto desempenho e limpeza rápida.",
                IsBuiltIn = true,
                EnabledOptimizations = new() { "gamer", "padrao", "quickclean", "benchmark", "reverter" }
            },
            new()
            {
                Name = "Modo Minimalista",
                Description = "Apenas o essencial: visual limpo com otimização padrão, limpeza de temporários e reversão.",
                IsBuiltIn = true,
                EnabledOptimizations = new() { "padrao", "quickclean", "reverter" }
            },
            new()
            {
                Name = "Modo Completo",
                Description = "Exibe todas as otimizações, testes de benchmark e ferramentas de manutenção do Windows.",
                IsBuiltIn = true,
                EnabledOptimizations = AllOptimizations.Select(o => o.Id).ToList()
            },
            new()
            {
                Name = "Modo Manutenção",
                Description = "Ferramentas de integridade e diagnósticos: SFC, DISM, CHKDSK, Updates e análise de saúde.",
                IsBuiltIn = true,
                EnabledOptimizations = new() { "analisar", "sfc", "dism", "chkdsk", "update", "quickclean", "reverter" }
            },
            new()
            {
                Name = "Modo Debloat & Privacidade",
                Description = "Remoção de bloatwares, serviços em segundo plano desnecessários e preservação de privacidade.",
                IsBuiltIn = true,
                EnabledOptimizations = new() { "debloat", "padrao", "quickclean", "reverter" }
            }
        };
    }

    public OptimizationProfile GetActiveProfile()
    {
        var profile = _config.Profiles.FirstOrDefault(p => p.Name.Equals(_config.ActiveProfile, StringComparison.OrdinalIgnoreCase));
        if (profile == null)
        {
            profile = _config.Profiles.FirstOrDefault(p => p.Name.Equals("Padrão", StringComparison.OrdinalIgnoreCase)) 
                      ?? _config.Profiles.FirstOrDefault() 
                      ?? GetDefaultProfiles()[0];
            _config.ActiveProfile = profile.Name;
        }
        return profile;
    }

    public void SetActiveProfile(string profileName)
    {
        var match = _config.Profiles.FirstOrDefault(p => p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            _config.ActiveProfile = match.Name;
            Save();
        }
    }

    public void SaveProfile(string profileName, string description, List<string> enabledIds)
    {
        var existing = _config.Profiles.FirstOrDefault(p => p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.Description = description;
            existing.EnabledOptimizations = new List<string>(enabledIds);
        }
        else
        {
            var newProfile = new OptimizationProfile
            {
                Name = profileName,
                Description = description,
                IsBuiltIn = false,
                EnabledOptimizations = new List<string>(enabledIds)
            };
            _config.Profiles.Add(newProfile);
        }
        _config.ActiveProfile = profileName;
        Save();
    }

    public bool DeleteProfile(string profileName)
    {
        var existing = _config.Profiles.FirstOrDefault(p => p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));
        if (existing != null && !existing.IsBuiltIn)
        {
            _config.Profiles.Remove(existing);
            if (_config.ActiveProfile.Equals(profileName, StringComparison.OrdinalIgnoreCase))
            {
                _config.ActiveProfile = "Padrão";
            }
            Save();
            return true;
        }
        return false;
    }

    public void SaveTheme(string theme)
    {
        _config.Theme = theme;
        Save();
    }

    public void SaveLanguage(string language)
    {
        _config.Language = language;
        Save();
    }

    public void AddIso(IsoEntry iso)
    {
        _config.IsoCatalog.Add(iso);
        Save();
    }

    public void RemoveIso(IsoEntry iso)
    {
        _config.IsoCatalog.Remove(iso);
        Save();
    }
}
