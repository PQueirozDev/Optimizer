using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Management;
using System.Text.RegularExpressions;

namespace PQueirozOptimizer.Services;

public enum HealthVerdict { Good, Warning, Critical, Unknown }

/// <summary>Um componente lido (disco, bateria, energia): veredito, motivos e as leituras que sustentam o veredito.</summary>
public sealed record HealthCard(string Title, string Meta, HealthVerdict Verdict, IReadOnlyList<string> Reasons, IReadOnlyList<(string Key, string Value)> Metrics);

/// <summary>Erros de hardware registrados pelo Windows (WHEA) e reinícios do driver de vídeo (TDR) desde uma data.</summary>
public sealed record HardwareErrors(int Whea, int GpuResets, string? Last, string? Error);

/// <summary>
/// Saúde do hardware lida só pelas APIs do Windows (sem driver de kernel): S.M.A.R.T. e contadores de confiabilidade
/// dos discos, desgaste e ciclos da bateria e os erros de hardware do log do sistema. Nada é estimado: o que o
/// Windows não informa aparece como "sem dados".
/// </summary>
public static class HardwareHealthService
{
    private static string L(string pt, string en) => Translator.IsEnglish ? en : pt;

    // ================= Discos =================
    private sealed class DiskRaw
    {
        public int Index; public string Model = "", Serial = "", Firmware = "", Bus = "", Media = "", Pnp = "", Letters = "";
        public long Size; public int Health = 5; public bool? PredictFail;
        public double? Temp, TempMax, Wear; public long? PowerOnHours, ReadErrUncorrected, WriteErrUncorrected, StartStop;
    }

    public static List<HealthCard> ReadDisks()
    {
        var disks = new Dictionary<int, DiskRaw>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Index, Model, SerialNumber, FirmwareRevision, InterfaceType, Size, PNPDeviceID FROM Win32_DiskDrive");
            foreach (ManagementObject o in searcher.Get())
            {
                using (o)
                {
                    var index = Convert.ToInt32(o["Index"] ?? 0, CultureInfo.InvariantCulture);
                    var pnp = o["PNPDeviceID"]?.ToString() ?? "";
                    disks[index] = new DiskRaw
                    {
                        Index = index, Model = o["Model"]?.ToString()?.Trim() ?? L($"Disco {index}", $"Disk {index}"),
                        Serial = o["SerialNumber"]?.ToString()?.Trim() ?? "", Firmware = o["FirmwareRevision"]?.ToString()?.Trim() ?? "",
                        Bus = pnp.StartsWith("USBSTOR", StringComparison.OrdinalIgnoreCase) ? "USB" : o["InterfaceType"]?.ToString() ?? "",
                        Size = Convert.ToInt64(o["Size"] ?? 0L, CultureInfo.InvariantCulture), Pnp = pnp,
                    };
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return new List<HealthCard> { new(L("Não foi possível ler os discos", "Could not read the disks"), "", HealthVerdict.Unknown, new[] { ex.Message }, Array.Empty<(string, string)>()) };
        }

        // Storage Management API: saúde, tipo e barramento por disco físico e os contadores de confiabilidade (SMART)
        try
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            scope.Connect();
            var byDeviceId = new Dictionary<string, int>();
            using (var physical = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT DeviceId, HealthStatus, MediaType, BusType FROM MSFT_PhysicalDisk")))
            {
                foreach (ManagementObject o in physical.Get())
                {
                    using (o)
                    {
                        var id = o["DeviceId"]?.ToString() ?? "";
                        if (!int.TryParse(id, out var index) || !disks.TryGetValue(index, out var d)) continue;
                        byDeviceId[id] = index;
                        d.Health = Convert.ToInt32(o["HealthStatus"] ?? 5, CultureInfo.InvariantCulture);
                        d.Media = Convert.ToInt32(o["MediaType"] ?? 0, CultureInfo.InvariantCulture) switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => "" };
                        d.Bus = Convert.ToInt32(o["BusType"] ?? 0, CultureInfo.InvariantCulture) switch { 17 => "NVMe", 11 => "SATA", 7 => "USB", 10 => "SAS", 8 => "RAID", _ => d.Bus };
                    }
                }
            }
            using var reliability = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM MSFT_StorageReliabilityCounter"));
            foreach (ManagementObject o in reliability.Get())
            {
                using (o)
                {
                    if (!byDeviceId.TryGetValue(o["DeviceId"]?.ToString() ?? "", out var index)) continue;
                    var d = disks[index];
                    d.Temp = Num(o["Temperature"]); d.TempMax = Num(o["TemperatureMax"]); d.Wear = Num(o["Wear"]);
                    d.PowerOnHours = (long?)Num(o["PowerOnHours"]); d.StartStop = (long?)Num(o["StartStopCycleCount"]);
                    d.ReadErrUncorrected = (long?)Num(o["ReadErrorsUncorrected"]); d.WriteErrUncorrected = (long?)Num(o["WriteErrorsUncorrected"]);
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { }

        // Aviso de falha iminente do próprio disco (SMART "predict failure"), por instância do driver
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT InstanceName, PredictFailure FROM MSStorageDriver_FailurePredictStatus");
            foreach (ManagementObject o in searcher.Get())
            {
                using (o)
                {
                    var instance = o["InstanceName"]?.ToString() ?? "";
                    var fail = o["PredictFailure"] is true;
                    foreach (var d in disks.Values.Where(d => d.Pnp.Length > 0 && instance.StartsWith(d.Pnp, StringComparison.OrdinalIgnoreCase)))
                        d.PredictFail = d.PredictFail == true || fail;
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { }

        try { FillLetters(disks); }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { }
        return disks.Values.OrderBy(d => d.Index).Select(DiskCard).ToList();
    }

    private static double? Num(object? value)
    {
        if (value is null) return null;
        try
        {
            var n = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return n is < 0 or > 1e12 ? null : n;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) { return null; }
    }

    private static void FillLetters(Dictionary<int, DiskRaw> disks)
    {
        var partitionToDisk = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using (var searcher = new ManagementObjectSearcher("SELECT Antecedent, Dependent FROM Win32_DiskDriveToDiskPartition"))
            foreach (ManagementObject o in searcher.Get())
                using (o)
                    if (RefId(o["Antecedent"]) is { } disk && RefId(o["Dependent"]) is { } partition) partitionToDisk[partition] = disk;
        var letters = new Dictionary<int, SortedSet<string>>();
        using (var searcher = new ManagementObjectSearcher("SELECT Antecedent, Dependent FROM Win32_LogicalDiskToPartition"))
            foreach (ManagementObject o in searcher.Get())
                using (o)
                {
                    if (RefId(o["Antecedent"]) is not { } partition || RefId(o["Dependent"]) is not { } letter || !partitionToDisk.TryGetValue(partition, out var disk)) continue;
                    var m = Regex.Match(disk, @"PHYSICALDRIVE(\d+)", RegexOptions.IgnoreCase);
                    if (!m.Success) continue;
                    var index = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    if (!letters.TryGetValue(index, out var set)) letters[index] = set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                    set.Add(letter);
                }
        foreach (var (index, set) in letters)
            if (disks.TryGetValue(index, out var d)) d.Letters = string.Join(" ", set);
    }

    private static string? RefId(object? reference)
    {
        var m = Regex.Match(reference?.ToString() ?? "", "DeviceID=\"(.*)\"");
        return m.Success ? m.Groups[1].Value.Replace(@"\\", @"\") : null;
    }

    private static HealthCard DiskCard(DiskRaw d)
    {
        var ssd = d.Media == "SSD" || d.Bus == "NVMe";
        var critical = new List<string>();
        var warning = new List<string>();
        var hasData = d.Health != 5 || d.PredictFail.HasValue || d.Wear.HasValue || d.Temp.HasValue || d.PowerOnHours.HasValue;
        if (d.PredictFail == true) critical.Add(L("O próprio disco avisa (S.M.A.R.T.) que deve falhar em breve. Faça backup agora.", "The disk itself warns (S.M.A.R.T.) that it is about to fail. Back up now."));
        if (d.Health == 2) critical.Add(L("O Windows classifica o disco como não saudável.", "Windows classifies the disk as unhealthy."));
        else if (d.Health == 1) warning.Add(L("O Windows classifica a saúde do disco como \"atenção\".", "Windows classifies the disk health as \"warning\"."));
        if (d.Wear is { } wear)
        {
            if (wear >= 95) critical.Add(L($"Desgaste de {wear:0}%: o SSD está no fim da vida útil. Faça backup e planeje a troca.", $"{wear:0}% wear: the SSD is at the end of its life. Back up and plan a replacement."));
            else if (wear >= 80) warning.Add(L($"Desgaste de {wear:0}%: o SSD já gastou boa parte da vida útil.", $"{wear:0}% wear: the SSD has used up most of its life."));
        }
        if (d.Temp is { } temp)
        {
            var (high, veryHigh) = ssd ? (70.0, 85.0) : (55.0, 65.0);
            if (temp >= veryHigh) critical.Add(L($"Temperatura de {temp:0} °C é alta demais para este disco.", $"{temp:0} °C is too hot for this disk."));
            else if (temp >= high) warning.Add(L($"Temperatura de {temp:0} °C está alta. Melhore a ventilação.", $"{temp:0} °C is high. Improve the airflow."));
        }
        if (d.ReadErrUncorrected > 0 || d.WriteErrUncorrected > 0)
            warning.Add(L($"Erros não corrigidos: {d.ReadErrUncorrected ?? 0} de leitura e {d.WriteErrUncorrected ?? 0} de escrita.", $"Uncorrected errors: {d.ReadErrUncorrected ?? 0} read and {d.WriteErrUncorrected ?? 0} write."));

        var verdict = critical.Count > 0 ? HealthVerdict.Critical : warning.Count > 0 ? HealthVerdict.Warning : hasData ? HealthVerdict.Good : HealthVerdict.Unknown;
        var reasons = critical.Concat(warning).ToList();
        if (reasons.Count == 0)
            reasons.Add(verdict == HealthVerdict.Good
                ? L("Nenhum problema nas leituras disponíveis.", "No problems in the available readings.")
                : L("Este disco não informa dados de saúde (comum em pendrives e alguns HDs externos).", "This disk doesn't report health data (common on USB sticks and some external drives)."));

        var media = d.Media.Length > 0 ? d.Media : ssd ? "SSD" : "";
        var meta = string.Join(" · ", new[] { media, d.Bus, SizeText(d.Size), d.Letters.Length > 0 ? L("Unidades ", "Drives ") + d.Letters : "" }.Where(s => s.Length > 0));
        var metrics = new List<(string, string)>();
        void Add(string key, string? value) { if (!string.IsNullOrEmpty(value)) metrics.Add((key, value)); }
        Add(L("Temperatura", "Temperature"), d.Temp is { } t ? $"{t:0} °C" + (d.TempMax is > 0 and { } max ? L($" (máx. {max:0})", $" (max {max:0})") : "") : null);
        Add(L("Desgaste", "Wear"), d.Wear is { } w ? $"{w:0}%" : null);
        Add(L("Horas ligado", "Power-on hours"), d.PowerOnHours is { } h ? $"{h:N0} h" : null);
        Add(L("Ciclos liga/desliga", "Start/stop cycles"), d.StartStop?.ToString("N0", CultureInfo.CurrentCulture));
        Add(L("Aviso de falha (S.M.A.R.T.)", "Failure warning (S.M.A.R.T.)"), d.PredictFail switch { true => L("Sim", "Yes"), false => L("Não", "No"), _ => null });
        Add(L("Estado no Windows", "Windows status"), d.Health switch { 0 => L("Saudável", "Healthy"), 1 => L("Atenção", "Warning"), 2 => L("Não saudável", "Unhealthy"), _ => null });
        Add("Firmware", d.Firmware);
        return new HealthCard(d.Model, meta, verdict, reasons, metrics);
    }

    private static string SizeText(long bytes) => bytes <= 0 ? "" : bytes >= 1e12 ? $"{bytes / 1e12:0.##} TB" : $"{bytes / 1e9:0} GB";

    // ================= Bateria e energia =================
    private static List<Dictionary<string, object?>> Query(string scope, string cls)
    {
        var list = new List<Dictionary<string, object?>>();
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, "SELECT * FROM " + cls);
            foreach (ManagementObject o in searcher.Get())
            {
                using (o)
                {
                    var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in o.Properties) row[p.Name] = p.Value;
                    list.Add(row);
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { }
        return list;
    }

    private static long? Long(Dictionary<string, object?>? row, string key)
    {
        if (row is null || !row.TryGetValue(key, out var v) || v is null) return null;
        try { return Convert.ToInt64(v, CultureInfo.InvariantCulture); }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) { return null; }
    }

    private static string? Str(Dictionary<string, object?>? row, string key) =>
        row is not null && row.TryGetValue(key, out var v) && v is string s && s.Trim().Length > 0 ? s.Trim() : null;

    /// <summary>Desgaste da bateria em % (capacidade perdida em relação à de projeto); null sem os dois valores.</summary>
    public static double? BatteryWear(long? designed, long? fullCharge) =>
        designed is > 0 && fullCharge is > 0 ? Math.Max(0, 100.0 * (1 - (double)fullCharge.Value / designed.Value)) : null;

    public static List<HealthCard> ReadBatteries()
    {
        var cards = new List<HealthCard>();
        var win = Query(@"root\cimv2", "Win32_Battery");
        var statics = Query(@"root\WMI", "BatteryStaticData");
        var full = Query(@"root\WMI", "BatteryFullChargedCapacity");
        var cycles = Query(@"root\WMI", "BatteryCycleCount");
        var status = Query(@"root\WMI", "BatteryStatus");
        var count = Math.Max(win.Count, statics.Count);
        static Dictionary<string, object?>? At(List<Dictionary<string, object?>> l, int i) => i < l.Count ? l[i] : null;
        for (var i = 0; i < count; i++)
        {
            var (w, st, f, c, s) = (At(win, i), At(statics, i), At(full, i), At(cycles, i), At(status, i));
            var designed = Long(st, "DesignedCapacity");
            var fullCharge = Long(f, "FullChargedCapacity");
            var cycleCount = Long(c, "CycleCount") is > 0 and var cc ? cc : (long?)null;
            var charge = Long(w, "EstimatedChargeRemaining");
            var online = s is not null && s.TryGetValue("PowerOnline", out var po) && po is true;
            var charging = s is not null && s.TryGetValue("Charging", out var ch) && ch is true;
            var wear = BatteryWear(designed, fullCharge);
            var verdict = HealthVerdict.Good;
            var reasons = new List<string>();
            if (wear is null) { verdict = HealthVerdict.Unknown; reasons.Add(L("O Windows não informou a capacidade de projeto ou a atual, então o desgaste não pôde ser calculado.", "Windows didn't report the design or current capacity, so wear couldn't be calculated.")); }
            else if (wear >= 50) { verdict = HealthVerdict.Critical; reasons.Add(L($"Perdeu {wear:0}% da capacidade original: a autonomia é metade do que era. Vale trocar a bateria.", $"Lost {wear:0}% of its original capacity: battery life is half what it was. Consider replacing it.")); }
            else if (wear >= 30) { verdict = HealthVerdict.Warning; reasons.Add(L($"Perdeu {wear:0}% da capacidade original. Ainda funciona, mas a autonomia caiu bastante.", $"Lost {wear:0}% of its original capacity. It still works, but battery life dropped a lot.")); }
            else reasons.Add(L($"Desgaste de {wear:0}%: normal para uma bateria com uso.", $"{wear:0}% wear: normal for a used battery."));
            if (cycleCount >= 800) reasons.Add(L($"{cycleCount} ciclos de carga: perto do fim da vida útil típica (500 a 1000).", $"{cycleCount} charge cycles: close to the typical end of life (500 to 1000)."));
            if (cycleCount >= 1000 && verdict == HealthVerdict.Good) verdict = HealthVerdict.Warning;

            var metrics = new List<(string, string)>();
            void Add(string key, string? value) { if (!string.IsNullOrEmpty(value)) metrics.Add((key, value)); }
            Add(L("Carga atual", "Current charge"), charge is { } ch2 ? $"{ch2}%" : null);
            Add(L("Estado", "State"), charging ? L("Carregando", "Charging") : online ? L("Na tomada", "Plugged in") : L("Na bateria", "On battery"));
            if (Long(w, "EstimatedRunTime") is > 0 and < 71582788 and var run && !online) Add(L("Autonomia estimada pelo Windows", "Runtime estimated by Windows"), $"{run / 60}h {run % 60:00}min");
            Add(L("Capacidade de projeto", "Design capacity"), designed is { } dc ? $"{dc / 1000.0:0.0} Wh" : null);
            Add(L("Capacidade máxima atual", "Current full capacity"), fullCharge is { } fc ? $"{fc / 1000.0:0.0} Wh" : null);
            Add(L("Desgaste", "Wear"), wear is { } wr ? $"{wr:0.0}%" : null);
            Add(L("Ciclos de carga", "Charge cycles"), cycleCount?.ToString(CultureInfo.CurrentCulture));
            if (Long(s, "Voltage") is > 0 and var volt) Add(L("Tensão", "Voltage"), $"{volt / 1000.0:0.00} V");
            if (!online && Long(s, "DischargeRate") is > 0 and var rate) Add(L("Consumo agora", "Power draw now"), $"{rate / 1000.0:0.0} W");
            Add(L("Fabricante", "Manufacturer"), Str(st, "ManufactureName"));
            var title = count > 1 ? L($"Bateria {i + 1}", $"Battery {i + 1}") : L("Bateria", "Battery");
            cards.Add(new HealthCard(title, Str(st, "DeviceName") ?? Str(w, "Name") ?? "", verdict, reasons, metrics));
        }
        return cards;
    }

    // ================= Erros de hardware =================
    /// <summary>
    /// Conta os erros WHEA (CPU, memória, PCIe) e os reinícios do driver de vídeo (TDR: Display 4101, nvlddmkm 13/153,
    /// amdkmdag) no log do Sistema desde <paramref name="since"/>. Depois de um overclock ou undervolt, qualquer um
    /// deles indica instabilidade mesmo sem tela azul.
    /// </summary>
    public static HardwareErrors ErrorsSince(DateTime since)
    {
        var time = since.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        try
        {
            var whea = Count($"*[System[Provider[@Name='Microsoft-Windows-WHEA-Logger'] and TimeCreated[@SystemTime>='{time}']]]", out var lastWhea);
            var gpu = Count($"*[System[((Provider[@Name='nvlddmkm'] and (EventID=13 or EventID=153)) or (Provider[@Name='Display'] and EventID=4101) or Provider[@Name='amdkmdag']) and TimeCreated[@SystemTime>='{time}']]]", out var lastGpu);
            return new HardwareErrors(whea, gpu, lastWhea ?? lastGpu, null);
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException) { return new HardwareErrors(0, 0, null, ex.Message); }
    }

    private static int Count(string xpath, out string? last)
    {
        last = null;
        var count = 0;
        using var reader = new EventLogReader(new EventLogQuery("System", PathType.LogName, xpath) { ReverseDirection = true });
        for (var record = reader.ReadEvent(); record != null; record = reader.ReadEvent())
        {
            using (record)
            {
                if (count++ > 0) continue;
                string description;
                try { description = record.FormatDescription() ?? ""; }
                catch (EventLogException) { description = ""; }
                var firstLine = description.Split('\n')[0].Trim();
                last = $"{record.TimeCreated:dd/MM HH:mm} · {record.ProviderName} {record.Id}" + (firstLine.Length > 0 ? " · " + firstLine : "");
            }
        }
        return count;
    }
}
