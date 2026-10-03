using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PQueirozOptimizer.Services;

public sealed record LatencyResult(double? AverageMs, double? JitterMs, double LossPercent, double? MinMs, double? MaxMs);
public sealed record SpeedResult(double DownloadMbps, double UploadMbps, LatencyResult Latency);
public sealed record DnsProvider(string Id, string Name, string[] Servers, string Description);
public sealed record AdapterInfo(string Name, string Description, string Type, string Speed, string[] Dns, bool DnsFromDhcp);

/// <summary>
/// Ferramentas de rede: teste de velocidade (servidores da Cloudflare), latência e jitter, troca de DNS,
/// ajustes de latência reversíveis, reset da pilha de rede e sincronização do relógio.
/// </summary>
public sealed class NetworkService
{
    private readonly ActivityLog _log;
    private readonly RegistryTweakStore _tweaks;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly string DnsBackupPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OtimizadorPC", "Tweaks", "dns.json");

    public NetworkService(ActivityLog log, RegistryTweakStore? tweaks = null) { _log = log; _tweaks = tweaks ?? new RegistryTweakStore(); }

    // ================= Diagnóstico =================
    public static IReadOnlyList<AdapterInfo> ActiveAdapters()
    {
        var list = new List<AdapterInfo>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            var props = nic.GetIPProperties();
            if (props.GatewayAddresses.Count == 0) continue; // adaptadores virtuais sem saída para a internet
            var type = nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? "Cabo (Ethernet)" : nic.NetworkInterfaceType.ToString();
            var speed = nic.Speed >= 1_000_000_000 ? $"{nic.Speed / 1_000_000_000d:0.#} Gbps" : $"{nic.Speed / 1_000_000d:0} Mbps";
            var dns = props.DnsAddresses.Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).Select(a => a.ToString()).ToArray();
            list.Add(new AdapterInfo(nic.Name, nic.Description, type, speed, dns, IsDnsFromDhcp(nic.Id)));
        }
        return list;
    }

    private static bool IsDnsFromDhcp(string interfaceId)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{interfaceId}");
        return string.IsNullOrWhiteSpace(key?.GetValue("NameServer") as string);
    }

    public static async Task<LatencyResult> MeasureLatencyAsync(string host = "1.1.1.1", int count = 12, CancellationToken cancellationToken = default)
    {
        var times = new List<double>();
        using var ping = new Ping();
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var reply = await ping.SendPingAsync(host, 1500);
                if (reply.Status == IPStatus.Success) times.Add(reply.RoundtripTime);
            }
            catch (PingException) { }
            await Task.Delay(150, cancellationToken);
        }
        var loss = (count - times.Count) * 100d / count;
        if (times.Count == 0) return new LatencyResult(null, null, loss, null, null);
        // Jitter = variação média entre medições seguidas (como nos testes de VoIP e jogos)
        var jitter = times.Count > 1 ? times.Zip(times.Skip(1), (a, b) => Math.Abs(b - a)).Average() : 0;
        return new LatencyResult(times.Average(), jitter, loss, times.Min(), times.Max());
    }

    /// <summary>Teste de velocidade: 4 downloads e 2 uploads em paralelo por até ~8 s cada.</summary>
    public static async Task<SpeedResult> RunSpeedTestAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report("Medindo latência...");
        var latency = await MeasureLatencyAsync(cancellationToken: cancellationToken);
        progress?.Report("Medindo download...");
        var down = await MeasureAsync(async (token, add) =>
        {
            using var response = await Http.GetAsync("https://speed.cloudflare.com/__down?bytes=50000000", HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, token)) > 0) add(read);
        }, 4, cancellationToken);
        progress?.Report("Medindo upload...");
        var payload = new byte[8_000_000];
        Random.Shared.NextBytes(payload);
        var up = await MeasureAsync(async (token, add) =>
        {
            using var content = new ProgressContent(payload, add);
            using var response = await Http.PostAsync("https://speed.cloudflare.com/__up", content, token);
        }, 2, cancellationToken);
        return new SpeedResult(down, up, latency);
    }

    private static async Task<double> MeasureAsync(Func<CancellationToken, Action<int>, Task> transfer, int parallel, CancellationToken cancellationToken)
    {
        long bytes = 0;
        using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        window.CancelAfter(TimeSpan.FromSeconds(8));
        var watch = Stopwatch.StartNew();
        var tasks = Enumerable.Range(0, parallel).Select(async _ =>
        {
            // Repete a transferência até a janela de tempo acabar
            while (!window.IsCancellationRequested)
            {
                try { await transfer(window.Token, n => Interlocked.Add(ref bytes, n)); }
                catch (OperationCanceledException) { break; }
                catch (HttpRequestException) when (!cancellationToken.IsCancellationRequested) { await Task.Delay(200, CancellationToken.None); if (Interlocked.Read(ref bytes) == 0 && watch.Elapsed > TimeSpan.FromSeconds(4)) break; }
            }
        });
        await Task.WhenAll(tasks);
        cancellationToken.ThrowIfCancellationRequested();
        var seconds = Math.Max(0.5, watch.Elapsed.TotalSeconds);
        if (bytes == 0) throw new HttpRequestException("Sem resposta do servidor de teste. Verifique a conexão.");
        return bytes * 8 / seconds / 1_000_000;
    }

    private sealed class ProgressContent : HttpContent
    {
        private readonly byte[] _data; private readonly Action<int> _add;
        public ProgressContent(byte[] data, Action<int> add) { _data = data; _add = add; }
        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
        {
            for (var offset = 0; offset < _data.Length; offset += 65536)
            {
                var size = Math.Min(65536, _data.Length - offset);
                await stream.WriteAsync(_data.AsMemory(offset, size));
                _add(size);
            }
        }
        protected override bool TryComputeLength(out long length) { length = _data.Length; return true; }
    }

    // ================= DNS =================
    public static readonly DnsProvider[] DnsProviders =
    {
        new("auto", "Automático (provedor)", Array.Empty<string>(), "Volta ao DNS entregue pelo roteador."),
        new("cloudflare", "Cloudflare", new[] { "1.1.1.1", "1.0.0.1" }, "Rápido e com foco em privacidade."),
        new("google", "Google", new[] { "8.8.8.8", "8.8.4.4" }, "Estável e disponível no mundo todo."),
        new("quad9", "Quad9", new[] { "9.9.9.9", "149.112.112.112" }, "Bloqueia domínios maliciosos conhecidos."),
    };

    /// <summary>Mede o tempo de resposta de cada DNS (ping ao servidor), para ajudar na escolha.</summary>
    public static async Task<Dictionary<string, double?>> BenchmarkDnsAsync(CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, double?>();
        foreach (var p in DnsProviders.Where(p => p.Servers.Length > 0))
            result[p.Id] = (await MeasureLatencyAsync(p.Servers[0], 5, cancellationToken)).AverageMs;
        return result;
    }

    /// <summary>Aplica o DNS em todos os adaptadores ativos, guardando o anterior de cada um na primeira troca.</summary>
    public async Task SetDnsAsync(DnsProvider provider)
    {
        var adapters = ActiveAdapters();
        if (adapters.Count == 0) throw new InvalidOperationException("Nenhum adaptador de rede conectado.");
        var backup = LoadDnsBackup();
        foreach (var a in adapters)
            if (!backup.ContainsKey(a.Name)) backup[a.Name] = a.DnsFromDhcp ? Array.Empty<string>() : a.Dns;
        if (provider.Servers.Length > 0) SaveDnsBackup(backup);
        foreach (var a in adapters)
        {
            var servers = provider.Id == "auto" ? (backup.TryGetValue(a.Name, out var original) ? original : Array.Empty<string>()) : provider.Servers;
            await PowerShellBridge.RunScriptAsync(servers.Length == 0
                ? "Set-DnsClientServerAddress -InterfaceAlias $env:PQO_IF -ResetServerAddresses"
                : "Set-DnsClientServerAddress -InterfaceAlias $env:PQO_IF -ServerAddresses ($env:PQO_DNS -split ',')",
                new Dictionary<string, string> { ["PQO_IF"] = a.Name, ["PQO_DNS"] = string.Join(",", servers.Where(s => System.Net.IPAddress.TryParse(s, out _))) });
        }
        if (provider.Id == "auto" && File.Exists(DnsBackupPath)) File.Delete(DnsBackupPath);
        await PowerShellBridge.RunScriptAsync("Clear-DnsClientCache");
        _log.Write("SUCCESS", $"DNS alterado para {provider.Name} em {adapters.Count} adaptador(es)");
    }

    private static Dictionary<string, string[]> LoadDnsBackup()
    {
        try { return File.Exists(DnsBackupPath) ? JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(DnsBackupPath)) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or JsonException) { return new(); }
    }

    private static void SaveDnsBackup(Dictionary<string, string[]> backup)
    {
        var dir = Path.GetDirectoryName(DnsBackupPath)!;
        if (!Directory.Exists(dir)) { Directory.CreateDirectory(dir); RegistryTweakStore.ProtectDirectory(dir); }
        File.WriteAllText(DnsBackupPath, JsonSerializer.Serialize(backup));
    }

    /// <summary>DNS que está em uso: o nome do provedor conhecido ou "Automático".</summary>
    public static string CurrentDnsLabel()
    {
        var adapters = ActiveAdapters();
        if (adapters.Count == 0) return "Sem conexão";
        if (adapters.All(a => a.DnsFromDhcp)) return "Automático (provedor)";
        var servers = adapters.SelectMany(a => a.Dns).ToHashSet();
        return DnsProviders.FirstOrDefault(p => p.Servers.Length > 0 && p.Servers.Any(servers.Contains))?.Name ?? string.Join(", ", servers);
    }

    // ================= Ajustes de latência (reversíveis) =================
    public const string LatencyTweakId = "network-latency";
    private const string InterfacesKey = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
    private static readonly Regex InterfaceKeyPattern = new(@"^SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters\\Interfaces\\\{[0-9A-Fa-f-]{36}\}$", RegexOptions.IgnoreCase);
    public bool IsLatencyTweakApplied => _tweaks.IsApplied(LatencyTweakId);

    /// <summary>
    /// Desliga o algoritmo de Nagle e o ACK atrasado nos adaptadores conectados: o Windows envia pacotes
    /// pequenos na hora, sem agrupar. Ajuda jogos que usam TCP; não muda a velocidade da internet.
    /// </summary>
    public void ApplyLatencyTweak()
    {
        var writes = new List<RegistryWrite>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.GetIPProperties().GatewayAddresses.Count == 0) continue;
            var key = $@"{InterfacesKey}\{nic.Id}";
            if (!InterfaceKeyPattern.IsMatch(key)) continue;
            writes.Add(new(RegistryHive.LocalMachine, key, "TcpAckFrequency", RegistryValueKind.DWord, 1));
            writes.Add(new(RegistryHive.LocalMachine, key, "TCPNoDelay", RegistryValueKind.DWord, 1));
        }
        if (writes.Count == 0) throw new InvalidOperationException("Nenhum adaptador de rede conectado.");
        _tweaks.Apply(LatencyTweakId, "Ajustes de latência de rede", writes);
        _log.Write("SUCCESS", "Ajustes de latência de rede aplicados (Nagle e ACK atrasado desligados)");
    }

    public void RevertLatencyTweak()
    {
        _tweaks.Revert(LatencyTweakId, (hive, key, name) => hive == RegistryHive.LocalMachine && InterfaceKeyPattern.IsMatch(key) && name is "TcpAckFrequency" or "TCPNoDelay");
        _log.Write("SUCCESS", "Ajustes de latência de rede revertidos");
    }

    // ================= Reparos =================
    /// <summary>Reinicia Winsock e a pilha TCP/IP e limpa o DNS. Precisa reiniciar o PC.</summary>
    public async Task ResetNetworkAsync(IProgress<string>? progress = null)
    {
        foreach (var (file, args, label) in new[]
        {
            ("netsh.exe", new[] { "winsock", "reset" }, "Winsock reiniciado"),
            ("netsh.exe", new[] { "int", "ip", "reset" }, "Pilha TCP/IP reiniciada"),
            ("ipconfig.exe", new[] { "/flushdns" }, "Cache DNS limpo"),
        })
        {
            var code = await Task.Run(() => GamingService.RunTool(file, args));
            progress?.Report(code == 0 ? label : $"[ERRO] {label}: código {code}");
        }
        _log.Write("SUCCESS", "Rede redefinida (reinicie o PC para concluir)");
    }

    public async Task FlushDnsAsync()
    {
        await Task.Run(() => GamingService.RunTool("ipconfig.exe", "/flushdns"));
        _log.Write("SUCCESS", "Cache DNS limpo");
    }

    /// <summary>Sincroniza o relógio com o servidor de horário do Windows (relógio errado causa falhas de login e de certificado em jogos).</summary>
    public async Task<bool> SyncTimeAsync()
    {
        var ok = await Task.Run(() =>
        {
            GamingService.RunTool("net.exe", "start", "w32time");
            return GamingService.RunTool("w32tm.exe", "/resync", "/force") == 0;
        });
        _log.Write(ok ? "SUCCESS" : "WARN", ok ? "Relógio sincronizado com a internet" : "Não foi possível sincronizar o relógio agora");
        return ok;
    }
}
