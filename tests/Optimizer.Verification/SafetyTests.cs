using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using PQueirozOptimizer;
using PQueirozOptimizer.Services;

/// <summary>Etapa A: caminhos de ferramentas, licença, Defender, diretórios protegidos, BIOS, risco e operações interrompidas.</summary>
internal static class SafetyTests
{
    static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }

    static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; } catch (T) { return true; }
    }

    public static void Run(string root)
    {
        Directory.CreateDirectory(root);
        Tools();
        Clock(root);
        Defender();
        Journal(root);
        Risk();
        Uninstall();
        Bios();
        ProtectedDirectory(root);
        Translations();
        Updates();
    }

    static void Updates()
    {
        static UpdateInfo Release(string version, bool withChecksum = true) => new(true, "1.10.1", version,
            $"https://github.com/PQueirozDev/Optimizer/releases/tag/v{version}", $"https://github.com/PQueirozDev/Optimizer/releases/download/v{version}/Qrztweaks-Setup-v{version}.exe",
            $"Qrztweaks-Setup-v{version}.exe", withChecksum ? $"https://github.com/PQueirozDev/Optimizer/releases/download/v{version}/SHA256SUMS.txt" : null);
        var seenAtStartup = Release("1.10.2");
        Check(UpdateService.PickNewest(seenAtStartup, Release("1.10.4")).LatestVersion == "1.10.4", "Duas versões novas: instala direto a mais recente, não uma por uma");
        Check(UpdateService.PickNewest(seenAtStartup, Release("1.10.2")).LatestVersion == "1.10.2", "Mesma versão: continua a já encontrada");
        Check(UpdateService.PickNewest(seenAtStartup, null).LatestVersion == "1.10.2", "Sem conseguir verificar de novo: usa a já encontrada");
        Check(UpdateService.PickNewest(seenAtStartup, Release("1.10.4", withChecksum: false)).LatestVersion == "1.10.2", "Versão nova ainda sem hash publicado não é escolhida");
        Check(UpdateService.PickNewest(Release("1.10.10"), Release("1.10.9")).LatestVersion == "1.10.10", "Versões comparadas como números (1.10.10 > 1.10.9)");
    }

    static void Tools()
    {
        Check(SystemTools.Resolve("sc.exe") == Path.Combine(Environment.SystemDirectory, "sc.exe"), "Ferramenta do Windows roda pelo caminho de System32");
        Check(SystemTools.Resolve(@"C:\Ferramentas\a.exe") == @"C:\Ferramentas\a.exe", "Caminho completo não é alterado");
        Check(SystemTools.Resolve("ferramenta-que-nao-existe.exe") == "ferramenta-que-nao-existe.exe", "Ferramenta fora de System32 continua pelo nome");
        Check(File.Exists(SystemTools.PowerShell), "PowerShell resolvido pelo caminho do Windows");
    }

    static void Clock(string root)
    {
        var path = Path.Combine(root, "clock-novo.dat");
        File.Delete(path);
        var clock = new LicenseClock(path);
        var now = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        Check(!clock.IsRolledBack(now) && clock.EffectiveNowUtc(now) == now, "Primeira ativação sem relógio salvo não quebra a validação");
    }

    static void Defender()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        Check(DefenderService.ExclusionProblem(Path.GetPathRoot(windows)!) is not null, "Exclusão de unidade inteira é recusada");
        Check(DefenderService.ExclusionProblem(Path.Combine(windows, "System32")) is not null, "Exclusão dentro do Windows é recusada");
        Check(DefenderService.ExclusionProblem(Path.Combine(profile, "Downloads", "jogo")) is not null, "Exclusão dentro de Downloads é recusada");
        Check(DefenderService.ExclusionProblem(Path.GetTempPath()) is not null, "Exclusão da pasta Temp é recusada");
        Check(DefenderService.ExclusionProblem(profile) is not null && DefenderService.ExclusionProblem(programFiles + "\\") is not null, "Pasta do usuário e Program Files inteiros são recusados");
        Check(DefenderService.ExclusionProblem(@"\\servidor\jogos") is not null, "Pasta de rede é recusada");
        Check(DefenderService.ExclusionProblem(Path.Combine(programFiles, "Riot Games", "VALORANT")) is null && DefenderService.ExclusionProblem(@"D:\Jogos\CS2") is null, "Pasta específica do jogo é aceita");
    }

    static void Journal(string root)
    {
        var dir = Path.Combine(root, "journal");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        var journal = new OperationJournal(dir);
        Check(journal.FindInterrupted() is null, "Sem registro, nenhuma operação interrompida");
        journal.Begin("padrao", new[] { "Limpando cache DNS" });
        Check(journal.FindInterrupted() is null, "Operação do próprio processo em andamento não é tratada como interrompida");
        // Queda simulada: o app fecha sem chamar End e outra instância abre depois
        var reopened = new OperationJournal(dir);
        var entry = reopened.FindInterrupted((_, _) => false);
        Check(entry is { Operation: "padrao" } && entry.Steps!.SequenceEqual(new[] { "Limpando cache DNS" }), "Queda no meio da operação é detectada na próxima abertura");
        reopened.Acknowledge();
        Check(reopened.FindInterrupted((_, _) => false) is null, "Aviso não se repete depois de mostrado");
        journal.Begin("debloat", null);
        journal.End();
        Check(journal.FindInterrupted((_, _) => false) is null, "Operação que terminou não deixa registro");
        File.WriteAllText(Path.Combine(dir, "em-andamento.json"), "{ quebrado");
        Check(journal.FindInterrupted((_, _) => false) is { Operation: "" }, "Registro corrompido ainda avisa, sem nome de operação");
        journal.End();
        Check(OperationJournal.Tracked.Contains("gamer") && !OperationJournal.Tracked.Contains("benchmark") && !OperationJournal.Tracked.Contains("analisar"), "Só operações que mudam o sistema entram no registro");
    }

    static void Risk()
    {
        // Toda etapa das otimizações tem risco declarado no script: nada fica "Seguro" por omissão
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Otimizador_de_PC.ps1"));
        foreach (var function in new[] { "Otimizar-Padrao", "Otimizar-Gamer", "Otimizar-Inteligente", "Otimizar-Debloat" })
        {
            var start = script.IndexOf("function " + function + " {", StringComparison.Ordinal);
            var end = script.IndexOf("\nfunction ", start + 1, StringComparison.Ordinal);
            var body = script[start..(end < 0 ? script.Length : end)];
            var pattern = function == "Otimizar-Debloat" ? @"Executar-Se-Confirmado\s+""[^""]*""\s*`?\s*""([^""]+)""" : @"@\{ Nome = ""([^""]+)""";
            var matches = Regex.Matches(body, pattern);
            var missing = new List<string>();
            for (var i = 0; i < matches.Count; i++)
            {
                var code = body[matches[i].Index..(i + 1 < matches.Count ? matches[i + 1].Index : body.Length)];
                if (PowerShellBridge.Marker(code, "risco") is not ("seguro" or "moderado" or "alto")) missing.Add(matches[i].Groups[1].Value);
            }
            Check(matches.Count > 0 && missing.Count == 0, $"{function}: todas as {matches.Count} etapas declaram o risco" + (missing.Count > 0 ? " (faltam: " + string.Join(", ", missing) + ")" : ""));
        }
        Check(PowerShellBridge.ClassifyRisk("{ Remove-Item x }") == StepRisk.Moderate && PowerShellBridge.ClassifyRisk("# risco: seguro") == StepRisk.Safe, "Etapa sem marcador conta como Moderada");

        var safe = new OperationStep("Limpando cache DNS", StepEffect.OneOff, StepRisk.Safe);
        var risky = new OperationStep("Desligar algo", StepEffect.Backup, StepRisk.High);
        var permanent = new OperationStep("Remover apps", StepEffect.Irreversible, StepRisk.Moderate);
        Check(MainWindow.ConfirmationSummary(new[] { safe }) is null, "Ajustes seguros e reversíveis não pedem confirmação extra");
        Check(MainWindow.ConfirmationSummary(new[] { safe, risky }) is { } r && r.Contains("Desligar algo") && !r.Contains("Limpando"), "Ajuste arriscado pede confirmação própria");
        Check(MainWindow.ConfirmationSummary(new[] { permanent }) is { } p && p.Contains("Remover apps") && p.Contains("Não reversíveis"), "Ajuste não reversível pede confirmação própria");
    }

    static void Uninstall()
    {
        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        Check(AppOptimizerService.SplitUninstallCommand($"\"{cmd}\" /c echo oi & calc.exe") is { } q && q.Exe == cmd && q.Arguments == "/c echo oi & calc.exe",
            "Desinstalador entre aspas: executável separado dos argumentos, sem shell intermediário");
        Check(AppOptimizerService.SplitUninstallCommand("MsiExec.exe /X{11111111-2222-3333-4444-555555555555}") is { } m &&
            m.Exe.Equals(Path.Combine(Environment.SystemDirectory, "msiexec.exe"), StringComparison.OrdinalIgnoreCase) && m.Arguments.StartsWith("/X{"), "MsiExec resolvido em System32");
        Check(AppOptimizerService.SplitUninstallCommand(@"C:\Program Files\NaoExiste\unins000.exe /SILENT") is null && AppOptimizerService.SplitUninstallCommand("") is null,
            "Desinstalador inexistente é recusado");
    }

    static void Bios()
    {
        var nvram = "HIICrc32= 1234\r\nSetup Question\t= Core Ratio\r\nToken\t=10\t// Do NOT change this line\r\nOffset\t=20\r\nWidth\t=01\r\nBIOS Default\t=<0>\r\nValue\t=<5>\r\n";
        var setting = BiosService.Parse(nvram, out var header).Single();
        Check(BiosService.FirmwareFingerprint(header) == "1234" && BiosService.FirmwareFingerprint(new[] { "Script File Name : x" }) is null, "Identificação da BIOS lida do cabeçalho do export");
        setting.NumericValue = "200";
        Check(BiosService.NumericProblem(setting) is null, "Valor numérico dentro do campo é aceito");
        setting.NumericValue = "300";
        Check(BiosService.NumericProblem(setting) is not null, "Valor maior que o campo (Width) é recusado");
        setting.NumericValue = "1.5";
        Check(BiosService.NumericProblem(setting) is not null, "Valor numérico malformado é recusado");
    }

    static void ProtectedDirectory(string root)
    {
        var target = Path.Combine(root, "alvo");
        var link = Path.Combine(root, "link-protegido");
        Directory.CreateDirectory(target);
        if (Directory.Exists(link)) Directory.Delete(link);
        using (var p = Process.Start(new ProcessStartInfo(SystemTools.Resolve("cmd.exe"), $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false })!) p.WaitForExit();
        if (!Directory.Exists(link)) { Console.WriteLine("SKIP Junction não pôde ser criado neste PC"); return; }
        try { Check(Throws<InvalidOperationException>(() => RegistryTweakStore.EnsureProtectedDirectory(link)), "Pasta de dados que é link (junction) é recusada"); }
        finally { Directory.Delete(link); }
    }

    static void Translations()
    {
        Translator.IsEnglish = true;
        try
        {
            var summary = MainWindow.ConfirmationSummary(new[] { new OperationStep("Limpando cache DNS", StepEffect.Irreversible, StepRisk.High) })!;
            var english = Translator.Tr(summary);
            Check(english.StartsWith("Risky") && english.Contains("Not reversible") && english.EndsWith("Apply anyway?"), "Confirmação de ajustes traduzida");
            Check(Translator.Tr(DefenderService.ExclusionProblem(@"\\servidor\x")) == "Network folders can't be excluded.", "Aviso do Defender traduzido");
            Check(Translator.Tr("A operação \"Versão Padrão\", iniciada em 10/10/2026 12:00, não terminou: o Qrztweaks fechou durante a execução.\n\nO que foi aplicado até ali tem backup. Abrir Atividade e reversão para conferir ou reverter?")
                is var interrupted && interrupted.StartsWith("The operation \"Standard Mode\"") && interrupted.Contains("did not finish"), "Aviso de operação interrompida traduzido");
        }
        finally { Translator.IsEnglish = false; }
    }
}
