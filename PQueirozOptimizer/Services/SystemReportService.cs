using System.Globalization;
using System.IO;
using System.Management;
using System.Net;
using System.Text;

namespace PQueirozOptimizer.Services;

/// <summary>
/// Relatório do PC em um único arquivo HTML (abre em qualquer navegador, sem internet): sistema, placa-mãe e BIOS,
/// saúde dos discos e da bateria e erros de hardware dos últimos 30 dias. Útil para pedir suporte ou registrar o
/// estado antes de uma mudança. Só leitura; nada de número de série ou nome de usuário.
/// </summary>
public static class SystemReportService
{
    private static string L(string pt, string en) => Translator.IsEnglish ? en : pt;
    private static string H(string? text) => WebUtility.HtmlEncode(text ?? "");

    public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Qrztweaks");

    public static string Build(string appVersion)
    {
        var snapshot = new SystemInfoService().Read();
        var (boardMaker, boardModel, bios) = Board();
        var disks = HardwareHealthService.ReadDisks();
        var batteries = HardwareHealthService.ReadBatteries();
        var errors = HardwareHealthService.ErrorsSince(DateTime.Now.AddDays(-30));

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"").Append(Translator.IsEnglish ? "en" : "pt-BR").Append("\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.Append("<title>").Append(H(L("Relatório do PC", "PC report"))).Append("</title><style>");
        sb.Append(":root{--bg:#000;--card:#0e0e0e;--line:#1f1f1f;--fg:#f5f5f5;--muted:#8c8c8c}");
        sb.Append("@media (prefers-color-scheme: light){:root{--bg:#f5f5f5;--card:#fff;--line:#e2e2e2;--fg:#111;--muted:#5c5c5c}}");
        sb.Append("body{margin:0;background:var(--bg);color:var(--fg);font:14px/1.5 'Segoe UI',system-ui,sans-serif}main{max-width:980px;margin:0 auto;padding:32px 16px}");
        sb.Append("h1{font-size:26px;margin:0 0 4px}h2{font-size:12px;letter-spacing:.08em;text-transform:uppercase;color:var(--muted);margin:32px 0 10px}");
        sb.Append(".card{background:var(--card);border:1px solid var(--line);border-radius:16px;padding:18px 20px;margin-bottom:12px}");
        sb.Append(".row{display:flex;justify-content:space-between;gap:16px;border-top:1px solid var(--line);padding:8px 0}.row:first-of-type{border-top:0}");
        sb.Append(".k{color:var(--muted)}.v{font-family:'Cascadia Mono',Consolas,monospace;text-align:right}.tag{border:1px solid var(--fg);border-radius:20px;padding:2px 10px;font-size:11px;font-weight:600}");
        sb.Append(".tag.bad{background:var(--fg);color:var(--bg)}.muted{color:var(--muted)}.head{display:flex;justify-content:space-between;align-items:flex-start;gap:12px}");
        sb.Append("</style></head><body><main>");
        sb.Append("<h1>").Append(H(L("Relatório do PC", "PC report"))).Append("</h1>");
        sb.Append("<p class=\"muted\">").Append(H($"Qrztweaks {appVersion} · {DateTime.Now.ToString("g", CultureInfo.CurrentCulture)}")).Append("</p>");

        sb.Append("<h2>").Append(H(L("Sistema", "System"))).Append("</h2><div class=\"card\">");
        Row(sb, L("Windows", "Windows"), $"{snapshot.OperatingSystem} (build {snapshot.Build}, {snapshot.Architecture})");
        Row(sb, L("Processador", "Processor"), snapshot.Processor);
        Row(sb, L("Placa de vídeo", "Graphics"), snapshot.Graphics);
        Row(sb, L("Memória", "Memory"), snapshot.Memory);
        Row(sb, L("Placa-mãe", "Motherboard"), $"{boardMaker} {boardModel}".Trim());
        Row(sb, "BIOS", bios);
        Row(sb, L("Disco do sistema", "System drive"), L($"{snapshot.FreeSpace} livres de {snapshot.Storage}", $"{snapshot.FreeSpace} free of {snapshot.Storage}"));
        Row(sb, L("Tempo ligado", "Uptime"), snapshot.UptimeText);
        sb.Append("</div>");

        sb.Append("<h2>").Append(H(L("Erros de hardware (30 dias)", "Hardware errors (30 days)"))).Append("</h2><div class=\"card\">");
        if (errors.Error is { } error) Row(sb, L("Leitura", "Reading"), error);
        else
        {
            Row(sb, L("Erros WHEA (CPU, memória, PCIe)", "WHEA errors (CPU, memory, PCIe)"), errors.Whea.ToString(CultureInfo.CurrentCulture));
            Row(sb, L("Reinícios do driver de vídeo", "Graphics driver resets"), errors.GpuResets.ToString(CultureInfo.CurrentCulture));
            if (errors.Last is { } last) Row(sb, L("Último registro", "Last entry"), last);
        }
        sb.Append("</div>");

        Cards(sb, L("Discos", "Disks"), disks);
        if (batteries.Count > 0) Cards(sb, L("Bateria", "Battery"), batteries);
        sb.Append("</main></body></html>");
        return sb.ToString();
    }

    /// <summary>Grava o relatório na pasta Documentos\Qrztweaks e devolve o caminho.</summary>
    public static string Save(string appVersion)
    {
        Directory.CreateDirectory(DefaultFolder);
        var path = Path.Combine(DefaultFolder, (Translator.IsEnglish ? "pc-report-" : "relatorio-pc-") + DateTime.Now.ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture) + ".html");
        File.WriteAllText(path, Build(appVersion), new UTF8Encoding(false));
        return path;
    }

    private static void Row(StringBuilder sb, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        sb.Append("<div class=\"row\"><span class=\"k\">").Append(H(key)).Append("</span><span class=\"v\">").Append(H(value)).Append("</span></div>");
    }

    private static void Cards(StringBuilder sb, string title, IEnumerable<HealthCard> cards)
    {
        sb.Append("<h2>").Append(H(title)).Append("</h2>");
        foreach (var card in cards)
        {
            var bad = card.Verdict is HealthVerdict.Critical or HealthVerdict.Warning;
            sb.Append("<div class=\"card\"><div class=\"head\"><div><strong>").Append(H(card.Title)).Append("</strong><div class=\"muted\">").Append(H(card.Meta)).Append("</div></div>");
            sb.Append("<span class=\"tag").Append(bad ? " bad" : "").Append("\">").Append(H(VerdictText(card.Verdict))).Append("</span></div>");
            sb.Append("<p>").Append(H(string.Join(" ", card.Reasons))).Append("</p>");
            foreach (var (key, value) in card.Metrics) Row(sb, key, value);
            sb.Append("</div>");
        }
    }

    public static string VerdictText(HealthVerdict verdict) => verdict switch
    {
        HealthVerdict.Good => L("Saudável", "Healthy"),
        HealthVerdict.Warning => L("Atenção", "Warning"),
        HealthVerdict.Critical => L("Crítico", "Critical"),
        _ => L("Sem dados", "No data"),
    };

    private static (string Maker, string Model, string Bios) Board()
    {
        string maker = "", model = "", bios = "";
        try
        {
            using (var s = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard"))
                foreach (ManagementObject o in s.Get()) using (o) { maker = o["Manufacturer"]?.ToString()?.Trim() ?? ""; model = o["Product"]?.ToString()?.Trim() ?? ""; }
            using (var s = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS"))
                foreach (ManagementObject o in s.Get())
                    using (o)
                    {
                        bios = o["SMBIOSBIOSVersion"]?.ToString()?.Trim() ?? "";
                        if (o["ReleaseDate"] is string date && date.Length >= 8) bios += $" ({date[6..8]}/{date[4..6]}/{date[..4]})";
                    }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { }
        return (maker, model, bios);
    }
}
