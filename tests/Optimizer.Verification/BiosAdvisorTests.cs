using System.IO;
using PQueirozOptimizer.BiosAdvisor;
using PQueirozOptimizer.BiosAdvisor.Benchmark;
using PQueirozOptimizer.BiosAdvisor.Detector;
using PQueirozOptimizer.BiosAdvisor.Monitoring;
using PQueirozOptimizer.BiosAdvisor.Updates;

/// <summary>Testes do BIOS Advisor: motor de regras, análise de memória, detecção (com WMI simulado), benchmark e atualização.</summary>
internal static class BiosAdvisorTests
{
    static void Assert(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS BIOS Advisor: " + name); }

    // ---------- Perfis de teste ----------
    internal static HardwareProfile TufB460(string partNumber = "DDR4 3000", int configured = 2933, string locatorA = "ChannelA-DIMM2", string locatorB = "ChannelB-DIMM2", long? bar1 = 256, string gpu = "NVIDIA GeForce RTX 2060", int enabledCores = 8, int threads = 16)
    {
        MemoryModule M(string locator) => new(locator, "BANK", "A-DATA", partNumber, 8L << 30, 2933, configured, 1, MemoryType.Ddr4);
        return new HardwareProfile(
            new MotherboardInfo("ASUSTeK COMPUTER INC.", "TUF GAMING B460M-PLUS", "Rev 1.xx"),
            new BiosInfo("American Megatrends Inc.", "0708", new DateTime(2020, 7, 22), FirmwareMode.Uefi, true, true),
            new CpuInfo("Intel(R) Core(TM) i7-10700F CPU @ 2.90GHz", CpuVendor.Intel, "x64", 8, enabledCores, threads, 2900, true, false),
            new MemoryInfo(new[] { M(locatorA), M(locatorB) }, 4),
            new[] { new GpuInfo(gpu, "NVIDIA", 6L << 30, "32.0.16.1088", "PCI\\VEN_10DE&DEV_1F08", new GpuLinkInfo(3, 3, 16, 16, bar1)) },
            false, false) { Observed = new RuntimeObservations(160, 100) };
    }

    static HardwareProfile Ryzen5800X3D() => new(
        new MotherboardInfo("Micro-Star International Co., Ltd.", "MAG B550 TOMAHAWK (MS-7C91)", "1.0"),
        new BiosInfo("American Megatrends International, LLC.", "A.H0", null, FirmwareMode.Uefi, true, null),
        new CpuInfo("AMD Ryzen 7 5800X3D 8-Core Processor", CpuVendor.Amd, "x64", 8, 8, 16, 3400, false, true),
        new MemoryInfo(new[] { new MemoryModule("DIMM_A2", "P0 CHANNEL A", "G.Skill", "F4-3600C16-8GVKC", 8L << 30, 2133, 2133, 1, MemoryType.Ddr4),
                               new MemoryModule("DIMM_B2", "P0 CHANNEL B", "G.Skill", "F4-3600C16-8GVKC", 8L << 30, 2133, 2133, 1, MemoryType.Ddr4) }, 4),
        new[] { new GpuInfo("AMD Radeon RX 6700 XT", "Advanced Micro Devices, Inc.", 12L << 30, "31.0", "PCI\\VEN_1002&DEV_73DF", null) },
        false, false);

    static IEnumerable<AdvisorRecommendation> All(AdvisorReport r) => r.Recommendations;
    static AdvisorRecommendation? Get(AdvisorReport r, string id) => r.Recommendations.FirstOrDefault(x => x.Id == id);

    /// <summary>Detecção real deste PC (WMI, nvidia-smi, contadores e site da fabricante): imprime o resultado para conferir à mão.</summary>
    public static int Live(PQueirozOptimizer.Services.ActivityLog log)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var service = new BiosAdvisorService(log);
        var p = service.DetectAsync().GetAwaiter().GetResult();
        Console.WriteLine($"LIVE detecção em {sw.ElapsedMilliseconds} ms");
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(p, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        foreach (var preset in Enum.GetValues<AdvisorPreset>())
        {
            var r = BiosAdvisorEngine.Analyze(p, preset);
            Console.WriteLine($"LIVE {preset}: nota {r.Score.Overall?.ToString() ?? "-"} ({r.Score.Verifiable}/{r.Score.Total}) perfil={r.SpecificProfileName ?? "genérico"}");
            foreach (var x in r.Recommendations) Console.WriteLine($"   {x.Id,-22} {x.Evidence,-15} {x.Compliance,-9} {x.CurrentValue?.Pt}");
        }
        using var sensors = new SensorReader(p);
        Thread.Sleep(1500);
        Console.WriteLine("LIVE sensores: " + sensors.Read());
        var update = BiosUpdateCheck.CheckAsync(p, CancellationToken.None).GetAwaiter().GetResult();
        Console.WriteLine("LIVE atualização: " + update);
        return 0;
    }

    public static void Run()
    {
        PlatformTests();
        MemoryTests();
        EngineTests();
        DetectorTests();
        BenchmarkTests();
        UpdateTests();
        DatabaseTests();
        BiosReadTests();
    }

    static void PlatformTests()
    {
        var cpu = CpuModel.Parse("Intel(R) Core(TM) i7-10700F CPU @ 2.90GHz");
        Assert(cpu is { Vendor: CpuVendor.Intel, Tier: 7, Generation: 10, Number: "10700", Suffix: "F", Unlocked: false, HasIntegratedGraphics: false }, "i7-10700F: 10ª geração, bloqueado, sem vídeo integrado");
        Assert(CpuModel.Parse("Intel(R) Core(TM) i5-9600K CPU @ 3.70GHz") is { Generation: 9, Unlocked: true }, "i5-9600K: 9ª geração desbloqueado");
        Assert(CpuModel.Parse("AMD Ryzen 7 5800X3D 8-Core Processor") is { Vendor: CpuVendor.Amd, Generation: 5, IsX3D: true }, "Ryzen 7 5800X3D reconhecido como X3D série 5000");
        Assert(CpuModel.Parse("Intel(R) Core(TM) Ultra 7 265K") is { Brand: "Core Ultra", Generation: 2, Unlocked: true }, "Core Ultra 7 265K");
        Assert(PlatformAnalyzer.ParseChipset("TUF GAMING B460M-PLUS", CpuVendor.Intel) == "B460", "Chipset B460 pelo nome da placa");
        Assert(PlatformAnalyzer.ParseChipset("MAG B550 TOMAHAWK (MS-7C91)", CpuVendor.Amd) == "B550", "Chipset B550 pelo nome da placa");
        Assert(PlatformAnalyzer.ParseChipset("ROG STRIX X670E-E GAMING WIFI", CpuVendor.Amd) == "X670E", "X670E não vira X670");
        var p = PlatformAnalyzer.Analyze(TufB460());
        Assert(p.MemoryCapMhz == 2933 && p.ChipsetAllowsCpuOverclock == false && p.ChipsetAllowsMemoryOverclock == false, "B460 + i7: memória presa em 2933 MHz e sem overclock");
        Assert(p.PowerSpec is { Pl1Watts: 65, Pl2Watts: 224, TauSeconds: 28 }, "Limites Intel do i7-10700F: 65/224/28");
        Assert(p.SupportsResizableBar == true && p.PlatformPcieGen == 3, "10ª geração: PCIe 3.0 e ReBAR pela plataforma");
        var i5 = TufB460() with { Cpu = TufB460().Cpu with { Name = "Intel(R) Core(TM) i5-10400F CPU @ 2.90GHz" } };
        Assert(PlatformAnalyzer.Analyze(i5).MemoryCapMhz == 2666, "B460 + i5: memória presa em 2666 MHz");
    }

    static void MemoryTests()
    {
        Assert(MemoryAnalyzer.ChannelOf("ChannelA-DIMM2", "BANK 1") == "A" && MemoryAnalyzer.ChannelOf("DIMM_B1", "") == "B" && MemoryAnalyzer.ChannelOf("DIMM 0", "P0 CHANNEL B") == "B", "Canal pelo nome do slot (ASUS, MSI, AMD)");
        Assert(MemoryAnalyzer.ChannelOf("DIMM1", "BANK 0") is null, "Slot sem canal no nome fica desconhecido");
        Assert(MemoryAnalyzer.SlotOf("ChannelA-DIMM2") == "A2" && MemoryAnalyzer.SlotOf("DIMM_B1") == "B1", "Slot curto (A2/B1)");
        Assert(MemoryAnalyzer.ParsePartNumber("F4-3200C16D-16GVKB") == (3200, 16), "Part number G.Skill");
        Assert(MemoryAnalyzer.ParsePartNumber("CMK16GX4M2B3200C16") == (3200, 16), "Part number Corsair");
        Assert(MemoryAnalyzer.ParsePartNumber("KF432C16BB/8") == (3200, 16), "Part number Kingston Fury");
        Assert(MemoryAnalyzer.ParsePartNumber("DDR4 3000           ") == (3000, null), "Part number com velocidade em texto");
        Assert(MemoryAnalyzer.ParsePartNumber("M378A1K43CB2-CTD") == (null, null), "Part number desconhecido não inventa velocidade");

        var real = TufB460();
        var a = MemoryAnalyzer.Analyze(real.Memory, PlatformAnalyzer.Analyze(real));
        Assert(a.Channels == ChannelMode.Dual && a.ChannelEvidence == Evidence.Inferred && a.ExpectedMhz == 2933 && !a.BelowExpected, "TUF B460: dual channel, 2933 MHz já no limite");
        var slow = TufB460(partNumber: "F4-3200C16-8GVKB", configured: 2133);
        var s = MemoryAnalyzer.Analyze(slow.Memory, PlatformAnalyzer.Analyze(slow));
        Assert(s.BelowExpected && s.Issues.Count >= 1, "Memória a 2133 com pente 3200 em B460: XMP aparentemente desligado");
        var single = TufB460(locatorB: "ChannelA-DIMM1");
        Assert(MemoryAnalyzer.Analyze(single.Memory, PlatformAnalyzer.Analyze(single)).Channels == ChannelMode.Single, "Dois pentes no mesmo canal = single channel");
        var one = real with { Memory = new MemoryInfo(new[] { real.Memory.Modules[0] }, 4) };
        Assert(MemoryAnalyzer.Analyze(one.Memory, PlatformAnalyzer.Analyze(one)) is { Channels: ChannelMode.Single, ChannelEvidence: Evidence.Detected }, "Um pente só = single channel detectado");
        Assert(MemoryAnalyzer.Analyze(MemoryInfo.Empty, PlatformAnalyzer.Analyze(real)).Channels == ChannelMode.Unknown, "Sem pentes: nada é inventado");
    }

    static void EngineTests()
    {
        var safe = BiosAdvisorEngine.Analyze(TufB460(), AdvisorPreset.Safe);
        Assert(safe.SpecificProfileAvailable && safe.SpecificProfileName!.Contains("i7-10700F"), "Perfil específico TUF B460M-PLUS + i7-10700F");
        Assert(Get(safe, "mem-profile") is { Evidence: Evidence.Inferred, Compliance: Compliance.Ok }, "XMP: 2933 no B460 = perfil aparentemente aplicado (inferido)");
        Assert(Get(safe, "intel-turbo") is { Evidence: Evidence.Detected, Compliance: Compliance.Ok }, "Turbo observado pelo Windows = detectado");
        Assert(Get(safe, "intel-speedshift") is { Evidence: Evidence.NeedsBiosCheck, Compliance: Compliance.Unknown }, "Speed Shift precisa ser verificado na BIOS");
        Assert(Get(safe, "cpu-smt") is { Evidence: Evidence.Detected, Compliance: Compliance.Ok }, "Hyper-Threading detectado ligado");
        Assert(Get(safe, "pcie-rebar") is { Evidence: Evidence.NotApplicable }, "RTX 2060: Resizable BAR não se aplica");
        Assert(Get(safe, "mem-slots") is { Compliance: Compliance.Ok }, "Slots A2 + B2 conferem com o perfil da placa");
        Assert(Get(safe, "intel-power-default") != null && Get(safe, "intel-power-raised") is null, "Seguro: limites padrão, sem PL1 elevado");
        Assert(Get(safe, "intel-cstates") is null && Get(safe, "latency-spread") is null, "Seguro não mostra itens competitivos");
        Assert(Get(safe, "mem-profile")!.Guide is { HasConfirmedPath: true } g && g.Steps[0] == "Advanced Mode (F7)" && g.Steps.Contains("Ai Overclock Tuner"), "Guia ASUS: Advanced Mode (F7) → Ai Tweaker → Ai Overclock Tuner");
        Assert(Get(safe, "intel-speedshift")!.Guide is { HasConfirmedPath: true } ss && ss.Steps.Contains("CPU Power Management Configuration"), "Guia ASUS do Speed Shift pelo manual da série 400");

        var perf = BiosAdvisorEngine.Analyze(TufB460(), AdvisorPreset.Performance);
        Assert(Get(perf, "intel-power-raised") is { Risk: Level.Medium, Thermal: ThermalImpact.High } r && r.RecommendedValue.Pt.Contains("125 W") && r.RecommendedValue.Pt.Contains("224 W"), "Desempenho: PL1 125 W / PL2 224 W com risco médio");
        Assert(Get(perf, "intel-power-default") is null, "Desempenho não repete os limites padrão (sem recomendações opostas)");
        var comp = BiosAdvisorEngine.Analyze(TufB460(), AdvisorPreset.Competitive);
        Assert(Get(comp, "intel-cstates") is { Weight: 0 } && Get(comp, "latency-spread") != null, "Competitivo: C-States e Spread Spectrum como opcionais");
        Assert(!All(comp).Any(x => x.Id is "sec-secureboot" or "sec-tpm" or "virt-vtx" && x.RecommendedValue.En.StartsWith("Disabled")), "Nenhum preset manda desligar segurança ou virtualização");
        Assert(!All(comp).Any(x => x.Id == "intel-thermal-monitor" && !x.RecommendedValue.En.StartsWith("Enabled")), "Thermal Monitor sempre Enabled");

        // Nota: só com evidência; confirmações entram e valem só para a mesma BIOS
        var before = safe.Score;
        Assert(before.Overall is >= 0 and <= 100 && before.Verifiable < before.Total, "Nota existe e itens sem evidência ficam fora");
        var confirmed = BiosAdvisorEngine.Analyze(TufB460(), AdvisorPreset.Safe, new HashSet<string> { "intel-speedshift", "mem-profile" });
        Assert(Get(confirmed, "intel-speedshift") is { Evidence: Evidence.UserConfirmed, Compliance: Compliance.Ok } && confirmed.Score.Verifiable == before.Verifiable + 1, "Confirmação do usuário entra na nota");
        Assert(Get(confirmed, "mem-profile")!.Evidence == Evidence.Inferred, "Confirmação não substitui uma leitura/dedução real");
        var state = new BiosAdvisorState { ConfirmationsFingerprint = TufB460().Fingerprint, Confirmed = { "intel-speedshift" } };
        var newBios = TufB460() with { Bios = TufB460().Bios with { Version = "2003" } };
        Assert(BiosAdvisorStore.ValidConfirmations(state, TufB460()).Count == 1 && BiosAdvisorStore.ValidConfirmations(state, newBios).Count == 0, "Confirmações caem quando a BIOS muda");

        // Problemas detectados viram "Ajustar"
        var bad = BiosAdvisorEngine.Analyze(TufB460(partNumber: "F4-3200C16-8GVKB", configured: 2133, enabledCores: 4, threads: 4, locatorB: "ChannelA-DIMM1"), AdvisorPreset.Safe);
        Assert(Get(bad, "mem-profile")!.Compliance == Compliance.Attention && Get(bad, "cpu-active-cores")!.Compliance == Compliance.Attention && Get(bad, "mem-channels")!.Compliance == Compliance.Attention, "XMP desligado, núcleos e canal único aparecem para ajustar");
        Assert(bad.Score.Overall < safe.Score.Overall, "Nota cai com problemas detectados");
        Assert(Get(bad, "cpu-smt")!.Compliance == Compliance.Attention, "i7-10700F com 1 thread por núcleo: Hyper-Threading desligado");

        // ReBAR: RTX 3060 com BAR1 de 256 MB = aparentemente desligado; do tamanho da VRAM = ativo
        var off = BiosAdvisorEngine.Analyze(TufB460(gpu: "NVIDIA GeForce RTX 3060", bar1: 256), AdvisorPreset.Safe);
        var on = BiosAdvisorEngine.Analyze(TufB460(gpu: "NVIDIA GeForce RTX 3060", bar1: 8192), AdvisorPreset.Safe);
        Assert(Get(off, "pcie-rebar") is { Evidence: Evidence.Inferred, Compliance: Compliance.Attention } && Get(on, "pcie-rebar") is { Compliance: Compliance.Ok }, "ReBAR deduzido pelo BAR1");
        var x8 = BiosAdvisorEngine.Analyze(TufB460() with { Gpus = new[] { TufB460().Gpus[0] with { Link = new GpuLinkInfo(3, 3, 8, 8, 256) } } }, AdvisorPreset.Safe);
        Assert(Get(x8, "pcie-link")!.Compliance == Compliance.Attention, "RTX 2060 em x8: slot errado");
        var x8native = BiosAdvisorEngine.Analyze(TufB460(gpu: "NVIDIA GeForce RTX 4060") with { Gpus = new[] { new GpuInfo("NVIDIA GeForce RTX 4060", "NVIDIA", 8L << 30, "1", "PCI\\VEN_10DE", new GpuLinkInfo(3, 4, 8, 8, 8192)) } }, AdvisorPreset.Safe);
        Assert(Get(x8native, "pcie-link")!.Compliance == Compliance.Ok, "RTX 4060 em x8 é normal (placa x8)");

        // AMD
        var amd = BiosAdvisorEngine.Analyze(Ryzen5800X3D(), AdvisorPreset.Performance);
        Assert(!amd.SpecificProfileAvailable && amd.Warnings.Any(w => w.Pt.Contains("Perfil específico ainda não disponível")), "Placa fora do banco: aviso e recomendações genéricas");
        Assert(Get(amd, "amd-pbo") is null, "5800X3D: sem PBO");
        Assert(Get(amd, "amd-cppc") != null && Get(amd, "intel-turbo") is null && Get(amd, "intel-power-raised") is null, "Ryzen: regras AMD, nada de Intel");
        Assert(Get(amd, "mem-profile") is { Compliance: Compliance.Attention, Evidence: Evidence.Inferred }, "Ryzen com pente 3600 a 2133: perfil desligado (inferido: a velocidade do pente vem do part number)");
        Assert(Get(amd, "mem-profile")!.Guide is { HasConfirmedPath: false } mg && mg.OptionNames.Contains("A-XMP"), "MSI sem manual: caminho não confirmado, só nomes das opções");
        var amd7 = Ryzen5800X3D() with { Cpu = Ryzen5800X3D().Cpu with { Name = "AMD Ryzen 7 7800X3D 8-Core Processor" }, Motherboard = new MotherboardInfo("ASUSTeK COMPUTER INC.", "TUF GAMING B650-PLUS", "") };
        Assert(Get(BiosAdvisorEngine.Analyze(amd7, AdvisorPreset.Performance), "amd-pbo") is { Risk: Level.Medium }, "7800X3D: PBO com risco médio");
        Assert(Get(BiosAdvisorEngine.Analyze(amd7, AdvisorPreset.Safe), "mem-profile")!.Guide is { HasConfirmedPath: false }, "ASUS fora da série 400: caminho não confirmado");

        // Notebook e Legacy
        var laptop = BiosAdvisorEngine.Analyze(TufB460() with { IsLaptop = true }, AdvisorPreset.Competitive);
        Assert(laptop.Warnings.Count > 0 && Get(laptop, "intel-power-raised") is null && Get(laptop, "intel-cstates") is null, "Notebook: sem ajustes de energia nem C-States");
        var legacy = BiosAdvisorEngine.Analyze(TufB460() with { Bios = TufB460().Bios with { Firmware = FirmwareMode.Legacy, SecureBootEnabled = false } }, AdvisorPreset.Safe);
        Assert(Get(legacy, "boot-uefi") is { Risk: Level.High, Compliance: Compliance.Attention, RequiresExtraConfirmation: true }, "Legacy: trocar para UEFI é risco alto com confirmação extra");
        var unknown = BiosAdvisorEngine.Analyze(TufB460() with { Motherboard = new MotherboardInfo("", "", ""), Cpu = TufB460().Cpu with { Name = "Genuine CPU", Vendor = CpuVendor.Unknown } }, AdvisorPreset.Safe);
        Assert(unknown.Recommendations.Count > 0 && unknown.Warnings.Count >= 2, "Hardware desconhecido não bloqueia o Advisor");

        // Revisão: híbridos Intel, Core Ultra sem HT, UEFI x CSM, máquina virtual
        var hybrid = TufB460() with { Cpu = new CpuInfo("12th Gen Intel(R) Core(TM) i7-12700K", CpuVendor.Intel, "x64", 12, 12, 20, 3600, false, true) };
        Assert(Get(BiosAdvisorEngine.Analyze(hybrid, AdvisorPreset.Safe), "cpu-smt") is { Compliance: Compliance.Ok, Evidence: Evidence.Inferred }, "i7-12700K (12C/20T): Hyper-Threading ligado nos P-cores, sem falso alarme");
        var ultra = TufB460() with { Cpu = new CpuInfo("Intel(R) Core(TM) Ultra 7 265K", CpuVendor.Intel, "x64", 20, 20, 20, 3900, false, true) };
        Assert(Get(BiosAdvisorEngine.Analyze(ultra, AdvisorPreset.Safe), "cpu-smt") is null, "Core Ultra 200 não tem Hyper-Threading: item não aparece");
        Assert(Get(safe, "boot-uefi") is { Evidence: Evidence.Inferred, Compliance: Compliance.Ok }, "UEFI + Secure Boot ativo: CSM desligado por dedução");
        var noSb = BiosAdvisorEngine.Analyze(TufB460() with { Bios = TufB460().Bios with { SecureBootEnabled = false } }, AdvisorPreset.Safe);
        Assert(Get(noSb, "boot-uefi") is { Evidence: Evidence.NeedsBiosCheck, Compliance: Compliance.Unknown }, "UEFI sem Secure Boot: CSM precisa ser visto na BIOS");
        var vm = BiosAdvisorEngine.Analyze(TufB460() with { IsVirtualMachine = true }, AdvisorPreset.Safe);
        Assert(vm.Score.Overall is null && vm.Warnings.Any(w => w.En.Contains("virtual machine")), "Máquina virtual: sem nota da BIOS física");
        foreach (var preset in Enum.GetValues<AdvisorPreset>())
            Assert(BiosAdvisorEngine.Analyze(TufB460(), preset).Recommendations.All(x => x.Name.Pt.Length > 0 && x.Name.En.Length > 0 && x.Description.En.Length > 0 && x.Rollback.En.Length > 0), $"Preset {preset}: todos os textos em PT e EN");
    }

    sealed class FakeWmi : IWmiSource
    {
        public Dictionary<string, List<Dictionary<string, object?>>> Rows { get; } = new();
        public IReadOnlyList<WmiRow> Query(string className, string[] properties, string scope = @"root\cimv2") =>
            Rows.TryGetValue(className, out var list) ? list.Select(r => new WmiRow(r)).ToList() : new List<WmiRow>();
    }

    static void DetectorTests()
    {
        var cpu = HardwareDetector.ParseCpu(new[] { new WmiRow(new Dictionary<string, object?> { ["Name"] = "Intel(R) Core(TM) i7-10700F CPU @ 2.90GHz", ["Manufacturer"] = "GenuineIntel", ["Architecture"] = (ushort)9, ["NumberOfCores"] = 8u, ["NumberOfEnabledCore"] = 8u, ["NumberOfLogicalProcessors"] = 16u, ["MaxClockSpeed"] = 2904u, ["VirtualizationFirmwareEnabled"] = false }) }, true);
        Assert(cpu is { Vendor: CpuVendor.Intel, Architecture: "x64", Cores: 8, Threads: 16, BaseClockMhz: 2900, HypervisorPresent: true }, "CPU pelo WMI (clock base pelo nome comercial)");
        var mem = HardwareDetector.ParseMemory(new[]
        {
            new WmiRow(new Dictionary<string, object?> { ["DeviceLocator"] = "ChannelA-DIMM2", ["Capacity"] = 8589934592UL, ["Speed"] = 2933u, ["ConfiguredClockSpeed"] = 2933u, ["SMBIOSMemoryType"] = 26u, ["Attributes"] = 1u, ["PartNumber"] = "DDR4 3000" }),
            new WmiRow(new Dictionary<string, object?> { ["DeviceLocator"] = "ChannelB-DIMM2", ["Capacity"] = 8589934592UL, ["Speed"] = 2933u, ["ConfiguredClockSpeed"] = null, ["SMBIOSMemoryType"] = 26u, ["Attributes"] = 0u }),
        }, new[] { new WmiRow(new Dictionary<string, object?> { ["MemoryDevices"] = 4, ["Use"] = 3 }), new WmiRow(new Dictionary<string, object?> { ["MemoryDevices"] = 1, ["Use"] = 7 }) });
        Assert(mem.Modules.Count == 2 && mem.TotalBytes == 16L << 30 && mem.Type == MemoryType.Ddr4 && mem.TotalSlots == 4, "Memória pelo WMI (16 GB DDR4, 4 slots)");
        Assert(mem.Modules[0].Rank == 1 && mem.Modules[1].Rank is null && mem.Modules[1].ConfiguredMhz == 0 && mem.ConfiguredMhz == 2933, "Rank 0 = desconhecido; velocidade configurada ausente fica desconhecida (nunca vira a nominal)");
        var gpus = HardwareDetector.ParseGpus(new[]
        {
            new WmiRow(new Dictionary<string, object?> { ["Name"] = "NVIDIA GeForce RTX 2060", ["AdapterCompatibility"] = "NVIDIA", ["AdapterRAM"] = 4293918720u, ["PNPDeviceID"] = "PCI\\VEN_10DE&DEV_1F08" }),
            new WmiRow(new Dictionary<string, object?> { ["Name"] = "Microsoft Remote Display Adapter", ["PNPDeviceID"] = "SWD\\REMOTEDISPLAYENUM" }),
        }, new Dictionary<string, long> { ["NVIDIA GeForce RTX 2060"] = 6L << 30 }, new Dictionary<string, GpuLinkReading> { ["NVIDIA GeForce RTX 2060"] = new(3, 3, 16, 16, 256) });
        Assert(gpus.Count == 1 && gpus[0].VramBytes == 6L << 30 && gpus[0].Link?.Bar1TotalMb == 256, "GPU: VRAM real do registro, adaptador virtual ignorado");
        Assert(HardwareDetector.ParseCimDate("20200722000000.000000+000") == new DateTime(2020, 7, 22), "Data CIM da BIOS");
        Assert(HardwareDetector.IsLaptop(new ushort[] { 10 }, null) && !HardwareDetector.IsLaptop(new ushort[] { 3 }, 1), "Notebook pelo chassi");
        Assert(HardwareDetector.IsVirtualMachine("Microsoft Corporation", "Virtual Machine") && !HardwareDetector.IsVirtualMachine("ASUS", "System Product Name"), "Máquina virtual detectada");
        var wmi = new FakeWmi();
        wmi.Rows["Win32_BaseBoard"] = new() { new() { ["Manufacturer"] = "ASUSTeK COMPUTER INC.", ["Product"] = "TUF GAMING B460M-PLUS" } };
        Assert(HardwareDetector.ParseBoard(wmi.Query("Win32_BaseBoard", Array.Empty<string>())).Product == "TUF GAMING B460M-PLUS", "Placa pelo WMI simulado");
        Assert(HardwareDetector.ParseBoard(new FakeWmi().Query("Win32_BaseBoard", Array.Empty<string>())).Product == "", "WMI vazio não quebra a detecção");
        Assert(NvidiaSmi.ParseBar1("FB Memory Usage\n    Total : 6144 MiB\nBAR1 Memory Usage\n    Total                             : 256 MiB\n    Used : 2 MiB").SequenceEqual(new long[] { 256 }), "BAR1 lido da saída do nvidia-smi");
        Assert(NvidiaSmi.ParseCsv("NVIDIA GeForce RTX 2060, 3, 16, 16\n", 4).Single()[2] == "16" && NvidiaSmi.Int("[N/A]") is null, "CSV do nvidia-smi; [N/A] vira null");
        Assert(SensorReader.ThrottleReasons("[N/A]") is null && SensorReader.ThrottleReasons("0x10000")!.Single().En.StartsWith("Other"), "Máscara ilegível = desconhecido; bit novo = outro motivo");
        Assert(SensorReader.ThrottleReasons("0x0000000000000004")!.Single().En.Contains("Power") && SensorReader.ThrottleReasons("0x1")!.Count == 0, "Motivos de redução de clock da NVIDIA (ocioso não conta)");
    }

    static void BenchmarkTests()
    {
        var frames = Enumerable.Repeat(4.0, 990).Concat(Enumerable.Repeat(10.0, 10)).ToList();
        var m = FrametimeAnalyzer.Compute(frames);
        Assert(m.Frames == 1000 && Math.Abs(m.AverageFps - 1000 / 4.06) < 0.5 && m.Low01Fps is not null, "Frametime: FPS médio e 0.1% low com 1000 quadros");
        Assert(Math.Abs(FrametimeAnalyzer.Percentile(new double[] { 1, 2, 3, 4 }, 0.5) - 2.5) < 1e-9, "Percentil com interpolação linear");
        var csv = "Application,ProcessID,MsBetweenPresents\n" + string.Join("\n", Enumerable.Range(0, 300).Select(i => $"game.exe,1,{(i % 50 == 0 ? 20 : 5).ToString(System.Globalization.CultureInfo.InvariantCulture)}")) + "\n" + string.Join("\n", Enumerable.Range(0, 120).Select(_ => "dwm.exe,2,16.6")) + "\nbad,line\n";
        var parsed = FrametimeAnalyzer.ParseCsv(csv);
        Assert(parsed.Application == "game.exe" && parsed.Frames == 300 && parsed.Low01Fps is null, "CSV do PresentMon: escolhe o jogo com mais quadros; 0.1% low só com 1000+ quadros");
        var stalls = "Application,ProcessID,SwapChainAddress,MsBetweenPresents\n" + string.Join("\n", Enumerable.Range(0, 200).Select(i => i == 100 ? "game.exe,1,0xA,1500" : "game.exe,1,0xA,5"))
            + "\n" + string.Join("\n", Enumerable.Range(0, 150).Select(_ => "game.exe,1,0xB,8"));
        var withStall = FrametimeAnalyzer.ParseCsv(stalls);
        Assert(withStall.Frames == 200 && withStall.DurationSeconds > 2, "Travadas longas entram na conta; swapchains diferentes não se misturam");
        var threw = false;
        try { FrametimeAnalyzer.ParseCsv("a,b\n1,2"); } catch (FormatException) { threw = true; }
        Assert(threw, "CSV sem coluna de frametime é recusado");
        var before = new BenchmarkRun { Slot = BenchmarkSlot.Baseline, AverageFps = 365, Low1Fps = 287, CpuTempMax = 91 };
        var after = new BenchmarkRun { Slot = BenchmarkSlot.After, AverageFps = 389, Low1Fps = 321, CpuTempMax = 88, GpuTempMax = 70 };
        var d = BenchmarkComparer.Compare(before, after);
        Assert(d.Count == 3 && Math.Abs(d[0].Change - 6.575) < 0.01 && Math.Abs(d[1].Change - 11.85) < 0.01 && d[2].Change == -3, "Comparação: +6.6% FPS, +11.8% 1% low, -3 °C; GPU só num teste fica de fora");
        var withSensors = new BenchmarkRun().WithSensors(new[] { Snap(50, null), Snap(70, 60) });
        Assert(withSensors.CpuTempMax is null && withSensors.GpuTempMax == 60 && withSensors.CpuUsageAvg == 60, "Sensores ausentes ficam null (sem valores inventados)");

        static SensorSnapshot Snap(double cpu, double? gpuTemp) => new(cpu, null, null, null, null, null, null, gpuTemp, null, null, Array.Empty<LocalizedText>(), DateTime.Now);
    }

    static string RepoFile(string relative)
    {
        // Sobe a partir da pasta do teste até a raiz do repositório (onde fica bios-db/)
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, relative))) return Path.Combine(dir.FullName, relative);
        throw new FileNotFoundException(relative);
    }

    static void DatabaseTests()
    {
        var db = BiosDatabase.LoadEmbedded();
        Assert(db.Version >= 1 && db.BoardFor(new MotherboardInfo("ASUSTeK", "TUF GAMING B460M-PLUS", "")) is { Name: "ASUS TUF GAMING B460M-PLUS" } && db.PathSetsFor("asus").Any(), "Banco embutido: perfil da TUF B460M-PLUS e caminhos ASUS");
        Assert(db.BiosQuestions.ContainsKey(Settings.MemoryProfile) && db.OptionNamesFor("msi", Settings.MemoryProfile)!.Contains("A-XMP"), "Banco embutido: nomes de perguntas do SCEWIN e opções por fabricante");

        // Assinatura do arquivo publicado (o mesmo que o app baixa do GitHub)
        var json = File.ReadAllBytes(RepoFile("bios-db/bios-db.json"));
        var sig = File.ReadAllText(RepoFile("bios-db/bios-db.json.sig"));
        Assert(BiosDatabase.VerifySignature(json, sig), "bios-db.json.sig confere com a chave pública do app (rode tools/Sign-BiosDatabase.ps1 após editar o banco)");
        var lf = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(json).Replace("\r\n", "\n"));
        var crlf = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(lf).Replace("\n", "\r\n"));
        Assert(BiosDatabase.VerifySignature(lf, sig) && BiosDatabase.VerifySignature(crlf, sig), "Assinatura vale com LF (GitHub) e CRLF (Windows)");
        var bom = System.Text.Encoding.UTF8.GetPreamble().Concat(lf).ToArray();
        Assert(BiosDatabase.VerifySignature(bom, sig), "Assinatura aceita UTF-8 com BOM");
        foreach (var version in new[] { 0, -1 })
        {
            var rejected = false;
            try { BiosDatabase.Parse("{\"format\":1,\"version\":" + version + "}", "teste"); }
            catch (FormatException) { rejected = true; }
            Assert(rejected, "Banco com versão " + version + " é recusado");
        }
        var tampered = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(json).Replace("Ai Overclock Tuner", "Ai Overclock Tunes"));
        Assert(!BiosDatabase.VerifySignature(tampered, sig) && !BiosDatabase.VerifySignature(json, "AAAA"), "Banco alterado ou assinatura inválida é recusado");
        Assert(BiosDatabase.Parse(System.Text.Encoding.UTF8.GetString(json), "teste").Version == db.Version, "Arquivo do repositório e banco embutido são o mesmo");

        var threw = false;
        try { BiosDatabase.Parse("{\"format\":2,\"version\":9}", "x"); } catch (FormatException) { threw = true; }
        Assert(threw, "Formato de banco desconhecido é recusado");

        // Banco "remoto" com uma placa nova: entra sem mudar código (e expressões inválidas são ignoradas)
        var remote = BiosDatabase.Parse("""
            { "format": 1, "version": 99, "updated": "2026-12-01",
              "pathSets": [ { "id": "msi-b550", "vendor": "msi", "source": "MSI B550 BIOS Manual", "chipsets": ["B550"], "productPattern": "MAG B550",
                              "paths": { "mem.profile": ["OC", "A-XMP"] }, "notes": {} } ],
              "optionNames": {},
              "boards": [ { "name": "MSI MAG B550 TOMAHAWK", "vendor": "msi", "productPattern": "^MAG B550 TOMAHAWK", "chipset": "B550", "source": "MSI", "mappedCpus": ["5800X3D"], "facts": [] },
                          { "name": "Quebrada", "vendor": "x", "productPattern": "([", "chipset": "", "source": "" } ],
              "biosQuestions": { "mem.profile": ["^A-XMP$", "(["] } }
            """, "teste");
        Assert(remote.Boards.Count == 1 && remote.BiosQuestions[Settings.MemoryProfile].Length == 1, "Padrão inválido no banco é ignorado sem quebrar");
        var amd = BiosAdvisorEngine.Analyze(Ryzen5800X3D(), AdvisorPreset.Safe, db: remote);
        Assert(amd.SpecificProfileAvailable && amd.SpecificProfileName!.Contains("5800X3D"), "Placa adicionada só pelo banco vira perfil específico");
        Assert(Get(amd, "mem-profile")!.Guide is { HasConfirmedPath: true } mg && mg.Steps.SequenceEqual(new[] { "Advanced Mode (F7)", "OC", "A-XMP" }) && mg.Source == "MSI B550 BIOS Manual", "Caminho MSI vindo do banco, com a fonte");
    }

    static void BiosReadTests()
    {
        // Trecho no formato do export do SCEWIN (nvram.txt)
        var nvram = """
            HIICrc32= 1234
            Setup Question	= Intel(R) Speed Shift Technology
            Help String	= Enable/Disable Speed Shift
            Token	=1A	// Do NOT change this line
            Offset	=10
            Width	=01
            BIOS Default	=[01]Enabled
            Options	=*[01]Enabled	// Move "*" to the desired Option
                     [00]Disabled

            Setup Question	= Ai Overclock Tuner
            Help String	= Overclock
            Token	=2B	// Do NOT change this line
            Options	=*[00]Auto	// Move "*" to the desired Option
                     [01]Manual
                     [02]XMP I
                     [03]XMP II

            Setup Question	= Launch CSM
            Token	=3C	// Do NOT change this line
            Options	=[01]Enabled	// Move "*" to the desired Option
                     *[00]Disabled

            Setup Question	= Hyper-Threading
            Token	=4D	// Do NOT change this line
            Options	=*[01]Enabled	// Move "*" to the desired Option
                     [00]Disabled

            Setup Question	= Turbo Mode
            Token	=5E	// Do NOT change this line
            Options	=*[01]Enabled	// Move "*" to the desired Option
                     [00]Disabled
            """.Replace("\r\n", "\n");
        var settings = PQueirozOptimizer.Services.BiosService.Parse(nvram, out _);
        var profile = TufB460() with { Observed = null };
        var readings = BiosSettingMapper.Map(settings, BiosDatabase.LoadEmbedded(), profile.Fingerprint, DateTime.Now);
        Assert(readings.Values[Settings.IntelSpeedShift].Value == "Enabled" && readings.Values[Settings.MemoryProfile] is { Question: "Ai Overclock Tuner", Value: "Auto" } && readings.Values[Settings.Csm].Value == "Disabled", "SCEWIN: perguntas reconhecidas pelos nomes do banco");
        Assert(readings.Values[Settings.MemoryProfile].Options.Contains("XMP II") && readings.TotalQuestions == 5, "SCEWIN: opções e total de perguntas guardados");

        var statePath = Path.Combine(Path.GetTempPath(), "advisor-readings-" + Guid.NewGuid().ToString("N"), "state.json");
        try
        {
            var store = new BiosAdvisorStore(statePath);
            store.Save(new BiosAdvisorState { Readings = readings });
            var restored = store.Load().Readings;
            Assert(restored != null && restored.Fingerprint == readings.Fingerprint && restored.ReadAt == readings.ReadAt
                && restored.Values[Settings.MemoryProfile].Options.SequenceEqual(readings.Values[Settings.MemoryProfile].Options)
                && restored.Values[Settings.IntelSpeedShift].Value == "Enabled", "Leituras da BIOS persistem entre aberturas");
        }
        finally { if (File.Exists(statePath)) File.Delete(statePath); Directory.Delete(Path.GetDirectoryName(statePath)!); }
        var r = BiosAdvisorEngine.Analyze(profile, AdvisorPreset.Safe, readings: readings);
        Assert(Get(r, "intel-speedshift") is { Evidence: Evidence.ReadFromBios, Compliance: Compliance.Ok } s && s.CurrentValue!.Pt.Contains("lido da BIOS"), "Speed Shift lido da BIOS: Enabled = OK");
        Assert(Get(r, "intel-turbo") is { Evidence: Evidence.ReadFromBios, Compliance: Compliance.Ok }, "Turbo lido da BIOS quando o Windows não observou");
        Assert(Get(r, "mem-profile") is { Evidence: Evidence.ReadFromBios, Compliance: Compliance.Attention }, "Ai Overclock Tuner em Auto: XMP desligado na BIOS (vale mais que a dedução)");
        Assert(Get(r, "boot-uefi") is { Evidence: Evidence.ReadFromBios, Compliance: Compliance.Ok }, "Launch CSM Disabled lido da BIOS");
        Assert(Get(r, "cpu-smt") is { Evidence: Evidence.Detected, BiosValue.Value: "Enabled" }, "Leitura do Windows não é trocada; valor da BIOS só complementa");
        Assert(Get(r, "intel-speedshift")!.Guide is { } g && g.HasConfirmedPath, "Guia continua com o caminho do manual");
        var unread = BiosAdvisorEngine.Analyze(profile, AdvisorPreset.Safe);
        Assert(r.Score.Verifiable > unread.Score.Verifiable, "Leitura da BIOS aumenta o número de itens com evidência");
        var other = BiosAdvisorEngine.Analyze(profile with { Bios = profile.Bios with { Version = "2003" } }, AdvisorPreset.Safe, readings: readings);
        Assert(Get(other, "intel-speedshift")!.Evidence == Evidence.NeedsBiosCheck, "Leitura de outra versão de BIOS é ignorada");
        var changed = PQueirozOptimizer.Services.BiosService.Parse(nvram, out _);
        changed.First(x => x.Question == "Intel(R) Speed Shift Technology").SelectedIndex = 1; // Disabled
        var disabled = BiosSettingMapper.Map(changed, BiosDatabase.LoadEmbedded(), profile.Fingerprint, DateTime.Now);
        Assert(Get(BiosAdvisorEngine.Analyze(profile, AdvisorPreset.Safe, readings: disabled), "intel-speedshift")!.Compliance == Compliance.Attention, "Speed Shift Disabled na BIOS = ajustar");
    }

    static void UpdateTests()
    {
        Assert(BiosUpdateCheck.Compare("0708", "2003") < 0 && BiosUpdateCheck.Compare("2003", "2003") == 0 && BiosUpdateCheck.Compare("F12", "F15") < 0, "Comparação de versões de BIOS");
        Assert(BiosUpdateCheck.Compare("A.H0", "1.40") is null, "Formatos diferentes não são comparados");
        Assert(BiosUpdateCheck.Compare("2003", "2003-beta") is null && BiosUpdateCheck.Compare("1.9", "1.10") is null, "Versões com sufixo ou pontos não são comparadas");
        Assert(AsusBiosUpdateProvider.ParseLatest("{\"Result\":{\"Obj\":[{\"Name\":\"BIOS\",\"Files\":[{\"Version\":\"2101\"}]}]}}") is null, "ASUS: versão sem IsRelease não é considerada liberada");
        var json = "{\"Result\":{\"Count\":2,\"Obj\":[{\"Name\":\"BIOS\",\"Files\":[{\"Version\":\"2003\",\"ReleaseDate\":\"2026/03/16\",\"IsRelease\":\"1\"},{\"Version\":\"2101\",\"IsRelease\":\"0\"},{\"Version\":\"1901\",\"ReleaseDate\":\"2025/02/17\",\"IsRelease\":\"1\"}]},{\"Name\":\"Drivers\",\"Files\":[{\"Version\":\"9999\",\"IsRelease\":\"1\"}]}]}}";
        Assert(AsusBiosUpdateProvider.ParseLatest(json) is { Version: "2003" }, "ASUS: maior versão liberada da categoria BIOS (ignora beta e outras categorias)");
        Assert(AsusBiosUpdateProvider.ParseLatest("{\"Result\":null,\"Status\":\"FAIL\"}") is null, "ASUS: resposta de falha = não verificado");
        Assert(new AsusVendor().SupportUrl(new MotherboardInfo("ASUSTeK", "TUF GAMING B460M-PLUS", "")) == "https://www.asus.com/supportonly/tuf%20gaming%20b460m-plus/helpdesk_bios/", "Link oficial da ASUS");
    }
}
