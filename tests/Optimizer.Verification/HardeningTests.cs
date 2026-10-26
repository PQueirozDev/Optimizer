using System.IO;
using PQueirozOptimizer.Engine;
using PQueirozOptimizer.Models;
using PQueirozOptimizer.Services;

/// <summary>
/// Fase 2 da reformulação: preferências que não podem ser apagadas, backups que não confundem "corrompido" com
/// "vazio", log exportado sem dados pessoais e o Laboratório que não declara ganho sem FPS medido.
/// </summary>
internal static class HardeningTests
{
    static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }

    public static void Run(string root)
    {
        Directory.CreateDirectory(root);
        Config(root);
        Backups(root);
        Redaction();
        Lab();
        Tutorial(root);
        Preferences(root);
        Games(root);
    }

    static void Games(string root)
    {
        // Sessões de jogo: abrir e fechar gravam a partida com a duração; fechar sem abrir não inventa nada
        var path = Path.Combine(root, "sessoes.json");
        if (File.Exists(path)) File.Delete(path);
        var log = new GameSessionLog(path);
        var profile = new UserProfile { Name = "CS2", Processes = new[] { "cs2.exe" } };
        var start = new DateTime(2026, 10, 20, 20, 0, 0);
        log.Started(profile, start);
        log.Ended(profile.Id, start.AddMinutes(95));
        log.Ended("outro-perfil", start.AddMinutes(99));
        var saved = new GameSessionLog(path).Sessions;
        Check(saved.Count == 1 && saved[0].Duration == TimeSpan.FromMinutes(95) && saved[0].ProfileName == "CS2", "Sessão de jogo gravada com início, fim e duração");
        log.Started(profile, start.AddHours(3));
        Check(new GameSessionLog(path).Sessions.Last().End is null, "Partida ainda aberta fica sem fim (não inventa duração)");

        // Programas de transmissão não disparam perfil: o perfil de streaming liga com o jogo
        Check(!ProfileStore.IsGameTrigger("obs64.exe") && !ProfileStore.IsGameTrigger("Discord.exe") && ProfileStore.IsGameTrigger("cs2.exe"), "OBS e Discord não disparam perfis; o jogo dispara");
        Check(ProfileTemplates.All().First(p => p.Name == "Streaming").Processes.Length == 0, "Modelo Streaming começa sem processo de gatilho (o usuário escolhe o jogo)");

        // Fechar o jogo encerra a sessão mesmo com o OBS ainda aberto
        var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "cs2.exe", "obs64.exe" };
        var streamer = new UserProfile { Name = "Stream", Processes = new[] { "cs2.exe", "obs64.exe" }, AutoGameMode = true };
        string? exited = null;
        var watcher = new GameWatcher(() => running, set => streamer.Processes.Where(ProfileStore.IsGameTrigger).Any(set.Contains) ? streamer : null, start: false);
        watcher.GameExited += id => exited = id;
        watcher.Poll();
        running.Remove("cs2.exe");
        watcher.Poll();
        Check(exited == streamer.Id, "Jogo fechado com o OBS aberto: a sessão termina e o Modo Jogo é restaurado");

        // O perfil do jogo é achado pelos executáveis da pasta, mesmo quando o jogo abre por um inicializador (Riot)
        var gameDir = Path.Combine(root, "jogo-riot", "live", "ShooterGame", "Binaries", "Win64");
        Directory.CreateDirectory(gameDir);
        File.WriteAllText(Path.Combine(gameDir, "VALORANT-Win64-Shipping.exe"), "");
        var riotGame = new LibraryGame("Riot", "valorant.live", "VALORANT", Path.Combine(root, "jogo-riot", "live"), @"C:\Riot Games\Riot Client\RiotClientServices.exe", null, "--launch-product=valorant");
        Check(GameLibraryService.BelongsTo(riotGame, new[] { "VALORANT-Win64-Shipping.exe" }) && !GameLibraryService.BelongsTo(riotGame, new[] { "RiotClientServices.exe", "obs64.exe" }),
            "Perfil achado pelo executável do jogo na pasta, não pelo inicializador");

        // Disco do Windows em HDD gera aviso na análise; SSD não
        var machine = new MachineSummary("CPU", "GPU", "1", 16, "100 GB livres de 500 GB", "Placa", "Windows 11", 26100, "Equilibrado", false, false, false, false, false, false, false);
        Check(SmartOptimizer.Notices(machine with { SystemDiskType = "HDD" }, OptimizationGoal.CompetitiveGaming).Any(n => n.Title == "Windows em HDD")
              && !SmartOptimizer.Notices(machine with { SystemDiskType = "SSD" }, OptimizationGoal.CompetitiveGaming).Any(n => n.Title == "Windows em HDD"), "Windows em HDD aparece como aviso na Otimização inteligente");

        // Riot: o comando de desinstalação vira o de abertura; nada fora do formato esperado é aceito
        var riot = GameLibraryService.ParseRiotUninstall("""
            "C:\Riot Games\Riot Client\RiotClientServices.exe" --uninstall-product=valorant --uninstall-patchline=live
            """);
        Check(riot is { Product: "valorant", Patchline: "live" } && riot.Value.Client.EndsWith("RiotClientServices.exe")
              && GameLibraryService.ParseRiotUninstall("""
                 "C:\x\RiotClientServices.exe" --uninstall-product=val&calc
                 """) is { Product: "val" }
              && GameLibraryService.ParseRiotUninstall("cmd /c RiotClientServices.exe --uninstall-product=x") is null, "Riot: produto e patchline lidos do desinstalador, sem aceitar comandos extras");

        // Gargalos: quadros irregulares viram achado com evidência; sem quadros, a análise diz que não avaliou
        var steady = new PerfResult { Process = "jogo.exe", DurationSeconds = 90, CpuAvg = 50, GpuAvg = 80, Frames = 9000, AverageFps = 120, AverageFrametimeMs = 8.3, P99FrametimeMs = 11, Low1Fps = 95 };
        var stutter = steady with { P99FrametimeMs = 25, Low1Fps = 40 };
        var sensorsOnly = steady with { Frames = null, AverageFps = null, AverageFrametimeMs = null, P99FrametimeMs = null, Low1Fps = null };
        Check(DiagnosticsEngine.Run(new DiagnosticInputs { LastCapture = stutter, RamTotalGb = 16 }).Findings.Any(f => f.Id == "frametime.unstable" && f.Evidence[0].Contains("25"))
              && !DiagnosticsEngine.Run(new DiagnosticInputs { LastCapture = steady, RamTotalGb = 16 }).Findings.Any(f => f.Id == "frametime.unstable"),
            "Gargalos: quadros irregulares viram achado com o frametime medido; quadros estáveis não");
        Check(DiagnosticsEngine.Run(new DiagnosticInputs { LastCapture = sensorsOnly, RamTotalGb = 16 }).MissingData.Any(m => m.Contains("frametime")),
            "Gargalos: sem quadros do PresentMon, a estabilidade aparece como não avaliada");

        // A biblioteca lê todas as lojas sem erro, mesmo sem nenhuma instalada
        var games = GameLibraryService.Scan();
        Check(games.All(g => g.LaunchUri != null || g.ExePath != null), $"Biblioteca: {games.Count} jogo(s), todos com um jeito de abrir ({string.Join(", ", games.Select(g => g.Store).Distinct())})");
    }

    static void Preferences(string root)
    {
        var dir = Path.Combine(root, "preferencias");
        Directory.CreateDirectory(dir);
        var source = Path.Combine(dir, "origem.json");
        if (File.Exists(source)) File.Delete(source);
        var original = new ConfigService(source);
        original.SaveProfile("Meu modo", "teste", new List<string> { "padrao" });
        original.SaveMonitorInterval(5);
        var exported = Path.Combine(dir, "exportado.json");
        original.ExportTo(exported);
        Check(!File.ReadAllText(exported).Contains("drive.google", StringComparison.OrdinalIgnoreCase), "Preferências exportadas sem links de download do catálogo de ISOs");

        var target = Path.Combine(dir, "destino.json");
        if (File.Exists(target)) File.Delete(target);
        var other = new ConfigService(target);
        other.ImportFrom(exported);
        Check(other.Config.Profiles.Any(p => p.Name == "Meu modo") && other.Config.MonitorIntervalSeconds == 5 && new ConfigService(target).Config.Profiles.Any(p => p.Name == "Meu modo"),
            "Importar traz modos e monitor do arquivo e grava");

        var foreign = Path.Combine(dir, "outro.json");
        File.WriteAllText(foreign, """{"nome":"não é do Qrztweaks"}""");
        var before = File.ReadAllText(target);
        var refused = false;
        try { other.ImportFrom(foreign); } catch (InvalidDataException) { refused = true; }
        Check(refused && File.ReadAllText(target) == before, "Arquivo que não é de preferências é recusado sem mexer nas atuais");

        var big = Path.Combine(dir, "grande.json");
        File.WriteAllText(big, "{\"profiles\":[],\"x\":\"" + new string('a', (int)ConfigService.ImportMaxBytes) + "\"}");
        refused = false;
        try { other.ImportFrom(big); } catch (InvalidDataException) { refused = true; }
        Check(refused, "Arquivo de preferências grande demais é recusado");

        var tampered = Path.Combine(dir, "alterado.json");
        File.WriteAllText(tampered, """{"language":"xx","monitorIntervalSeconds":3,"profiles":[{"name":"Estranho","enabledOptimizations":["padrao","Remove-Item C:\\"]}],"isoCatalog":[{"name":"ISO falsa","downloadUrl":"https://exemplo.invalid/x.iso"}]}""");
        other.ImportFrom(tampered);
        var odd = other.Config.Profiles.First(p => p.Name == "Estranho");
        Check(odd.EnabledOptimizations.SequenceEqual(new[] { "padrao" }) && other.Config.MonitorIntervalSeconds == 1 && other.Config.Language is "pt" or "en" && !other.Config.IsoCatalog.Any(i => i.Name == "ISO falsa"),
            "Importar descarta ajustes desconhecidos, links de ISO, idioma e intervalo inválidos");

        // Favoritos: marcar, desmarcar, limite e persistência
        var favPath = Path.Combine(dir, "favoritos.json");
        if (File.Exists(favPath)) File.Delete(favPath);
        var favs = new ConfigService(favPath);
        for (var i = 0; i < ConfigService.MaxFavorites; i++) favs.ToggleFavorite("Função " + i);
        var overLimit = favs.ToggleFavorite("Uma a mais");
        favs.ToggleFavorite("Função 0");
        var reloaded = new ConfigService(favPath);
        Check(!overLimit && !reloaded.IsFavorite("Uma a mais") && !reloaded.IsFavorite("Função 0") && reloaded.IsFavorite("Função 1") && reloaded.Config.Favorites.Count == ConfigService.MaxFavorites - 1,
            "Favoritos: marcar e desmarcar ficam salvos, com limite");

        var monitor = PQueirozOptimizer.Services.HardwareMonitorService.Shared;
        var previous = monitor.IntervalSeconds;
        monitor.IntervalSeconds = 3;
        Check(monitor.IntervalSeconds == 1, "Intervalo do monitor fora da lista volta a 1 s");
        monitor.IntervalSeconds = previous;
    }

    static void Tutorial(string root)
    {
        // Fechar o app no meio do tour: o passo fica salvo e o tour continua dele na próxima abertura
        var path = Path.Combine(root, "tutorial", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path)) File.Delete(path);
        var first = new ConfigService(path);
        first.SetWelcomeTourStep(4);
        Check(new ConfigService(path).WelcomeTourStep == 4 && !new ConfigService(path).IsTutorialCompleted("inicio"), "Tutorial: passo salvo ao fechar no meio e retomado na próxima abertura");
        first.SetTutorialCompleted("inicio", true);
        first.SetWelcomeTourStep(0);
        var done = new ConfigService(path);
        Check(done.IsTutorialCompleted("inicio") && done.WelcomeTourStep == 0, "Tutorial: concluído fica salvo e não aparece de novo sozinho");
        done.ResetTutorials();
        Check(!new ConfigService(path).IsTutorialCompleted("inicio"), "Tutorial: Rever tutoriais faz o tour aparecer de novo");
        File.WriteAllText(path, """{"welcomeTourStep":-7}""");
        Check(new ConfigService(path).WelcomeTourStep == 0, "Tutorial: passo inválido no arquivo começa do início");
        // Toda ajuda contextual tem os cinco campos preenchidos
        Check(PQueirozOptimizer.MainWindow.FeatureHelps.Values.All(h => new[] { h.What, h.When, h.Benefits, h.Risks, h.Undo }.All(t => t.Length > 20)), "Ajuda contextual: o que faz, quando usar, benefício, riscos e como desfazer em todas as funções");
    }

    static void Config(string root)
    {
        // Arquivo travado por outra instância: as preferências não podem ser trocadas pelos padrões
        var locked = Path.Combine(root, "travado", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(locked)!);
        var original = """{"activeProfile":"Meu modo","profiles":[{"name":"Meu modo","enabledOptimizations":["padrao"]}],"completedTutorials":["inicio"]}""";
        File.WriteAllText(locked, original);
        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var service = new ConfigService(locked);
            var refusedSave = false;
            try { service.Save(); } catch (IOException) { refusedSave = true; }
            Check(refusedSave && service.LastSaveError != null, "Preferências travadas: salvar avisa quem chamou em vez de fingir que gravou");
        }
        Check(File.ReadAllText(locked) == original, "Preferências travadas por outra instância não são apagadas");

        // Nulos no JSON (editado à mão ou de outra versão) não derrubam a inicialização
        var nulls = Path.Combine(root, "nulos", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(nulls)!);
        File.WriteAllText(nulls, """{"profiles":[null,{"name":null},{"name":"Meu","enabledOptimizations":null}],"completedTutorials":null,"isoCatalog":[null,{"name":null,"localPath":null}],"appearance":null}""");
        var loaded = new ConfigService(nulls);
        Check(loaded.Config.Profiles.Any(p => p.Name == "Meu") && loaded.Config.Profiles.Any(p => p.Name == "Padrão") && !loaded.IsTutorialCompleted("inicio") && loaded.LastLoadError is null,
            "Perfis, tutoriais e ISOs nulos no arquivo são ignorados sem perder o resto");

        // Tema de uma versão mais nova: só o tema volta ao padrão, os perfis ficam
        var future = Path.Combine(root, "futuro", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(future)!);
        File.WriteAllText(future, """{"profiles":[{"name":"Meu","enabledOptimizations":["padrao"]}],"appearance":{"theme":"Neon","accent":"Ultra","density":"Compact","cardSize":7}}""");
        var newer = new ConfigService(future);
        var appearance = newer.Config.Appearance;
        Check(newer.Config.Profiles.Any(p => p.Name == "Meu") && appearance.Theme == ThemeMode.Dark && appearance.Accent == AccentIntensity.Default && appearance.Density == Density.Compact && appearance.CardSize == CardSize.Medium,
            "Tema ou intensidade desconhecidos voltam ao padrão sem apagar os perfis");
    }

    static void Backups(string root)
    {
        var path = Path.Combine(root, "backup.json");
        if (File.Exists(path)) File.Delete(path);
        Check(SafeFile.ReadBackup<Dictionary<string, string[]>>(path, "de teste").Count == 0, "Backup ausente = nada guardado ainda");
        SafeFile.WriteAllText(path, """{"Ethernet":["1.1.1.1"]}""");
        Check(SafeFile.ReadBackup<Dictionary<string, string[]>>(path, "de teste")["Ethernet"][0] == "1.1.1.1" && !Directory.EnumerateFiles(root, "*.tmp").Any(),
            "Backup gravado por troca atômica, sem temporário sobrando");
        File.WriteAllText(path, """{"Ethernet":["1.1.""");
        var refused = false;
        try { SafeFile.ReadBackup<Dictionary<string, string[]>>(path, "de teste"); } catch (InvalidDataException) { refused = true; }
        Check(refused, "Backup cortado não é lido como vazio (a próxima alteração não grava o estado otimizado como original)");
    }

    static void Redaction()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var line = $"[2026-10-25 10:00:00] [INFO] Exclusão: {profile}\\Jogos · conta {Environment.UserName} no {Environment.MachineName} · fulano@exemplo.com · chave PQO1-ABCDEFGH12345678";
        var clean = ActivityLog.Redact(line);
        Check(!clean.Contains(profile, StringComparison.OrdinalIgnoreCase) && !clean.Contains("fulano@exemplo.com") && !clean.Contains("ABCDEFGH12345678")
              && clean.Contains("%USERPROFILE%\\Jogos") && clean.Contains("<email>") && clean.Contains("PQO1-<oculta>") && clean.Contains("[INFO]"),
            "Log exportado sem pasta do usuário, e-mail ou chave de licença");
        Check(!clean.Contains(" " + Environment.UserName + " ", StringComparison.OrdinalIgnoreCase) && !clean.Contains(" " + Environment.MachineName + " ", StringComparison.OrdinalIgnoreCase), "Log exportado sem o nome da conta e do PC");
    }

    static void Lab()
    {
        static PerfResult Run(double? cpu, double? fps = null, double? low = null) => new() { CpuAvg = cpu, AverageFps = fps, Low1Fps = low, Process = "jogo.exe", DurationSeconds = 60 };
        var sensorsOnly = OptimizationLab.Evaluate(new[] { Run(60), Run(61), Run(60) }, new[] { Run(40), Run(41), Run(40) });
        Check(sensorsOnly.Verdict == LabVerdict.NoProvenDifference, "Sem FPS medido o Laboratório não declara ganho, mesmo com menos uso de CPU");
        var missing = OptimizationLab.Evaluate(new[] { Run(null), Run(null), Run(null) }, new[] { Run(40), Run(41), Run(40) });
        Check(missing.Verdict == LabVerdict.InsufficientData, "Gravação sem valor medido não conta como zero");
        var zeroLow = OptimizationLab.Evaluate(new[] { Run(50, 100, 0), Run(50, 101, 0), Run(50, 100, 0) }, new[] { Run(50, 120, 10), Run(50, 121, 10), Run(50, 120, 10) });
        Check(zeroLow.Low1DeltaPercent is null && zeroLow.Verdict == LabVerdict.LikelyGain, "1% low zero antes não gera variação infinita");
        var imported = new PerfResult { Process = "jogo.exe", Source = "CSV importado" };
        var local = new PerfResult { Process = "jogo.exe", Conditions = new CaptureConditions("CPU", "GPU", "1", "Windows", 26100, "Qrz", "1920x1080", 144, "2.5") };
        Check(PerfMetrics.ConditionDifferences(imported, local).Any(d => d.Contains("desconhecidas")), "Log importado avisa que as condições não podem ser conferidas");
    }
}
