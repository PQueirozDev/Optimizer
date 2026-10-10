using System.Diagnostics;
using System.IO;
using System.Management;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer.Engine;

public enum DiagnosisSeverity { Info, Warning, Critical }
public enum DiagnosisConfidence { Low, Medium, High }

/// <summary>Um diagnóstico com a evidência que o sustenta. Nada é afirmado sem dado medido.</summary>
public sealed record Diagnosis(
    string Id, string Title, DiagnosisSeverity Severity, DiagnosisConfidence Confidence,
    IReadOnlyList<string> Evidence, IReadOnlyList<string> Causes, IReadOnlyList<string> Recommendations,
    string HowToConfirm, string Risks, string? RelatedPage = null);

public sealed record DiskReading(string Name, string MediaType, string Health, bool IsSystem);
public sealed record ProcessLoad(string Name, double CpuPercent);

/// <summary>Tudo o que os diagnósticos podem usar. Campos ausentes = não medido.</summary>
public sealed record DiagnosticInputs
{
    public double RamTotalGb { get; init; }
    public IReadOnlyList<HardwareSample> MonitorHistory { get; init; } = Array.Empty<HardwareSample>();
    public PerfResult? LastCapture { get; init; }
    public double? SystemDriveFreeGb { get; init; }
    public double? SystemDriveTotalGb { get; init; }
    public IReadOnlyList<DiskReading> Disks { get; init; } = Array.Empty<DiskReading>();
    public IReadOnlyList<string> ProblemDevices { get; init; } = Array.Empty<string>();
    public DateTime? GpuDriverDate { get; init; }
    public string? GpuName { get; init; }
    public int? ProcessCount { get; init; }
    public IReadOnlyList<ProcessLoad> TopProcesses { get; init; } = Array.Empty<ProcessLoad>();
    public int? StartupItems { get; init; }
    public string? PowerPlan { get; init; }
    public bool IsLaptop { get; init; }
    public bool? OnBattery { get; init; }
    public double? PagefileUsedPercent { get; init; }
    public TimeSpan? Uptime { get; init; }
    public double? CpuPerformanceLimitNow { get; init; }
    public IReadOnlyList<string> GpuThrottleNow { get; init; } = Array.Empty<string>();
    public DateTime Now { get; init; } = DateTime.Now;
}

/// <summary>Assistente local de diagnóstico (fase 8). Regras explícitas, sem IA paga e sem dados inventados.</summary>
public static class DiagnosticsEngine
{
    public sealed record Report(IReadOnlyList<Diagnosis> Findings, IReadOnlyList<string> MissingData);

    public static Report Run(DiagnosticInputs i)
    {
        var findings = new List<Diagnosis>();
        var missing = new List<string>();
        var cap = i.LastCapture;

        // ---------- Gargalos (só com um teste do Performance Lab) ----------
        if (cap is null || cap.DurationSeconds < 20 || cap.CpuAvg is null)
            missing.Add("Sem um teste do Performance Lab de pelo menos 20 segundos: limitações de CPU e GPU não podem ser avaliadas.");
        else if (cap.GpuAvg is null)
            missing.Add("O Windows não informou o uso da GPU no último teste: gargalo de GPU não avaliado.");
        else
        {
            var strong = cap.DurationSeconds >= 60 ? DiagnosisConfidence.High : DiagnosisConfidence.Medium;
            var fpsText = cap.AverageFps is { } f ? $"FPS médio {f:0}" : "sem FPS medido";
            if (cap.GpuAvg >= 95 && cap.CpuAvg < 85)
                findings.Add(new("bottleneck.gpu", "Limitado pela placa de vídeo no último teste", DiagnosisSeverity.Info, strong,
                    new[] { $"GPU em {cap.GpuAvg:0}% em média e CPU em {cap.CpuAvg:0}% durante {cap.DurationSeconds:0} s ({cap.Process ?? "processo não informado"}, {fpsText})." },
                    new[] { "A GPU é o componente mais exigido nessa configuração do jogo (o esperado em jogos pesados)." },
                    new[] { "Reduza a resolução ou as opções gráficas mais pesadas, ou ative DLSS/FSR/XeSS.", "Otimizações do Windows quase não mudam esse cenário." },
                    "Baixe a resolução: se o FPS subir na mesma proporção, a GPU era o limite.", "Nenhum: é uma leitura, não um problema."));
            else if (cap.GpuAvg < 75 && cap.AverageFps is { } fps && cap.Conditions is { RefreshHz: > 0 } c && fps < c.RefreshHz * 0.9 && !LooksCapped(fps))
                findings.Add(new("bottleneck.cpu", "Possível limitação de CPU no último teste", DiagnosisSeverity.Warning, cap.DurationSeconds >= 60 ? DiagnosisConfidence.Medium : DiagnosisConfidence.Low,
                    new[] { $"GPU em só {cap.GpuAvg:0}% com {fps:0} FPS, abaixo dos {c.RefreshHz} Hz do monitor.", $"CPU total em {cap.CpuAvg:0}% (um único núcleo saturado não aparece no total)." },
                    new[] { "Jogo limitado por um núcleo da CPU.", "Limite de FPS no jogo ou no driver.", "Memória lenta (XMP/EXPO desligado) ou processos em segundo plano." },
                    new[] { "Confira se não há limite de FPS ou V-Sync ligado.", "Feche programas em segundo plano (Modo Jogo).", "Confira no BIOS Advisor se o perfil XMP/EXPO está ativo." },
                    "Baixe a resolução: se o FPS não mudar, a CPU (ou um limite) é o gargalo.", "Nenhum: é uma leitura.", "biosadvisor"));
            if (cap.LimitedSeconds is > 0 && cap.DurationSeconds > 0)
            {
                var share = cap.LimitedSeconds.Value / cap.DurationSeconds * 100;
                if (share >= 10)
                    findings.Add(new("throttle.cpu", "A CPU foi limitada durante o teste", share >= 40 ? DiagnosisSeverity.Warning : DiagnosisSeverity.Info, DiagnosisConfidence.Medium,
                        new[] { $"O contador \"% Performance Limit\" do Windows ficou acima de zero em {cap.LimitedSeconds} de {cap.DurationSeconds:0} s ({share:0}%)." },
                        new[] { "Temperatura alta (cooler, pasta térmica, poeira).", "Limite de energia da placa-mãe ou do plano de energia." },
                        new[] { "Limpe o cooler e confira a pasta térmica.", "Confira o plano de energia e, no BIOS Advisor, os limites de energia." },
                        "Repita o teste com o gabinete aberto ou ventoinhas no máximo: se o limite sumir, é térmico.", "Limpeza interna exige cuidado com eletricidade estática.", "biosadvisor"));
            }
            if (cap.ThrottleReasons.Any(r => r.Contains("Temperatura", StringComparison.OrdinalIgnoreCase) || r.Contains("energia", StringComparison.OrdinalIgnoreCase)))
                findings.Add(new("throttle.gpu", "A placa de vídeo reduziu o clock no teste", DiagnosisSeverity.Warning, DiagnosisConfidence.High,
                    new[] { "O driver NVIDIA informou: " + string.Join(", ", cap.ThrottleReasons) + (cap.GpuTempMax is { } t ? $". Temperatura máxima {t:0} °C." : ".") },
                    new[] { "Temperatura ou limite de energia da placa." },
                    new[] { "Melhore o fluxo de ar do gabinete e limpe a placa.", "Confira se os cabos de energia da GPU estão bem conectados." },
                    "Acompanhe temperatura e clock no Performance Lab durante uma partida longa.", "Undervolt e mudanças de curva de ventoinha ficam por sua conta: o app não altera a GPU."));
        }

        // ---------- Memória ----------
        var ramValues = (cap?.Samples.Select(s => s.Ram).OfType<double>() ?? Enumerable.Empty<double>()).Concat(i.MonitorHistory.Select(s => s.Ram)).ToList();
        if (ramValues.Count >= 10)
        {
            var avg = ramValues.Average();
            if (avg >= 85)
                findings.Add(new("ram.pressure", "Memória RAM quase toda em uso", avg >= 92 ? DiagnosisSeverity.Critical : DiagnosisSeverity.Warning, ramValues.Count >= 30 ? DiagnosisConfidence.High : DiagnosisConfidence.Medium,
                    new[] { $"Uso médio de {avg:0}% em {ramValues.Count} leituras, com {i.RamTotalGb:0.#} GB instalados." }.Concat(i.PagefileUsedPercent is { } pf ? new[] { $"Arquivo de paginação em {pf:0}% de uso." } : Array.Empty<string>()).ToList(),
                    new[] { "Programas demais abertos (navegador com muitas abas é comum).", i.RamTotalGb < 12 ? "Pouca memória instalada para o uso atual." : "Algum programa usando memória fora do normal." },
                    new[] { "Feche programas que não está usando; confira o Gerenciador de Tarefas por uso de memória.", i.RamTotalGb < 12 ? "Para jogos atuais, 16 GB é o mínimo confortável." : "Reinicie o programa que mais consome." },
                    "Abra o Gerenciador de Tarefas → Memória e veja o que mais consome.", "Nenhum."));
        }
        else missing.Add("Poucas leituras de memória: deixe o monitor ao vivo rodando por alguns segundos.");

        // ---------- Armazenamento ----------
        if (i.SystemDriveFreeGb is { } free && i.SystemDriveTotalGb is { } total && total > 0)
        {
            var pct = free / total * 100;
            if (pct < 10 || free < 20)
                findings.Add(new("disk.space", "Pouco espaço livre no disco do Windows", pct < 5 ? DiagnosisSeverity.Critical : DiagnosisSeverity.Warning, DiagnosisConfidence.High,
                    new[] { $"{free:0.#} GB livres de {total:0.#} GB ({pct:0}%)." },
                    new[] { "Arquivos temporários, jogos grandes ou atualizações antigas do Windows." },
                    new[] { "Use a Limpeza rápida e a limpeza de componentes do Windows.", "Mova jogos para outra unidade." },
                    "Configurações → Sistema → Armazenamento mostra o que ocupa o disco.", "Apagar arquivos é definitivo: revise antes.", "dashboard"));
        }
        foreach (var disk in i.Disks.Where(d => !d.Health.Equals("Healthy", StringComparison.OrdinalIgnoreCase) && d.Health.Length > 0))
            findings.Add(new("disk.health", $"Disco com alerta de saúde: {disk.Name}", DiagnosisSeverity.Critical, DiagnosisConfidence.High,
                new[] { $"O Windows informa o estado \"{disk.Health}\" para este disco." }, new[] { "Desgaste ou falha física do disco." },
                new[] { "Faça backup dos seus arquivos agora.", "Confira o SMART com a ferramenta do fabricante e planeje a troca." },
                "Use a ferramenta do fabricante do disco (ou CrystalDiskInfo) para ver os atributos SMART.", "Continuar usando um disco com falha pode causar perda de dados."));
        if (i.Disks.FirstOrDefault(d => d.IsSystem) is { MediaType: "HDD" } hdd)
            findings.Add(new("disk.hdd", "O Windows está num HD mecânico", DiagnosisSeverity.Info, DiagnosisConfidence.High,
                new[] { $"{hdd.Name} é informado como HDD." }, new[] { "HDs têm acesso aleatório muito mais lento que SSDs." },
                new[] { "Um SSD para o Windows e os jogos é a melhoria de maior impacto em inicialização e carregamentos." },
                "Gerenciador de Tarefas → Desempenho mostra o tipo de cada disco.", "A migração exige clonar o disco ou reinstalar o Windows."));

        // ---------- Processos e inicialização ----------
        var heavy = i.TopProcesses.Where(p => p.CpuPercent >= 10).ToList();
        if (heavy.Count > 0 && heavy.Sum(p => p.CpuPercent) >= 25)
            findings.Add(new("processes.cpu", "Programas usando CPU em segundo plano", DiagnosisSeverity.Warning, DiagnosisConfidence.Medium,
                heavy.Select(p => $"{p.Name}: {p.CpuPercent:0}% da CPU no momento da análise.").ToList(),
                new[] { "Atualizações, antivírus, sincronização na nuvem ou um programa travado." },
                new[] { "Feche o que não precisa antes de jogar ou use o Modo Jogo, que pausa programas em segundo plano." },
                "Gerenciador de Tarefas → Processos, ordenado por CPU, por alguns minutos.", "Encerrar processos do sistema pode travar o Windows; feche só programas conhecidos.", "gaming"));
        if (i.StartupItems is > 15)
            findings.Add(new("startup.many", "Muitos programas iniciando com o Windows", DiagnosisSeverity.Info, DiagnosisConfidence.High,
                new[] { $"{i.StartupItems} itens ativos na inicialização." }, new[] { "Programas que se adicionam à inicialização ao serem instalados." },
                new[] { "Desative os que não precisa na página Inicialização (é reversível)." }, "Página Inicialização lista cada item e o impacto.", "Desativar drivers ou antivírus pode tirar funções; o app avisa quais são do Windows.", "startup"));
        if (i.ProcessCount is > 300)
            findings.Add(new("processes.many", "Quantidade alta de processos", DiagnosisSeverity.Info, DiagnosisConfidence.Low,
                new[] { $"{i.ProcessCount} processos em execução." }, new[] { "Muitos programas abertos ou serviços de terceiros." },
                new[] { "Confira o que está aberto e o que inicia com o Windows." }, "Gerenciador de Tarefas → Detalhes.", "Nenhum."));

        // ---------- Energia ----------
        if (i.PowerPlan == "Economia de energia" && (!i.IsLaptop || i.OnBattery == false))
            findings.Add(new("power.saver", "Plano Economia de energia ativo na tomada", DiagnosisSeverity.Warning, DiagnosisConfidence.High,
                new[] { "O plano ativo é Economia de energia" + (i.IsLaptop ? " com o notebook na tomada." : " num desktop.") },
                new[] { "Plano escolhido manualmente ou por um programa do fabricante." },
                new[] { "Use Equilibrado (ou o plano Qrz em desktops) quando estiver na tomada." }, "Painel de Controle → Opções de Energia.", "Mais consumo de energia."));

        // ---------- Drivers ----------
        if (i.ProblemDevices.Count > 0)
            findings.Add(new("drivers.problem", $"{i.ProblemDevices.Count} dispositivo(s) com problema de driver", DiagnosisSeverity.Warning, DiagnosisConfidence.High,
                i.ProblemDevices.Take(8).Select(d => "Com erro no Gerenciador de Dispositivos: " + d).ToList(),
                new[] { "Driver ausente, corrompido ou incompatível." }, new[] { "Instale o driver do fabricante (página Drivers).", "Remova dispositivos fantasmas que não usa mais." },
                "Gerenciador de Dispositivos mostra um aviso amarelo nesses itens.", "Instale drivers só de fontes oficiais.", "drivers"));
        if (i.GpuDriverDate is { } date && (i.Now - date).TotalDays > 365)
            findings.Add(new("drivers.gpu.old", "Driver de vídeo com mais de um ano", DiagnosisSeverity.Info, DiagnosisConfidence.Medium,
                new[] { $"{i.GpuName ?? "Placa de vídeo"}: driver de {date:dd/MM/yyyy}." }, new[] { "Driver não atualizado." },
                new[] { "Atualize pelo site do fabricante (NVIDIA, AMD ou Intel). A página Drivers tem os links e a instalação limpa." },
                "Compare a versão instalada com a mais recente no site do fabricante.", "Drivers novos às vezes trazem problemas; a instalação limpa ajuda a voltar.", "drivers"));

        // ---------- Estado atual ----------
        if (i.CpuPerformanceLimitNow is > 5)
            findings.Add(new("throttle.now", "A CPU está limitada agora", DiagnosisSeverity.Info, DiagnosisConfidence.Low,
                new[] { $"\"% Performance Limit\" em {i.CpuPerformanceLimitNow:0}% no momento da análise." }, new[] { "Temperatura, limite de energia ou plano de economia." },
                new[] { "Grave um teste no Performance Lab durante uma carga real para confirmar." }, "Teste longo no Performance Lab.", "Nenhum."));
        if (i.Uptime is { TotalDays: > 14 } up)
            findings.Add(new("uptime", "Windows sem reiniciar há muito tempo", DiagnosisSeverity.Info, DiagnosisConfidence.High,
                new[] { $"Ligado há {up.TotalDays:0} dias (a inicialização rápida mantém o kernel entre desligamentos)." }, new[] { "Atualizações e vazamentos de memória se acumulam." },
                new[] { "Reinicie (Reiniciar, não Desligar) de vez em quando." }, "Gerenciador de Tarefas → Desempenho → CPU mostra o tempo ligado.", "Nenhum."));

        return new Report(findings.OrderByDescending(f => f.Severity).ThenByDescending(f => f.Confidence).ToList(), missing);
    }

    /// <summary>FPS muito perto de um limite comum (30, 60, 120...): provável limitador, não gargalo.</summary>
    public static bool LooksCapped(double fps) => new[] { 30, 60, 75, 90, 100, 120, 144, 165, 180, 240, 360 }.Any(c => Math.Abs(fps - c) <= c * 0.02);
}

/// <summary>Coleta os dados dos diagnósticos (WMI, contadores e o histórico do monitor). Leitura apenas.</summary>
public static class DiagnosticsCollector
{
    public static async Task<DiagnosticInputs> CollectAsync(Models.SystemSnapshot snapshot, MachineSummary? machine, PerfResult? lastCapture)
    {
        return await Task.Run(() =>
        {
            var system = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            double? free = null, total = null;
            try { var d = new DriveInfo(system); free = d.AvailableFreeSpace / 1073741824.0; total = d.TotalSize / 1073741824.0; } catch (IOException) { }
            var inputs = new DiagnosticInputs
            {
                RamTotalGb = snapshot.MemoryGb, MonitorHistory = HardwareMonitorService.Shared.History, LastCapture = lastCapture,
                SystemDriveFreeGb = free, SystemDriveTotalGb = total, Disks = Disks(), ProblemDevices = ProblemDevices(),
                ProcessCount = Process.GetProcesses().Length, TopProcesses = TopProcesses(), StartupItems = StartupCount(),
                PowerPlan = machine?.PowerPlan, IsLaptop = machine?.IsLaptop ?? !SystemConditions.IsDesktop, OnBattery = OnBattery(),
                PagefileUsedPercent = Pagefile(), Uptime = snapshot.Uptime,
            };
            var (gpuName, driverDate) = GpuDriver();
            using var limit = BiosAdvisor.Detector.PdhCounter.TryCreate(BiosAdvisor.Detector.PdhCounter.PerformanceLimit);
            limit?.Read(); Thread.Sleep(500);
            return inputs with { GpuName = gpuName, GpuDriverDate = driverDate, CpuPerformanceLimitNow = limit?.Read() };
        });
    }

    private static IEnumerable<ManagementObject> Query(string scope, string wql)
    {
        try { using var searcher = new ManagementObjectSearcher(scope, wql); return searcher.Get().Cast<ManagementObject>().ToList(); }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { return Array.Empty<ManagementObject>(); }
    }

    private static List<DiskReading> Disks()
    {
        var systemDisk = SystemDiskNumber();
        return Query(@"root\Microsoft\Windows\Storage", "SELECT DeviceId, FriendlyName, MediaType, HealthStatus FROM MSFT_PhysicalDisk").Select(o => new DiskReading(
            o["FriendlyName"]?.ToString() ?? "Disco",
            Convert.ToInt32(o["MediaType"] ?? 0) switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => "Desconhecido" },
            Convert.ToInt32(o["HealthStatus"] ?? 0) switch { 0 => "Healthy", 1 => "Warning", 2 => "Unhealthy", _ => "" },
            o["DeviceId"]?.ToString() == systemDisk)).ToList();
    }

    private static string? SystemDiskNumber()
    {
        var letter = (Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\")[0];
        return Query(@"root\Microsoft\Windows\Storage", $"SELECT DiskNumber FROM MSFT_Partition WHERE DriveLetter = '{letter}'").Select(o => o["DiskNumber"]?.ToString()).FirstOrDefault();
    }

    private static List<string> ProblemDevices() =>
        Query(@"root\CIMV2", "SELECT Name, ConfigManagerErrorCode, Present FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0")
            .Where(o => o["Present"] is not false && Convert.ToInt32(o["ConfigManagerErrorCode"] ?? 0) is not (0 or 22 or 45)) // 22 desativado pelo usuário, 45 desconectado
            .Select(o => o["Name"]?.ToString() ?? "Dispositivo sem nome").Distinct().ToList();

    private static (string?, DateTime?) GpuDriver()
    {
        foreach (var o in Query(@"root\CIMV2", "SELECT Name, DriverDate, AdapterCompatibility FROM Win32_VideoController"))
        {
            var name = o["Name"]?.ToString();
            if (name is null || name.Contains("Basic", StringComparison.OrdinalIgnoreCase) || name.Contains("Virtual", StringComparison.OrdinalIgnoreCase)) continue;
            var raw = o["DriverDate"]?.ToString();
            return (name, raw is { Length: >= 8 } && DateTime.TryParseExact(raw[..8], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var d) ? d : null);
        }
        return (null, null);
    }

    private static double? Pagefile()
    {
        var rows = Query(@"root\CIMV2", "SELECT AllocatedBaseSize, CurrentUsage FROM Win32_PageFileUsage").ToList();
        var allocated = rows.Sum(r => Convert.ToDouble(r["AllocatedBaseSize"] ?? 0));
        return allocated > 0 ? rows.Sum(r => Convert.ToDouble(r["CurrentUsage"] ?? 0)) / allocated * 100 : null;
    }

    private static bool? OnBattery()
    {
        var battery = Query(@"root\CIMV2", "SELECT BatteryStatus FROM Win32_Battery").FirstOrDefault();
        return battery is null ? null : Convert.ToInt32(battery["BatteryStatus"] ?? 0) == 1;
    }

    private static int? StartupCount()
    {
        var count = 0;
        foreach (var (hive, path) in new[] { (Microsoft.Win32.Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run"), (Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"), (Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run") })
        {
            using var key = hive.OpenSubKey(path);
            count += key?.ValueCount ?? 0;
        }
        foreach (var folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Startup), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup) })
            try { if (Directory.Exists(folder)) count += Directory.GetFiles(folder).Count(f => !f.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase)); } catch (IOException) { }
        return count;
    }

    /// <summary>Uso de CPU por processo em uma janela de 1 s (tempo de processador de cada um / tempo real / núcleos).</summary>
    private static List<ProcessLoad> TopProcesses()
    {
        var first = new Dictionary<int, (string Name, TimeSpan Cpu)>();
        foreach (var p in Process.GetProcesses())
        {
            try { first[p.Id] = (p.ProcessName, p.TotalProcessorTime); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            finally { p.Dispose(); }
        }
        var watch = Stopwatch.StartNew();
        Thread.Sleep(1000);
        var list = new List<ProcessLoad>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (first.TryGetValue(p.Id, out var before) && p.ProcessName != "Idle" && p.Id != Environment.ProcessId)
                    list.Add(new(p.ProcessName, (p.TotalProcessorTime - before.Cpu).TotalMilliseconds / watch.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100));
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            finally { p.Dispose(); }
        }
        return list.GroupBy(p => p.Name).Select(g => new ProcessLoad(g.Key, g.Sum(x => x.CpuPercent))).OrderByDescending(p => p.CpuPercent).Take(5).ToList();
    }
}
