using System.Globalization;
using PQueirozOptimizer.BiosAdvisor.Detector;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer.BiosAdvisor.Monitoring;

/// <summary>
/// Uma leitura dos sensores disponíveis. Null = sensor indisponível neste PC (nada é estimado para preencher).
/// O clock da CPU é estimado pelo Windows (% de desempenho × clock base).
/// </summary>
public sealed record SensorSnapshot(
    double? CpuUsage, double? CpuClockMhz, double? CpuTemperature, double? CpuPowerWatts, double? CpuPerformanceLimit,
    double? GpuUsage, double? GpuClockMhz, double? GpuTemperature, double? GpuPowerWatts, double? GpuPowerLimitWatts,
    IReadOnlyList<LocalizedText>? GpuThrottleReasons, DateTime At);

/// <summary>
/// Junta os sensores que o app já tem (uso de CPU e GPU do monitor ao vivo), contadores do Windows e, em placas NVIDIA,
/// o nvidia-smi. Temperatura e consumo da CPU exigem um driver de kernel que o app não instala: ficam indisponíveis.
/// </summary>
public sealed class SensorReader : IDisposable
{
    private readonly PdhCounter? _performance = PdhCounter.TryCreate(PdhCounter.ProcessorPerformance);
    private readonly PdhCounter? _limit = PdhCounter.TryCreate(PdhCounter.PerformanceLimit);
    private readonly int _baseMhz;
    private readonly bool _nvidia;

    public SensorReader(HardwareProfile profile)
    {
        _baseMhz = profile.Cpu.BaseClockMhz;
        _nvidia = profile.Gpus.Any(g => g.IsNvidia) && NvidiaSmi.Available;
        _performance?.Read(); _limit?.Read();
    }

    public SensorSnapshot Read()
    {
        var latest = HardwareMonitorService.Shared.Latest;
        var perf = _performance?.Read();
        var limit = _limit?.Read();
        double? cpuClock = perf is { } p && _baseMhz > 0 ? _baseMhz * p / 100 : null;
        double? gpuUsage = latest?.Gpu, gpuClock = null, gpuTemp = null, gpuPower = null, gpuLimit = null;
        List<LocalizedText>? reasons = null;
        if (_nvidia)
        {
            var row = NvidiaSmi.Query("utilization.gpu", "clocks.gr", "temperature.gpu", "power.draw", "power.limit", "clocks_throttle_reasons.active").FirstOrDefault();
            if (row != null)
            {
                gpuUsage = NvidiaSmi.Double(row[0]) ?? gpuUsage;
                gpuClock = NvidiaSmi.Double(row[1]);
                gpuTemp = NvidiaSmi.Double(row[2]);
                gpuPower = NvidiaSmi.Double(row[3]);
                gpuLimit = NvidiaSmi.Double(row[4]);
                reasons = ThrottleReasons(row[5]);
            }
        }
        return new SensorSnapshot(latest?.Cpu, cpuClock, null, null, limit, gpuUsage, gpuClock, gpuTemp, gpuPower, gpuLimit, reasons, DateTime.Now);
    }

    /// <summary>Motivos de redução de clock da NVIDIA (máscara de bits do nvidia-smi). Ocioso não conta. Null = máscara ilegível.</summary>
    public static List<LocalizedText>? ThrottleReasons(string? mask)
    {
        var list = new List<LocalizedText>();
        var text = (mask ?? "").Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
        if (!ulong.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var bits)) return null;
        if ((bits & 0x4) != 0) list.Add(new("Limite de energia (power cap)", "Power limit (power cap)"));
        if ((bits & 0x20) != 0) list.Add(new("Temperatura (software)", "Temperature (software)"));
        if ((bits & 0x40) != 0) list.Add(new("Temperatura (hardware)", "Temperature (hardware)"));
        if ((bits & 0x8) != 0) list.Add(new("Proteção de hardware", "Hardware slowdown"));
        if ((bits & 0x80) != 0) list.Add(new("Freio de energia da fonte", "Power brake (PSU)"));
        // Bits conhecidos e inofensivos: ocioso (0x1), clock de aplicativo (0x2), sync boost (0x10), clock de vídeo (0x100)
        if ((bits & ~0x1FFUL) != 0) list.Add(new("Outro motivo informado pelo driver", "Other reason reported by the driver"));
        return list;
    }

    public void Dispose() { _performance?.Dispose(); _limit?.Dispose(); }
}
