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
        new() { Id = "padrao", Name = "Versão Padrão", Description = "Revise ajustes de energia, fila de impressão, cache DNS e armazenamento.", Category = "Desempenho", Icon = "⚡", Operation = "padrao" },
        new() { Id = "gamer", Name = "Versão Avançada", Description = "Desempenho, latência de periféricos, modo MSI da GPU, políticas do Editor de Política de Grupo, privacidade e desativação de componentes em segundo plano.", Category = "Desempenho", Icon = "🎮", Operation = "gamer" },
        new() { Id = "debloat", Name = "Debloat & Privacidade", Description = "Remove bloatware do Windows, aplicativos desnecessários e reduz telemetria.", Category = "Limpeza", Icon = "🛡️", Operation = "debloat" },
        new() { Id = "quickclean", Name = "Limpeza Rápida", Description = "Analisa temporários do usuário e do Windows, preservando arquivos recentes.", Category = "Limpeza", Icon = "🧹", Operation = "quickclean" },
        new() { Id = "analisar", Name = "Diagnóstico / Análise", Description = "Analisa a integridade de CPU, memória, armazenamento e saúde geral do sistema.", Category = "Diagnóstico", Icon = "🔍", Operation = "analisar" },
        new() { Id = "benchmark", Name = "Benchmark do Sistema", Description = "Testa velocidade do processador, tempo de resposta e latência do sistema operacional.", Category = "Diagnóstico", Icon = "📊", Operation = "benchmark" },
        new() { Id = "sfc", Name = "Verificador de Arquivos (SFC)", Description = "Examina e repara arquivos corrompidos ou ausentes do Windows (sfc /scannow).", Category = "Manutenção", Icon = "🔧", Operation = "sfc" },
        new() { Id = "dism", Name = "Reparo de Imagem (DISM)", Description = "Restaura e corrige a imagem do sistema usando o repositório oficial da Microsoft.", Category = "Manutenção", Icon = "🛠️", Operation = "dism" },
        new() { Id = "chkdsk", Name = "Verificação de Disco (CHKDSK)", Description = "Executa uma verificação online do sistema de arquivos no disco principal.", Category = "Manutenção", Icon = "💾", Operation = "chkdsk" },
        new() { Id = "reparar", Name = "Reparar Configurações do Windows", Description = "Restaura os padrões do agendador multimídia (áudio e jogos) apagados por versões antigas do otimizador. Só adiciona o que estiver faltando.", Category = "Manutenção", Icon = "🔧", Operation = "reparar" },
        new() { Id = "update", Name = "Estado do Windows Update", Description = "Consulta o serviço, as últimas atualizações e reinicializações pendentes.", Category = "Manutenção", Icon = "🔄", Operation = "update" },
        new() { Id = "reverter", Name = "Reverter Última Otimização", Description = "Restaura o snapshot de configurações para o estado anterior à última execução.", Category = "Segurança", Icon = "↩️", Operation = "reverter" }
    };

    private AppConfig _config = new();
    private readonly string _primaryPath;
    private readonly string? _secondaryPath;

    public AppConfig Config => _config;

    public ConfigService(string? configPath = null)
    {
        _primaryPath = configPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PQueirozOptimizer", "config.json");
        _secondaryPath = Path.Combine(AppContext.BaseDirectory, "config.json");

        // If running from bin folder, locate source config.json
        var devPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\config.json"));
        if (!File.Exists(_secondaryPath) && File.Exists(devPath))
        {
            _secondaryPath = devPath;
        }

        Load();
    }

    public void Load()
    {
        string? json = null;
        var source = File.Exists(_primaryPath) ? _primaryPath : _secondaryPath != null && File.Exists(_secondaryPath) ? _secondaryPath : null;

        if (source != null)
        {
            try { json = File.ReadAllText(source); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LastLoadError = ex.Message; }
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
            catch (JsonException ex)
            {
                // Guarda uma cópia antes de voltar aos padrões, para não perder perfis do usuário
                LastLoadError = "Preferências corrompidas; os padrões foram restaurados. " + ex.Message;
                if (source == _primaryPath)
                {
                    try { File.Copy(_primaryPath, $"{_primaryPath}.corrompido-{DateTime.Now:yyyyMMddHHmmss}", true); }
                    catch (Exception copyError) when (copyError is IOException or UnauthorizedAccessException) { }
                }
                _config = new AppConfig();
            }
        }

        // Só grava quando os padrões mudaram algo (ou a cópia ainda não existe no perfil): regravar a
        // cada abertura disputava o arquivo com outra instância, como o atalho de Limpeza Rápida.
        var loaded = source == _primaryPath && LastLoadError is null ? JsonSerializer.Serialize(_config, JsonOpts) : null;
        EnsureDefaults();
        if (loaded != null && JsonSerializer.Serialize(_config, JsonOpts) == loaded) return;
        // Falha ao gravar não deve impedir o app de abrir: as preferências ficam em memória.
        try { Save(); }
        catch (IOException ex) { LastSaveError = ex.InnerException?.Message ?? ex.Message; }
    }

    /// <summary>Último erro ao gravar as preferências na inicialização, se houver.</summary>
    public string? LastSaveError { get; private set; }

    /// <summary>Último erro ao ler as preferências na inicialização, se houver.</summary>
    public string? LastLoadError { get; private set; }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_config, JsonOpts);
            Directory.CreateDirectory(Path.GetDirectoryName(_primaryPath)!);
            // Temporário por processo: duas instâncias salvando ao mesmo tempo não disputam o mesmo arquivo
            var temp = $"{_primaryPath}.{Environment.ProcessId}.tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _primaryPath, true);
        }
        catch (Exception ex) { throw new IOException("Não foi possível salvar suas preferências.", ex); }
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
                existing.EnabledOptimizations ??= new();
                existing.EnabledOptimizations.RemoveAll(id => id.Equals("inteligente", StringComparison.OrdinalIgnoreCase));
                if (ShowsEverything(existing.Name))
                {
                    existing.EnabledOptimizations = AllOptimizations.Select(o => o.Id).ToList();
                }
                else if (existing.EnabledOptimizations == null || existing.EnabledOptimizations.Count == 0)
                {
                    existing.EnabledOptimizations = def.EnabledOptimizations;
                }
                // Ferramentas novas entram nos perfis prontos que já existiam na config do usuário
                foreach (var id in def.EnabledOptimizations.Where(id => id == "reparar" && !existing.EnabledOptimizations.Contains(id)))
                    existing.EnabledOptimizations.Add(id);
            }
        }

        // Clean up any references to "inteligente" across all profiles
        _config.Profiles.RemoveAll(p => p is null || string.IsNullOrWhiteSpace(p.Name));
        foreach (var p in _config.Profiles)
        {
            // Perfis salvos sem lista (JSON antigo ou editado à mão) voltam a exibir tudo
            if (p.EnabledOptimizations is null || p.EnabledOptimizations.Count == 0)
                p.EnabledOptimizations = AllOptimizations.Select(o => o.Id).ToList();
            p.EnabledOptimizations.RemoveAll(id => id.Equals("inteligente", StringComparison.OrdinalIgnoreCase));
        }

        // Default to "Padrão" (which displays all optimizations)
        if (string.IsNullOrWhiteSpace(_config.ActiveProfile) || 
            !_config.Profiles.Any(p => p.Name.Equals(_config.ActiveProfile, StringComparison.OrdinalIgnoreCase)))
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
                Name = "Modo Avançado",
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
                EnabledOptimizations = new() { "analisar", "sfc", "dism", "chkdsk", "update", "reparar", "quickclean", "reverter" }
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

    /// <summary>Preferências de aparência; quem usava o tema claro antigo passa para "Automático".</summary>
    public static AppearanceSettings EffectiveAppearance(AppConfig config)
    {
        var appearance = config.Appearance ?? new AppearanceSettings();
        if (string.Equals(config.Theme, "Light", StringComparison.OrdinalIgnoreCase) && appearance.Theme == ThemeMode.Dark) appearance.Theme = ThemeMode.Auto;
        return appearance;
    }

    public void SaveAppearance(AppearanceSettings settings)
    {
        _config.Appearance = settings.Clone();
        _config.Theme = "Dark";
        Save();
    }

    public bool IsTutorialCompleted(string id) => _config.CompletedTutorials.Contains(id, StringComparer.OrdinalIgnoreCase);

    public void SetTutorialCompleted(string id, bool completed)
    {
        _config.CompletedTutorials.RemoveAll(t => t.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (completed) _config.CompletedTutorials.Add(id);
        Save();
    }

    /// <summary>"Rever tutoriais": todos voltam a aparecer.</summary>
    public void ResetTutorials()
    {
        _config.CompletedTutorials.Clear();
        Save();
    }

    /// <summary>Perfis que sempre exibem todas as otimizações: a lista é refeita a cada abertura.</summary>
    public static bool ShowsEverything(string profileName) =>
        profileName.Equals("Padrão", StringComparison.OrdinalIgnoreCase) || profileName.Equals("Modo Completo", StringComparison.OrdinalIgnoreCase);
}


