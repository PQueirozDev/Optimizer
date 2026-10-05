using static PQueirozOptimizer.BiosAdvisor.R;

namespace PQueirozOptimizer.BiosAdvisor.Rules;

/// <summary>
/// Modo de boot, Secure Boot, TPM, virtualização e Spread Spectrum. Nenhum preset recomenda desligar proteções:
/// segurança e virtualização só aparecem para conferir e explicar o efeito.
/// </summary>
public sealed class PlatformRules : IAdvisorRule
{
    public IEnumerable<AdvisorRecommendation> Evaluate(AdvisorContext c)
    {
        var bios = c.Profile.Bios;

        // ---------- UEFI / CSM ----------
        if (bios.Firmware != FirmwareMode.Unknown)
        {
            var uefi = bios.Firmware == FirmwareMode.Uefi;
            yield return new AdvisorRecommendation
            {
                Id = "boot-uefi", BiosTarget = @"^Disabled$", SettingId = Settings.Csm, Category = AdvisorCategory.Boot, Weight = 0,
                Name = T("Modo de boot UEFI (CSM desligado)", "UEFI boot mode (CSM off)"),
                Description = uefi
                    ? T("O Windows iniciou em UEFI. Com o CSM desligado, Secure Boot e Resizable BAR ficam disponíveis.",
                        "Windows booted in UEFI mode. With CSM off, Secure Boot and Resizable BAR become available.")
                    : T("O Windows iniciou em modo Legacy (CSM). ATENÇÃO: desligar o CSM com o disco em MBR impede o Windows de iniciar. Antes, converta o disco para GPT (mbr2gpt, com backup) e, se usar BitLocker, tenha a chave de recuperação em mãos.",
                        "Windows booted in Legacy (CSM) mode. WARNING: turning CSM off with an MBR disk stops Windows from booting. First convert the disk to GPT (mbr2gpt, with a backup) and, if you use BitLocker, keep the recovery key at hand."),
                RecommendedValue = T("UEFI · Launch CSM: Disabled", "UEFI · Launch CSM: Disabled"),
                // Boot em UEFI não prova que o CSM está desligado; Secure Boot ativo sim (ele exige o CSM desligado)
                CurrentValue = !uefi ? T("Legacy / CSM (detectado)", "Legacy / CSM (detected)")
                    : bios.SecureBootEnabled == true ? T("UEFI · CSM desligado (o Secure Boot ativo exige)", "UEFI · CSM off (active Secure Boot requires it)")
                    : T("UEFI (detectado) · CSM não informado pelo Windows", "UEFI (detected) · CSM not reported by Windows"),
                Evidence = !uefi ? Evidence.Detected : bios.SecureBootEnabled == true ? Evidence.Inferred : Evidence.NeedsBiosCheck,
                Compliance = !uefi ? Compliance.Attention : bios.SecureBootEnabled == true ? Compliance.Ok : Compliance.Unknown,
                ExpectedBenefit = T("Requisito para Secure Boot, Resizable BAR e alguns anti-cheats.", "Required for Secure Boot, Resizable BAR and some anti-cheats."),
                Gain = Level.Low, Risk = uefi ? Level.Low : Level.High, Thermal = ThermalImpact.None,
                Rollback = T("Ligue o Launch CSM de novo e salve com F10. Se o disco já foi convertido para GPT, mantenha o CSM desligado.",
                    "Turn Launch CSM back on and save with F10. If the disk was already converted to GPT, keep CSM off."),
            };
        }

        // ---------- Secure Boot ----------
        yield return new AdvisorRecommendation
        {
            Id = "sec-secureboot", SettingId = Settings.SecureBoot, Category = AdvisorCategory.Security, Weight = 0,
            Name = T("Secure Boot", "Secure Boot"),
            Description = T("Impede que código não assinado rode antes do Windows. Exigido pelo Windows 11 e por anti-cheats como Vanguard (Valorant) e FACEIT. Não afeta o FPS.",
                "Prevents unsigned code from running before Windows. Required by Windows 11 and by anti-cheats like Vanguard (Valorant) and FACEIT. Doesn't affect FPS."),
            RecommendedValue = T("Enabled (Windows UEFI mode)", "Enabled (Windows UEFI mode)"),
            CurrentValue = bios.SecureBootEnabled switch { true => T("Ativado (detectado)", "On (detected)"), false => T("Desativado (detectado)", "Off (detected)"), _ => null },
            Evidence = bios.SecureBootEnabled is null ? Evidence.NeedsBiosCheck : Evidence.Detected,
            Compliance = bios.SecureBootEnabled switch { true => Compliance.Ok, false => Compliance.Attention, _ => Compliance.Unknown },
            ExpectedBenefit = T("Segurança e compatibilidade com jogos que exigem.", "Security and compatibility with games that require it."),
            Gain = Level.Low, Risk = bios.Firmware == FirmwareMode.Legacy ? Level.High : Level.Low, Thermal = ThermalImpact.None,
            Rollback = T("Mude o Secure Boot (OS Type) de volta para Other OS e salve com F10.", "Set Secure Boot (OS Type) back to Other OS and save with F10."),
        };

        // ---------- TPM ----------
        yield return new AdvisorRecommendation
        {
            Id = "sec-tpm", SettingId = Settings.Tpm, Category = AdvisorCategory.Security, Weight = 0,
            Name = c.IsAmd ? T("TPM (AMD fTPM)", "TPM (AMD fTPM)") : T("TPM (Intel PTT)", "TPM (Intel PTT)"),
            Description = T("TPM 2.0 do processador, exigido pelo Windows 11, BitLocker e alguns anti-cheats. Não afeta o FPS.",
                "The CPU's TPM 2.0, required by Windows 11, BitLocker and some anti-cheats. Doesn't affect FPS."),
            RecommendedValue = Enabled,
            CurrentValue = bios.TpmEnabled switch { true => T("Ativado (detectado)", "On (detected)"), false => T("Desativado (detectado)", "Off (detected)"), _ => null },
            Evidence = bios.TpmEnabled is null ? Evidence.NeedsBiosCheck : Evidence.Detected,
            Compliance = bios.TpmEnabled switch { true => Compliance.Ok, false => Compliance.Attention, _ => Compliance.Unknown },
            ExpectedBenefit = T("Compatibilidade com Windows 11 e criptografia de disco.", "Windows 11 compatibility and disk encryption."),
            Gain = Level.Low, Risk = Level.Low, Thermal = ThermalImpact.None,
            Rollback = T("Atenção: desligar o TPM com BitLocker ativo pede a chave de recuperação no próximo boot.", "Warning: turning TPM off with BitLocker on asks for the recovery key at next boot."),
        };

        // ---------- Virtualização ----------
        var cpu = c.Profile.Cpu;
        bool? vtOn = cpu.HypervisorPresent ? true : cpu.VirtualizationFirmwareEnabled;
        yield return new AdvisorRecommendation
        {
            Id = "virt-vtx", SettingId = Settings.Virtualization, Category = AdvisorCategory.Virtualization, Weight = 0,
            Name = c.IsAmd ? T("Virtualização (SVM)", "Virtualization (SVM)") : T("Virtualização (Intel VT-x)", "Virtualization (Intel VT-x)"),
            Description = T("Necessária para WSL 2, Hyper-V, Docker, Android no Windows, emuladores e para a Segurança Baseada em Virtualização. Desligar na BIOS não aumenta o FPS por si só; o que pode custar desempenho é a Integridade de Memória (VBS) do Windows, ajustada no próprio Windows.",
                "Needed for WSL 2, Hyper-V, Docker, Android on Windows, emulators and Virtualization-Based Security. Turning it off in BIOS won't raise FPS by itself; what can cost performance is Windows Memory Integrity (VBS), set in Windows itself."),
            RecommendedValue = T("Enabled se usa os recursos acima (padrão)", "Enabled if you use the features above (default)"),
            CurrentValue = vtOn switch
            {
                true when cpu.HypervisorPresent => T("Ativada (hipervisor do Windows em uso)", "On (Windows hypervisor in use)"),
                true => T("Ativada (detectado)", "On (detected)"),
                false => T("Desativada (detectado)", "Off (detected)"),
                _ => null,
            },
            Evidence = vtOn is null || c.Profile.IsVirtualMachine ? Evidence.NeedsBiosCheck : Evidence.Detected,
            Compliance = Compliance.Info,
            ExpectedBenefit = T("Compatibilidade com programas de virtualização.", "Compatibility with virtualization software."),
            Gain = Level.Low, Risk = Level.Low, Thermal = ThermalImpact.None,
            Rollback = UndoToAuto,
        };

        // ---------- Spread Spectrum (competitivo) ----------
        if (c.IsDesktop)
            yield return new AdvisorRecommendation
            {
                Id = "latency-spread", SettingId = Settings.SpreadSpectrum, Category = AdvisorCategory.Latency, MinPreset = AdvisorPreset.Competitive, Weight = 0,
                Name = T("BCLK Spread Spectrum", "BCLK Spread Spectrum"),
                Description = T("Faz o clock de referência oscilar de propósito para reduzir interferência eletromagnética. Desligado, o clock fica exato. Efeito pequeno; nunca altere a frequência do BCLK em si.",
                    "Makes the reference clock wobble on purpose to reduce electromagnetic interference. Off, the clock stays exact. Small effect; never change the BCLK frequency itself."),
                RecommendedValue = T("Disabled", "Disabled"),
                Evidence = Evidence.NeedsBiosCheck, Compliance = Compliance.Info,
                ExpectedBenefit = T("Clock de referência estável.", "Stable reference clock."),
                Gain = Level.Low, Risk = Level.Low, Thermal = ThermalImpact.None,
                Rollback = UndoToAuto,
            };
    }
}
