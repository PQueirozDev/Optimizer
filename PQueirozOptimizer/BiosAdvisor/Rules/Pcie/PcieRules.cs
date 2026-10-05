using System.Text.RegularExpressions;
using static PQueirozOptimizer.BiosAdvisor.R;

namespace PQueirozOptimizer.BiosAdvisor.Rules;

/// <summary>Link PCIe da placa de vídeo, Above 4G Decoding e Resizable BAR.</summary>
public sealed class PcieRules : IAdvisorRule
{
    // Placas de vídeo que já nascem com menos de 16 linhas: x8/x4 nelas não é erro de slot
    private static readonly Regex X8Gpus = new(@"RTX\s*(3050|4060|5060|5050)|RX\s*(6600|7600|9060)|Arc\s*A3", RegexOptions.IgnoreCase);
    private static readonly Regex X4Gpus = new(@"RX\s*(6400|6500)|GTX\s*1630", RegexOptions.IgnoreCase);

    /// <summary>Largura nativa do link da placa (x16 na maioria; algumas de entrada já nascem com x8/x4).</summary>
    public static int NativeWidth(string gpuName) => X4Gpus.IsMatch(gpuName) ? 4 : X8Gpus.IsMatch(gpuName) ? 8 : 16;

    public IEnumerable<AdvisorRecommendation> Evaluate(AdvisorContext c)
    {
        var gpu = c.Profile.PrimaryGpu;
        if (gpu is null || !gpu.IsDiscrete) yield break;

        // ---------- Link (só NVIDIA informa pelo nvidia-smi) ----------
        var link = gpu.Link;
        var slot = c.Board?.PrimaryGpuSlot;
        if (link?.PcieWidthMax is { } width)
        {
            var native = NativeWidth(gpu.Name);
            var ok = width >= native;
            var gen = link.PcieGenMax is { } g ? $"PCIe {g}.0 " : "";
            yield return new AdvisorRecommendation
            {
                Id = "pcie-link", SettingId = Settings.PcieLinkSpeed, Category = AdvisorCategory.Pcie,
                Name = T("Link PCIe da placa de vídeo", "Graphics card PCIe link"),
                Description = ok
                    ? T("A placa de vídeo negocia o link com o slot. Deixe a velocidade do slot em Auto para usar a maior geração suportada pelos dois.",
                        "The graphics card negotiates the link with the slot. Keep the slot speed on Auto to use the highest generation both support.")
                    : T($"A placa está com no máximo x{width} (ela suporta x{native}). Isso indica slot secundário ou linhas divididas: instale-a no slot principal{(slot is null ? "" : $" ({slot})")} e confira se nenhum M.2 divide as linhas desse slot.",
                        $"The card is limited to x{width} (it supports x{native}). That points to a secondary slot or shared lanes: install it in the main slot{(slot is null ? "" : $" ({slot})")} and check that no M.2 drive shares that slot's lanes."),
                RecommendedValue = T($"Auto · x{native}{(slot is null ? "" : $" no {slot}")}", $"Auto · x{native}{(slot is null ? "" : $" on {slot}")}"),
                CurrentValue = T($"{gen}x{width} (máximo negociado)", $"{gen}x{width} (maximum negotiated)"),
                Evidence = ok ? Evidence.Detected : Evidence.Inferred,
                Compliance = ok ? Compliance.Ok : Compliance.Attention,
                ExpectedBenefit = T("Sem gargalo de barramento para a placa de vídeo.", "No bus bottleneck for the graphics card."),
                Gain = ok ? Level.Low : Level.Medium, Risk = Level.Low, Thermal = ThermalImpact.None,
                Rollback = UndoToAuto,
            };
        }

        // ---------- Resizable BAR ----------
        var gpuSupport = GpuSupportsResizableBar(gpu);
        if (gpuSupport == false)
        {
            yield return NotApplicable("pcie-rebar", Settings.ResizableBar, T("Resizable BAR", "Resizable BAR"),
                T($"A {gpu.Name} não suporta Resizable BAR (NVIDIA RTX 30 em diante, AMD RX 6000 em diante e Intel Arc suportam). Above 4G Decoding também não traz ganho para ela.",
                    $"The {gpu.Name} doesn't support Resizable BAR (NVIDIA RTX 30 and newer, AMD RX 6000 and newer and Intel Arc support it). Above 4G Decoding brings no gain for it either."));
            yield break;
        }
        if (c.Platform.SupportsResizableBar == false)
        {
            yield return NotApplicable("pcie-rebar", Settings.ResizableBar, T("Resizable BAR", "Resizable BAR"),
                T("Este processador não suporta Resizable BAR oficialmente (Intel a partir da 10ª geração; AMD Ryzen 3000 em diante).",
                    "This CPU doesn't officially support Resizable BAR (Intel 10th gen and newer; AMD Ryzen 3000 and newer)."));
            yield break;
        }

        // NVIDIA: BAR1 do tamanho da VRAM indica ReBAR ativo; 256 MB indica desligado. É dedução, não leitura da BIOS.
        Evidence evidence = Evidence.NeedsBiosCheck;
        Compliance compliance = Compliance.Unknown;
        LocalizedText? current = null;
        if (link?.Bar1TotalMb is { } bar1 && gpu.VramBytes is { } vram && vram > 0)
        {
            var vramMb = vram / 1024 / 1024;
            evidence = Evidence.Inferred;
            compliance = bar1 >= vramMb * 0.9 ? Compliance.Ok : Compliance.Attention;
            current = compliance == Compliance.Ok
                ? T($"Ativo (janela BAR1 de {bar1} MB ≈ VRAM)", $"Active (BAR1 window of {bar1} MB ≈ VRAM)")
                : T($"Aparentemente desligado (janela BAR1 de {bar1} MB)", $"Appears off (BAR1 window of {bar1} MB)");
        }
        var legacy = c.Profile.Bios.Firmware == FirmwareMode.Legacy;
        yield return new AdvisorRecommendation
        {
            Id = "pcie-rebar", SettingId = Settings.ResizableBar, Category = AdvisorCategory.Pcie,
            Name = T("Above 4G Decoding + Resizable BAR", "Above 4G Decoding + Resizable BAR"),
            Description = T("Deixa o processador acessar toda a VRAM de uma vez. Precisa de Above 4G Decoding ligado, CSM desligado (Windows em UEFI), BIOS da placa com suporte e driver de vídeo atualizado. O ganho depende do jogo." +
                    (legacy ? " Este Windows iniciou em modo Legacy: resolva o modo de boot primeiro." : ""),
                "Lets the CPU access all VRAM at once. Requires Above 4G Decoding on, CSM off (Windows in UEFI), board BIOS support and an updated graphics driver. The gain depends on the game." +
                    (legacy ? " This Windows booted in Legacy mode: fix the boot mode first." : "")),
            RecommendedValue = T("Above 4G Decoding: Enabled · Re-Size BAR: Enabled/Auto", "Above 4G Decoding: Enabled · Re-Size BAR: Enabled/Auto"),
            CurrentValue = current,
            Evidence = evidence, Compliance = compliance,
            ExpectedBenefit = T("Alguns FPS a mais em jogos compatíveis; neutro na maioria dos outros.", "A few extra FPS in supported games; neutral in most others."),
            Gain = Level.Medium, Risk = legacy ? Level.High : Level.Low, Thermal = ThermalImpact.None,
            Rollback = Undo("Disabled", "Disabled"),
        };
    }

    /// <summary>Suporte da placa de vídeo pelo nome. Null = não sabemos.</summary>
    public static bool? GpuSupportsResizableBar(GpuInfo gpu)
    {
        var name = gpu.Name;
        if (gpu.IsNvidia)
        {
            if (Regex.IsMatch(name, @"RTX\s*[3-9]0\d0|RTX\s*A\d{3,4}", RegexOptions.IgnoreCase)) return true;
            if (Regex.IsMatch(name, @"RTX\s*20\d0|GTX|Quadro|TITAN", RegexOptions.IgnoreCase)) return false;
            return null;
        }
        if (gpu.IsAmd)
        {
            if (Regex.IsMatch(name, @"RX\s*(6\d{3}|7\d{3}|9\d{3})", RegexOptions.IgnoreCase)) return true;
            if (Regex.IsMatch(name, @"RX\s*(5\d{2,3}|4\d{2}|Vega)", RegexOptions.IgnoreCase)) return false;
            return null;
        }
        if (gpu.IsIntel && name.Contains("Arc", StringComparison.OrdinalIgnoreCase)) return true;
        return null;
    }

    private static AdvisorRecommendation NotApplicable(string id, string setting, LocalizedText name, LocalizedText reason) => new()
    {
        Id = id, SettingId = setting, Category = AdvisorCategory.Pcie, Weight = 0,
        Name = name, Description = reason,
        RecommendedValue = T("Não se aplica", "Not applicable"),
        Evidence = Evidence.NotApplicable, Compliance = Compliance.Info,
        ExpectedBenefit = T("Nenhum neste hardware.", "None on this hardware."),
        Gain = Level.Low, Risk = Level.Low, Thermal = ThermalImpact.None,
        Rollback = UndoToAuto,
    };
}
