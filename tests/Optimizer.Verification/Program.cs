using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PQueirozOptimizer;
using PQueirozOptimizer.Models;
using PQueirozOptimizer.Services;

internal static class Program
{
    static void Assert(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); }

    // App com recursos e estilos, mas sem a inicialização real (licença, idioma salvo, janela principal)
    internal static App CreateTestApp()
    {
        var app = new App(); app.InitializeComponent();
        var key = typeof(Application).GetField("EVENT_STARTUP", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var events = (System.ComponentModel.EventHandlerList)typeof(Application).GetProperty("Events", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;
        events[key] = null;
        return app;
    }

    // Inicialização no estilo Autoruns: leitura real deste PC e liga/desliga de itens criados só para o teste
    static void TestStartupPage(ActivityLog log)
    {
        Assert(StartupService.ExecutablePath("rundll32.exe \"C:\\Program Files\\X\\x.dll\",Iniciar") == @"C:\Program Files\X\x.dll", "rundll32 aponta para a DLL que ele carrega");
        Assert(StartupService.ResolveImage("explorer.exe") is { } explorer && File.Exists(explorer), "Nome solto resolvido como o Windows faria");
        Assert(StartupService.ResolveImage("powershell.exe") is { } powershell && File.Exists(powershell), "Programa do PATH encontrado");
        Assert(string.Equals(AutorunsService.ServiceImage(@"%SystemRoot%\system32\svchost.exe -k netsvcs -p", @"%SystemRoot%\System32\wuaueng.dll"), Path.Combine(Environment.SystemDirectory, "wuaueng.dll"), StringComparison.OrdinalIgnoreCase), "Serviço do svchost representado pela DLL");
        Assert(AutorunsService.ServiceImage("\"C:\\Program Files\\X\\svc.exe\" -service", null) == @"C:\Program Files\X\svc.exe", "Serviço com argumentos");
        Assert(string.Equals(AutorunsService.ServiceImage(@"\SystemRoot\System32\x.exe", null), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\x.exe"), StringComparison.OrdinalIgnoreCase), "Caminho \\SystemRoot\\ resolvido");
        Assert(AutorunsService.IsWindowsSigner("Microsoft Windows") && AutorunsService.IsWindowsSigner("Microsoft Windows Publisher") && !AutorunsService.IsWindowsSigner("Microsoft Corporation") && !AutorunsService.IsWindowsSigner("Microsoft Windows Hardware Compatibility Publisher"), "Só o que é do Windows fica oculto (Edge, OneDrive e drivers de terceiros aparecem)");
        Assert(AutorunsService.TriggerLabel(9) == "Ao fazer logon" && AutorunsService.TriggerLabel(8) == "Ao iniciar o Windows", "Gatilhos das tarefas descritos");
        Assert(StartupService.IsDefaultWinlogon("Shell", "explorer.exe") && !StartupService.IsDefaultWinlogon("Shell", "explorer.exe, outro.exe"), "Shell do Winlogon alterado aparece");
        var scriptTask = new AutorunEntry { Category = AutorunCategory.Tasks, Name = "Atualizador", Location = "Agendador de Tarefas", Key = @"\Atualizador", ImagePath = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"), Signature = SignatureStatus.Verified, Signer = "Microsoft Windows" };
        Assert(!AutorunsService.IsWindowsEntry(scriptTask), "Tarefa de terceiros que roda o PowerShell não fica oculta");

        const string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string approvedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        const string testName = "PQO-Verificacao";
        var service = new AutorunsService(log);
        // cmd.exe existe em qualquer Windows (inclusive no servidor do CI) e, se o teste fosse interrompido, não abriria nada no logon
        using (var run = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(runKey)) run.SetValue(testName, "\"" + Path.Combine(Environment.SystemDirectory, "cmd.exe") + "\" /c exit");
        try
        {
            var entries = service.ScanAsync().GetAwaiter().GetResult();
            Assert(entries.Any(e => e.Category == AutorunCategory.Tasks) && entries.Any(e => e.Category == AutorunCategory.Services), "Tarefas agendadas e serviços lidos");
            Assert(entries.Any(e => e.Category == AutorunCategory.Services && e.Key == "EventLog" && e.ImagePath!.EndsWith("wevtsvc.dll", StringComparison.OrdinalIgnoreCase)), "Serviço do Log de Eventos encontrado pela DLL");
            var item = entries.Single(e => e.Category == AutorunCategory.Logon && e.Key == testName);
            Assert(item.Enabled && item.CanToggle && item.FileExists && item.Icon != null && item.Company.Contains("Microsoft"), "Item de logon lido com arquivo, ícone e fabricante");
            service.SetEnabledAsync(item, false).GetAwaiter().GetResult();
            using (var approved = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(approvedKey))
                Assert(!StartupService.IsEnabled(approved?.GetValue(testName) as byte[]), "Desativar grava no mesmo lugar que o Gerenciador de Tarefas");
            Assert(!service.ScanAsync().GetAwaiter().GetResult().Single(e => e.Key == testName).Enabled, "Item desativado aparece desmarcado na leitura seguinte");
            service.SetEnabledAsync(item, true).GetAwaiter().GetResult();
            using (var approved = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(approvedKey))
                Assert(StartupService.IsEnabled(approved?.GetValue(testName) as byte[]), "Reativar desfaz a desativação");
            var eventLog = entries.Where(e => e.Key == "EventLog").ToList();
            service.VerifySignaturesAsync(eventLog).GetAwaiter().GetResult();
            Assert(eventLog[0].Signature == SignatureStatus.Verified && eventLog[0].IsWindows, "Assinatura de catálogo do Windows verificada e item oculto como do Windows");
        }
        finally
        {
            using (var run = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(runKey, true)) run?.DeleteValue(testName, false);
            using (var approved = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(approvedKey, true)) approved?.DeleteValue(testName, false);
        }
        TestScheduledTaskToggle(service);
        TestServiceToggle(service);
    }

    // Modo Jogo, Rede e plano Qrz: partes que não dependem de administrador nem alteram o sistema
    static void TestGamingFeatures(string root)
    {
        Assert(GamingService.DetectX3d("AMD Ryzen 9 7950X3D 16-Core Processor") is { DualCcd: true, Model: "7950X3D" }, "Ryzen 7950X3D reconhecido com dois CCDs");
        Assert(GamingService.DetectX3d("AMD Ryzen 7 9800X3D 8-Core Processor") is { DualCcd: false }, "Ryzen 9800X3D reconhecido com um CCD");
        Assert(GamingService.DetectX3d("AMD Ryzen 7 7700X 8-Core Processor") is null && GamingService.DetectX3d("Intel(R) Core(TM) i7-10700F CPU @ 2.90GHz") is null, "Processadores sem 3D V-Cache não são X3D");
        Assert(GamingService.GameProfileId(@"C:\Jogos\Jogo.exe") == GamingService.GameProfileId(@"c:\jogos\JOGO.EXE"), "Perfil de jogo não depende de maiúsculas no caminho");

        var planFile = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "PQueirozOptimizer", "Assets", "Qrz.powerplan.txt");
        using (var reader = new StreamReader(planFile))
        {
            var settings = PowerPlanService.LoadSettings(reader);
            Assert(settings.Count == 182 && settings.Any(s => s.Setting == new Guid("0cc5b647-c1df-4637-891a-dec35c318583") && s.Ac == 100), "Plano Qrz traz as 182 configurações, com estacionamento de núcleos desligado na tomada");
        }

        // Ajuste reversível: grava, guarda o original e a reversão só restaura o que o ajuste declarou
        const string key = @"Software\PQueirozOptimizer-Verificacao";
        var store = new RegistryTweakStore(Path.Combine(root, "tweaks"));
        using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(key)) k.SetValue("Existente", 5, Microsoft.Win32.RegistryValueKind.DWord);
        try
        {
            store.Apply("teste", "Teste", new[]
            {
                new RegistryWrite(Microsoft.Win32.RegistryHive.CurrentUser, key, "Existente", Microsoft.Win32.RegistryValueKind.DWord, 9),
                new RegistryWrite(Microsoft.Win32.RegistryHive.CurrentUser, key, "Novo", Microsoft.Win32.RegistryValueKind.String, "x"),
            });
            store.Apply("teste", "Teste", new[] { new RegistryWrite(Microsoft.Win32.RegistryHive.CurrentUser, key, "Existente", Microsoft.Win32.RegistryValueKind.DWord, 11) });
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key)) Assert((int)k!.GetValue("Existente")! == 11 && (string)k.GetValue("Novo")! == "x" && store.IsApplied("teste"), "Ajuste aplicado e backup criado");
            store.Revert("teste", (_, _, name) => name == "Existente");
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key)) Assert((int)k!.GetValue("Existente")! == 5 && k.GetValue("Novo") is not null && !store.IsApplied("teste"), "Reverter restaura o valor original e ignora itens fora da lista permitida");
        }
        finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(key, false); }

        // BIOS (formato do SCEWIN): mover o "*" só altera o item escolhido e preserva o resto do bloco
        var nvram = "// AMISCE Utility. Ver 5.05\r\nHIICrc32= 8DE3D5A6\r\n\r\n" +
            "Setup Question\t= Above 4G Decoding\r\nHelp String\t= Enables 64bit decoding\r\nToken\t=17\t// Do NOT change this line\r\nOffset\t=4A\r\nWidth\t=01\r\nBIOS Default\t=[00]Disabled\r\n" +
            "Options\t=*[00]Disabled\t// Move \"*\" to the desired Option\r\n         [01]Enabled\r\n\r\n" +
            "Setup Question\t= CSM Support\r\nToken\t=18\t// Do NOT change this line\r\nOptions\t=*[00]Disabled\t// Move \"*\" to the desired Option\r\n         [01]Enabled\r\n\r\n" +
            "Setup Question\t= Re-Size BAR Support\r\nToken\t=19\t// Do NOT change this line\r\nOptions\t=[00]Disabled\t// Move \"*\" to the desired Option\r\n        *[01]Auto\r\n\r\n" +
            "Setup Question\t= PCIe Link Width\r\nToken\t=20\t// Do NOT change this line\r\nValue\t=<16>\r\n";
        var bios = BiosService.Parse(nvram, out var biosHeader);
        Assert(bios.Count == 4 && bios[0].Question == "Above 4G Decoding" && bios[0].Options.Count == 2 && bios[0].SelectedIndex == 0 && bios[2].SelectedLabel == "Auto" && bios[3].NumericValue == "16", "Arquivo do SCEWIN lido (opções, selecionada e valor numérico)");
        Assert(BiosService.ApplyRecommendations(bios) == 1 && bios[0].SelectedLabel == "Enabled" && !bios[2].Changed, "Recomendações de BIOS mudam só o necessário (ReBAR já em Auto fica)");
        var rendered = BiosService.RenderBlock(bios[0]);
        Assert(rendered.Contains("Options\t=[00]Disabled\t// Move \"*\" to the desired Option") && rendered.Contains("        *[01]Enabled") && rendered.Contains("Token\t=17\t// Do NOT change this line"), "Bloco da BIOS regravado com o * na opção nova");
        var written = BiosService.Serialize(biosHeader, bios.Where(s => s.Changed));
        Assert(written.Contains("Above 4G Decoding") && !written.Contains("CSM Support") && written.StartsWith("// AMISCE"), "Só os itens alterados vão para o arquivo de gravação");
        Assert(BiosService.Parse(written, out _).Single().SelectedLabel == "Enabled", "Arquivo gravado volta a ser lido com o valor novo");
        var csmOn = BiosService.Parse(nvram.Replace("Options\t=*[00]Disabled\t// Move \"*\" to the desired Option\r\n         [01]Enabled\r\n\r\nSetup Question\t= Re-Size", "Options\t=[00]Disabled\t// Move \"*\" to the desired Option\r\n        *[01]Enabled\r\n\r\nSetup Question\t= Re-Size").Replace("        *[01]Auto", "         [01]Auto").Replace("Options\t=[00]Disabled\t// Move \"*\" to the desired Option\r\n         [01]Auto", "Options\t=*[00]Disabled\t// Move \"*\" to the desired Option\r\n         [01]Auto"), out _);
        BiosService.ApplyRecommendations(csmOn);
        Assert(csmOn[1].SelectedLabel == "Enabled" && csmOn[2].SelectedLabel == "Disabled", "Resizable BAR não é ligado com CSM ativo (evita PC sem iniciar)");

        // Configurações de jogos
        var (ini, iniChanges) = GameConfigService.SetIniValues("[/Script/FortniteGame.FortGameUserSettings]\nbUseVSync=True\nFrameRateLimit=0.000000\n\n[ScalabilityGroups]\nsg.ShadowQuality=3\n", new[]
        {
            new ConfigValue("/Script/FortniteGame.FortGameUserSettings", "bUseVSync", "False"), new ConfigValue("ScalabilityGroups", "sg.ShadowQuality", "0"),
            new ConfigValue("ScalabilityGroups", "sg.EffectsQuality", "0"), new ConfigValue("Nova", "X", "1"),
        }, "\n");
        Assert(iniChanges == 4 && ini.Contains("bUseVSync=False\nFrameRateLimit") && ini.Contains("sg.ShadowQuality=0\nsg.EffectsQuality=0") && ini.EndsWith("[Nova]\nX=1"), "Arquivo .ini alterado por seção, com chaves novas no lugar certo");
        var (kv, kvChanges) = GameConfigService.SetQuotedValues("\"VideoConfig\"\n{\n\t\"setting.mat_vsync\"\t\t\"1\"\n\t\"setting.other\"\t\t\"5\"\n}\n", new[] { new ConfigValue("", "setting.mat_vsync", "0"), new ConfigValue("", "setting.r_low_latency", "1") }, "\n");
        Assert(kvChanges == 2 && kv.Contains("\t\"setting.mat_vsync\"\t\t\"0\"") && kv.Contains("\t\"setting.r_low_latency\"\t\t\"1\"\n}") && kv.Contains("\"setting.other\"\t\t\"5\""), "Arquivo \"chave\" \"valor\" alterado sem mexer no resto");

        // NVIDIA: só leitura (gravar exige administrador e mudaria o driver deste PC)
        if (NvidiaProfileService.IsAvailable())
        {
            var current = NvidiaProfileService.ReadCurrent();
            Assert(current.Count == NvidiaProfileService.Settings.Length, "Configurações NVIDIA lidas do driver");
            foreach (var s in NvidiaProfileService.Settings) Console.WriteLine($"  NVIDIA {s.Title}: {s.Describe(current[s.Id])}");
        }
        else Console.WriteLine("SKIP NVIDIA indisponível neste PC");
        // As máquinas do GitHub Actions não têm placa de vídeo
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true" && DriverCleanService.DetectGpus().Count == 0) Console.WriteLine("SKIP Sem placa de vídeo no servidor de testes");
        else Assert(DriverCleanService.DetectGpus().Count > 0, "Placa de vídeo detectada pelo registro");

        Assert(MainWindowRate(12_500_000 / 8) == "12.5 Mbps" || MainWindowRate(12_500_000 / 8) == "12,5 Mbps", "Velocidade de rede formatada em Mbps");
    }

    static string MainWindowRate(double bytesPerSecond) =>
        (string)typeof(MainWindow).GetMethod("FormatRate", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { bytesPerSecond })!;

    static void TestScheduledTaskToggle(AutorunsService service)
    {
        const string task = "PQO-Verificacao";
        // Roda o cmd: mesmo sendo um programa do Windows, tarefa de terceiros que usa interpretador fica visível
        var created = RunTool("schtasks.exe", "/Create", "/TN", task, "/TR", "cmd.exe /c exit", "/SC", "DAILY", "/ST", "23:59", "/F");
        if (created != 0) { Console.WriteLine($"SKIP Tarefa de teste não pôde ser criada (código {created})"); return; }
        try
        {
            AutorunEntry Find() => service.ScanAsync().GetAwaiter().GetResult().Single(e => e.Category == AutorunCategory.Tasks && e.Key == @"\" + task);
            var entry = Find();
            Assert(entry.Enabled && entry.Detail.Contains("Diariamente") && !entry.IsWindows, "Tarefa agendada lida com o gatilho e visível");
            service.SetEnabledAsync(entry, false).GetAwaiter().GetResult();
            Assert(!Find().Enabled, "Tarefa desativada no Agendador de Tarefas");
            service.SetEnabledAsync(entry, true).GetAwaiter().GetResult();
            Assert(Find().Enabled, "Tarefa reativada");
        }
        finally { RunTool("schtasks.exe", "/Delete", "/TN", task, "/F"); }
    }

    // Mexer em serviços exige administrador: roda no GitHub Actions, localmente só quando o terminal está elevado
    static void TestServiceToggle(AutorunsService service)
    {
        var principal = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent());
        if (!principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator)) { Console.WriteLine("SKIP Serviço de teste exige administrador"); return; }
        const string name = "PQOVerificacao";
        RunTool("sc.exe", "delete", name);
        // O serviço só é configurado, nunca iniciado; o executável só precisa existir
        Assert(RunTool("sc.exe", "create", name, "binPath=", Path.Combine(Environment.SystemDirectory, "cmd.exe"), "start=", "delayed-auto") == 0, "Serviço de teste criado");
        try
        {
            AutorunEntry Find() => service.ScanAsync().GetAwaiter().GetResult().Single(e => e.Category == AutorunCategory.Services && e.Key == name);
            var entry = Find();
            Assert(entry.Enabled && entry.Detail.StartsWith("Automático (atraso"), "Serviço automático com início atrasado lido");
            service.SetEnabledAsync(entry, false).GetAwaiter().GetResult();
            var disabled = Find();
            Assert(!disabled.Enabled && disabled.Detail.StartsWith("Manual"), "Desativar passa o serviço para Manual e ele continua na lista");
            service.SetEnabledAsync(disabled, true).GetAwaiter().GetResult();
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name);
            Assert(key?.GetValue("Start") as int? == 2 && key.GetValue("DelayedAutostart") as int? == 1, "Reativar devolve o início automático atrasado original");
        }
        finally { RunTool("sc.exe", "delete", name); }
    }

    static void TestPowerMode()
    {
        // Resolve as funções nativas declaradas (DLL + nome) sem executá-las. As do modo de energia ficam de
        // fora: não são documentadas e podem faltar no Windows Server do CI (o app só esconde os modos).
        System.Runtime.InteropServices.Marshal.PrelinkAll(typeof(AutorunsService));
        foreach (var method in typeof(PowerModeService).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                     .Where(m => m.GetCustomAttribute<System.Runtime.InteropServices.DllImportAttribute>() != null && !m.Name.Contains("Overlay")))
            System.Runtime.InteropServices.Marshal.Prelink(method);
        Assert(true, "Funções nativas de energia, serviços e ícones existem neste Windows");
        Assert(PowerModeService.ModeFromOverlay(PowerModeService.OverlayFor(PowerMode.Efficiency)) == PowerMode.Efficiency
            && PowerModeService.ModeFromOverlay(PowerModeService.OverlayFor(PowerMode.Performance)) == PowerMode.Performance
            && PowerModeService.ModeFromOverlay(Guid.Empty) == PowerMode.Balanced, "Modos de energia mapeados para os valores do Windows");
        var plans = PowerModeService.GetPlans();
        Assert(PowerModeService.GetActivePlan() is { } active && plans.Any(p => p.Id == active && p.Name.Length > 0), "Planos de energia lidos com nome e plano ativo");
        Assert(PowerModeService.Describe(new PowerSource(true, false, 45, TimeSpan.FromMinutes(130), false)) == "Na bateria · 45% · cerca de 2h 10min restantes"
            && PowerModeService.Describe(new PowerSource(false, true, null, null, false)) == "Computador sem bateria (desktop)", "Estado da bateria descrito");
        var arguments = PowerModeService.ShortcutArguments(@"C:\Program Files\PQueiroz Optimizer\PQueirozOptimizer.exe");
        Assert(arguments.Contains("__COMPAT_LAYER=RunAsInvoker") && arguments.EndsWith("\"C:\\Program Files\\PQueiroz Optimizer\\PQueirozOptimizer.exe\" --power-mode"), "Atalho abre o seletor sem pedir administrador");
        Translator.IsEnglish = true;
        Assert(Translator.Tr("Na bateria · 45% · cerca de 2h 10min restantes") == "On battery · 45% · about 2h 10min left" && Translator.Tr("Ativado: Melhor desempenho") == "Activated: Best performance", "Seletor de energia traduzido");
        Assert(Translator.Tr("5 ativos · 0 desativados · 210 itens do Windows ocultos") == "5 enabled · 0 disabled · 210 Windows entries hidden", "Resumo da Inicialização traduzido");
        Translator.IsEnglish = false;
    }

    static int RunTool(string file, params string[] arguments)
    {
        var start = new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start)!;
        process.StandardOutput.ReadToEnd(); process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
    }

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            var root = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/verification");
            Directory.CreateDirectory(root);
            // Fotos e vídeo do site (não rodam os testes)
            if (args.Contains("--shots")) return Media.Shots(root);
            if (args.Contains("--tour")) return Media.Tour(root);
            if (args.Contains("--videoshots")) return Media.VideoShots(root);
            var log = new ActivityLog(Path.Combine(root, "verification.log"));
            var bridge = new PowerShellBridge(log);
            Assert(bridge.GetSteps("padrao").Count == 7, "Plano padrão contém 7 etapas");
            Assert(bridge.GetSteps("gamer").Count == 17, "Plano avançado contém 17 etapas");
            Assert(!UpdateService.CanAutoInstall(new UpdateInfo(true, "1.0.0", "1.1.0", null, "https://github.com/PQueirozDev/Optimizer/releases/download/v1.1.0/Setup.exe", "Setup.exe", null)), "Atualização sem hash publicado não é instalada automaticamente");
            Assert(new LicenseService().MachineId.Length == 20, "ID do computador gerado");
            Assert(new LicenseService().DisplayMachineId.Replace("-", "") == new LicenseService().MachineId, "ID exibido em blocos equivale ao ID real");
            var sampleKey = "PQO1-eyJQcm9kdWN0IjoiUFEifQ." + new string('A', 342);
            Assert(LicenseService.ExtractKey($"Sua chave:\n{sampleKey[..60]}\n{sampleKey[60..]}\nObrigado!") == sampleKey, "Chave extraída de mensagem com quebras de linha");
            Assert(LicenseService.ExtractKey("sem chave aqui") is null, "Texto sem chave é ignorado");
            Assert(!new LicenseService().TryActivate(sampleKey, out _, out _), "Chave com assinatura falsa é recusada");
            Assert(bridge.GetSteps("debloat").Count == 19, "Plano debloat contém 19 etapas, limpeza usa análise separada");
            // O aviso da tela de revisão vem do código da etapa, não de palavras do nome
            StepEffect Effect(string operation, string step) => bridge.GetSteps(operation).First(s => s.Name == step).Effect;
            Assert(Effect("debloat", "Pesquisa na web removida do menu Iniciar") == StepEffect.Backup, "Ajuste de registro com backup não é marcado como irreversível pelo nome");
            Assert(Effect("debloat", "Cortana removida") == StepEffect.Irreversible && Effect("debloat", "OneDrive removido") == StepEffect.Irreversible, "Remoção de apps marcada como não reversível");
            Assert(Effect("padrao", "Limpando cache DNS") == StepEffect.OneOff && Effect("gamer", "Otimizando unidades de disco") == StepEffect.OneOff, "Cache DNS e TRIM são ações pontuais");
            Assert(Effect("padrao", "Executando limpeza de disco (cleanmgr)") == StepEffect.Irreversible && Effect("padrao", "Plano de energia 'Alto Desempenho'") == StepEffect.Backup, "Limpeza de disco não reversível e plano de energia com backup");
            Assert(bridge.GetSteps("gamer").Count(s => s.Effect == StepEffect.Irreversible) == 0, "Versão Avançada não tem etapa irreversível");
            var configPath = Path.Combine(root, "config-test.json");
            File.WriteAllText(configPath, """{"activeProfile":"Modo Gamer","profiles":[{"name":"Modo Gamer","enabledOptimizations":null}]}""");
            var config = new ConfigService(configPath);
            Assert(config.Config.ActiveProfile == "Modo Gamer", "Perfil gamer preservado na carga");
            Assert(config.GetActiveProfile().EnabledOptimizations.Count > 0, "Lista nula de ajustes recuperada");
            config.Save();
            Assert(new ConfigService(configPath).Config.ActiveProfile == "Modo Gamer", "Perfil persiste após salvar");
            // Regravar a cada abertura disputava o arquivo com outra instância ("config.json.tmp em uso")
            File.SetLastWriteTimeUtc(configPath, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            _ = new ConfigService(configPath);
            Assert(File.GetLastWriteTimeUtc(configPath).Year == 2020, "Abrir sem mudanças não regrava as preferências");
            Assert(ConfigService.ShowsEverything("Padrão") && !ConfigService.ShowsEverything("Modo Avançado"), "Modos que sempre exibem tudo identificados");
            var driver = new DriverService();
            SystemSnapshot Snapshot(string gpu) => new("Windows", "1", "x64", "CPU", gpu, 16, 100, 50, DateTime.Now.AddHours(-1), false);

            // Painel: números não passam mais por texto formatado ("1.863,0 GB" era lido como 0 GB)
            var bigDisk = new SystemSnapshot("Windows", "1", "x64", "CPU", "GPU", 16, 1863, 1000, DateTime.Now.AddHours(-5), true);
            Assert(Math.Abs(bigDisk.FreePercent - 53.68) < 0.1, "Disco de 2 TB calcula o espaço livre (antes aparecia 0% livre)");
            Assert(bigDisk.Storage.EndsWith(" TB") && bigDisk.Uptime.TotalHours is > 4.9 and < 5.1 && bigDisk.UptimeText == "5h 0m", "Tamanho em TB e tempo ligado pela hora do boot");
            var health = ((int Score, string Headline, string Tone))typeof(MainWindow).GetMethod("HealthScore", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { bigDisk })!;
            Assert(health.Score == 100 && health.Tone == "Success", "Disco grande e saudável mantém a nota máxima");
            var fullDisk = bigDisk with { FreeGb = 90 };
            Assert(((int Score, string, string))typeof(MainWindow).GetMethod("HealthScore", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { fullDisk })! is { Score: 65 }, "Disco quase cheio reduz a nota");
            Assert(!driver.GetAllDrivers(Snapshot("AMD Radeon"))[0].IsRecommendedForCurrentHardware, "NVIDIA não recomendado para AMD");
            Assert(driver.GetAllDrivers(Snapshot("NVIDIA GeForce"))[0].IsRecommendedForCurrentHardware, "NVIDIA recomendado para GPU compatível");

            var cleanRoot = Path.Combine(root, "clean-fixtures");
            Directory.CreateDirectory(cleanRoot);
            var old = Path.Combine(cleanRoot, "old.tmp");
            var recent = Path.Combine(cleanRoot, "recent.tmp");
            var changed = Path.Combine(cleanRoot, "changed.tmp");
            var outside = Path.Combine(root, "outside.tmp");
            foreach (var file in new[] { old, recent, changed, outside }) File.WriteAllText(file, "fixture");
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-3));
            File.SetLastWriteTimeUtc(changed, DateTime.UtcNow.AddDays(-3));
            CleanFile Entry(string path) { var info = new FileInfo(path); return new(path, info.Length, info.LastWriteTimeUtc); }
            var entries = new[] { Entry(old), Entry(recent), Entry(changed), Entry(outside) };
            File.AppendAllText(changed, "changed");
            var result = new QuickCleanService(log).CleanAsync(new[] { new CleanCategory("Teste", cleanRoot, entries, 0) }).GetAwaiter().GetResult();
            Assert(result.Removed == 1 && !File.Exists(old), "Limpeza remove somente arquivo antigo aprovado");
            Assert(File.Exists(recent) && File.Exists(changed) && File.Exists(outside), "Limpeza preserva recente, alterado e fora da raiz");

            // Tradução da saída do script e das classificações do console ao vivo
            Translator.IsEnglish = true;
            Assert(Translator.Tr("Limpando cache DNS") == "Clearing DNS cache", "Nome de etapa traduzido");
            Assert(Translator.Tr("Resumo: 5 aplicado(s), 0 falha(s)") == "Summary: 5 applied, 0 failed", "Resumo do script traduzido");
            Assert(MainWindow.ClassifyLine("  [3/16]    [OK] Limpando cache DNS") is { Tone: "Success", Text: "Limpando cache DNS" }, "Linha de sucesso classificada");
            Assert(MainWindow.ClassifyLine("[FALHA] Cortana removida: acesso negado") is { Tone: "Danger" }, "Linha de falha classificada");
            Assert(MainWindow.ClassifyLine("======================") is null, "Moldura decorativa ignorada");
            Assert(Translator.Tr("Cortana removida: acesso negado").StartsWith("Cortana removed: "), "Rótulo antes de dois-pontos traduzido");
            Assert(Translator.Tr("  ● Disponível para download oficial") == "  ● Available for official download", "Símbolo e espaços preservados");
            Assert(Translator.Tr("Tarefa \\Microsoft\\Windows\\Autochk\\Proxy nao existe neste Windows; nada a fazer.").StartsWith("Task \\Microsoft"), "Tarefa ausente traduzida");
            Assert(Translator.Tr("Otimização — cancelado. O que já foi aplicado aparece em Atividade e reversão.") == "Optimization — cancelled. What was already applied shows up in Activity & restore.", "Cancelamento traduzido");
            Assert(Translator.Tr("CPU em uso agora: 12%") == "CPU in use now: 12%", "Uso de CPU do benchmark traduzido");
            Assert(MainWindow.ClassifyLine("[AVISO] Pouco espaco livre em disco") is { Tone: "Warning" } && MainWindow.ClassifyLine("[OK] TRIM: Ativo") is { Tone: "Success" }, "Linhas do diagnóstico coloridas pela marcação");
            Translator.IsEnglish = false;
            Assert(Translator.Tr("Limpando cache DNS") == "Limpando cache DNS", "Português permanece sem alteração");

            // Inicialização: formato das chaves StartupApproved (mesmo do Gerenciador de Tarefas)
            Assert(StartupService.IsEnabled(null) && StartupService.IsEnabled(new byte[] { 2, 0, 0, 0 }) && !StartupService.IsEnabled(new byte[] { 3, 0, 0, 0 }), "Estado de inicialização lido corretamente");
            Assert(StartupService.BuildApprovedValue(false, DateTime.UtcNow) is { Length: 12 } off && off[0] == 3 && BitConverter.ToInt64(off, 4) > 0, "Desativar grava 03 + data");
            Assert(StartupService.BuildApprovedValue(true, DateTime.UtcNow) is { Length: 12 } on && on[0] == 2, "Ativar grava 02");
            Assert(StartupService.ExecutablePath("\"C:\\Program Files\\App\\a.exe\" --min") == @"C:\Program Files\App\a.exe", "Caminho entre aspas extraído");
            Assert(StartupService.ExecutablePath(@"C:\Tools\b.exe -silent") == @"C:\Tools\b.exe", "Caminho sem aspas extraído");

            // Licença: aviso de vencimento (mesma janela de 7 dias do License Manager) e pedido de renovação
            var endOfDay = DateTime.Today.AddDays(1).AddSeconds(-1);
            Assert(new LicenseInfo("A", null, "X", "Standard") is { DaysLeft: null, IsExpiringSoon: false }, "Licença vitalícia não avisa vencimento");
            Assert(new LicenseInfo("A", endOfDay.ToUniversalTime(), "X", "Standard") is { DaysLeft: 0, IsExpiringSoon: true }, "Vence hoje avisa");
            Assert(new LicenseInfo("A", endOfDay.AddDays(7).ToUniversalTime(), "X", "Standard") is { DaysLeft: 7, IsExpiringSoon: true }, "Vence em 7 dias avisa");
            Assert(!new LicenseInfo("A", endOfDay.AddDays(8).ToUniversalTime(), "X", "Standard").IsExpiringSoon, "Vence em 8 dias ainda não avisa");
            var licenseService = new LicenseService();
            var renewal = licenseService.BuildActivationRequest("Maria Souza");
            // Mesmas expressões que o License Manager usa para ler o pedido colado
            var requestId = System.Text.RegularExpressions.Regex.Match(renewal, @"(?<![0-9A-Fa-f])[0-9A-Fa-f]{5}(?:[\s-]?[0-9A-Fa-f]{5}){3}(?![0-9A-Fa-f])");
            var requestPc = System.Text.RegularExpressions.Regex.Match(renewal, @"Computador:[ \t]*(.+)");
            Assert(renewal.StartsWith("Pedido de renovação") && renewal.Contains("Titular: Maria Souza") && requestId.Value == licenseService.DisplayMachineId && requestPc.Groups[1].Value.Trim() == Environment.MachineName, "Pedido de renovação legível pelo License Manager");
            Assert(licenseService.BuildActivationRequest().StartsWith("Pedido de ativação") && !licenseService.BuildActivationRequest().Contains("Titular"), "Pedido de ativação continua igual");
            var upgrade = licenseService.BuildActivationRequest("Maria Souza", LicensePlans.Lifetime);
            Assert(upgrade.StartsWith("Pedido de upgrade") && upgrade.Contains("Plano desejado: Vitalício") && upgrade.Contains(licenseService.DisplayMachineId), "Pedido de upgrade leva o plano desejado");
            // Planos: o da chave vale; chaves antigas (sem o campo) são deduzidas pela validade
            Assert(new LicenseInfo("A", DateTime.UtcNow.AddDays(30), "X", "Standard", "Mensal").PlanName == "Mensal"
                && new LicenseInfo("A", null, "X", "Standard").PlanName == "Vitalício"
                && new LicenseInfo("A", DateTime.UtcNow.AddDays(30), "X", "Standard").PlanName == "Personalizado"
                && new LicenseInfo("A", null, "X", "Standard", "Inventado").PlanName == "Vitalício", "Nome do plano da licença");
            Translator.IsEnglish = true;
            Assert(Translator.Tr("Sua licença expirou em 12/09/2026.") == "Your license expired on 12/09/2026." && Translator.Tr("Sua licença vence em 5 dias") == "Your license expires in 5 days"
                && Translator.Tr("A chave atual vale até 03/10/2026. Para renovar, abra um ticket de renovação no Discord e pague via Pix.").StartsWith("The current key is valid until 03/10/2026."), "Avisos de licença traduzidos");
            Translator.IsEnglish = false;

            // Revogação: lista assinada (aqui por uma chave só do teste), mais nova que a guardada, cache offline
            using (var testSigner = new System.Security.Cryptography.RSACryptoServiceProvider(2048))
            {
                var testPublic = Convert.ToBase64String(testSigner.ExportCspBlob(false));
                static string B64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                string SignedList(DateTime issuedAtUtc, string[] ids, string kind = "revocations")
                {
                    var payload = JsonSerializer.SerializeToUtf8Bytes(new { Product = "PQueirozOptimizer", Kind = kind, IssuedAtUtc = issuedAtUtc, Revoked = ids });
                    return JsonSerializer.Serialize(new { Payload = B64Url(payload), Signature = B64Url(testSigner.SignData(payload, System.Security.Cryptography.CryptoConfig.MapNameToOID("SHA256")!)) });
                }
                var cache = Path.Combine(root, "revocations.json");
                File.Delete(cache);
                var revocations = new RevocationService(cache, testPublic);
                var revokedId = RevocationService.KeyId("chave-revogada"u8.ToArray());
                Assert(!revocations.IsRevoked(revokedId), "Sem lista, nada revogado");
                Assert(revocations.TryApply(SignedList(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), [revokedId])) && revocations.IsRevoked(revokedId), "Lista assinada revoga a chave");
                Assert(!revocations.TryApply(SignedList(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), [])) && revocations.IsRevoked(revokedId), "Lista mais antiga não desfaz a revogação");
                var tampered = SignedList(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), []).Replace("\"Signature\":\"", "\"Signature\":\"A");
                Assert(!revocations.TryApply(tampered) && !revocations.TryApply(SignedList(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), [], kind: "license")), "Lista adulterada ou de outro tipo é recusada");
                Assert(new RevocationService(cache, testPublic).IsRevoked(revokedId), "Revogação vale offline (cache)");
                Assert(!new RevocationService(cache).IsRevoked(revokedId), "Lista assinada por outra chave não vale para o emissor real");
            }

            // Trava do relógio: atrasar o Windows não estica a validade; a hora do servidor corrige para baixo
            File.Delete(Path.Combine(root, "clock.dat"));
            var clock = new LicenseClock(Path.Combine(root, "clock.dat"));
            var clockNow = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
            clock.Observe(clockNow);
            Assert(!clock.IsRolledBack(clockNow.AddHours(-20)), "Pequeno acerto do relógio é tolerado");
            Assert(clock.IsRolledBack(clockNow.AddDays(-3)), "Relógio atrasado dias é detectado");
            Assert(clock.EffectiveNowUtc(clockNow.AddHours(-20)) == clockNow, "Validade usa a última data vista");
            clock.Observe(clockNow.AddDays(-5));
            Assert(clock.LastSeenUtc == clockNow, "Data mais antiga não substitui a última vista");
            clock.SetTrusted(clockNow.AddDays(-2));
            Assert(!clock.IsRolledBack(clockNow.AddDays(-2)), "Hora do servidor corrige relógio que estava adiantado");
            TestStartupPage(log);
            TestPowerMode();
            TestGamingFeatures(root);

            if (args.Contains("--i18n"))
            {
                var app = CreateTestApp();
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                Translator.Attach();
                Translator.IsEnglish = true;
                var window = new MainWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false };
                typeof(MainWindow).GetField("_loc", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!.GetType().GetMethod("SetLanguage")!.Invoke(typeof(MainWindow).GetField("_loc", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window), new object[] { "en" });
                window.Show();
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                void Wait(int ms) { var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) }; timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); }
                var portuguese = new System.Text.RegularExpressions.Regex(@"[áàâãéêíóôõúçÁÉÍÓÚÇÃÕ]|\b(de|do|da|dos|das|para|com|seu|sua|você|não|nao|um|uma|os|pelo|pela|ou|está|são|ativo|ativa|ativar|desativado|pronto|salvar|excluir|abrir|revisar|baixar|voltar|copiar|sair|limpar|todos|todas|nenhum|nenhuma|\w+ando|\w+endo|\w+cao|\w+coes)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var ignore = new System.Text.RegularExpressions.Regex(@"Pedro Queiroz|PQueiroz|pqueiroz|\\|NVIDIA|Intel|AMD|Microsoft Windows|GeForce|Ryzen|Radeon|https?://|\.exe|\.lnk");
                var found = new SortedSet<string>();
                void Collect(DependencyObject node)
                {
                    // Textos marcados como dados do sistema (nomes e descrições de programas) não são do app
                    if (node is System.Windows.Controls.TextBlock tb && tb.Tag as string != Translator.SystemDataTag && !string.IsNullOrWhiteSpace(tb.Text) && portuguese.IsMatch(tb.Text) && !ignore.IsMatch(tb.Text)) found.Add(tb.Text.Replace("\n", " ⏎ "));

                    for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Collect(VisualTreeHelper.GetChild(node, i));
                }
                void Snap(string name) { var visual = (FrameworkElement)window.Content; var bmp = new RenderTargetBitmap((int)visual.ActualWidth, (int)visual.ActualHeight, 96, 96, PixelFormats.Pbgra32); bmp.Render(visual); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bmp)); using var fs = File.Create(Path.Combine(root, name + ".png")); png.Save(fs); }
                Wait(4000);
                // O App do teste dispara a inicialização real (idioma salvo + outra janela); desfaz isso
                if (Application.Current != null) { Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown; Application.Current.MainWindow = window; }
                foreach (Window extra in (Application.Current?.Windows.Cast<Window>() ?? Enumerable.Empty<Window>()).Where(w => w != window).ToList()) extra.Close();
                Translator.IsEnglish = true;
                foreach (var page in new[] { "dashboard", "optimization", "startup", "drivers", "tools", "gaming", "network", "restore", "resources", "fixes", "services", "apps", "history", "settings", "about", "patchnotes", "bios" })
                {
                    typeof(MainWindow).GetMethod("NavigateTo", flags)!.Invoke(window, new object[] { page });
                    Wait(page == "dashboard" ? 4000 : page == "startup" ? 8000 : 600);
                    Collect(window); Snap("en-" + page);
                }
                foreach (var tab in new[] { 1, 2, 3 })
                {
                    typeof(MainWindow).GetField("_gamingTab", flags)!.SetValue(window, tab);
                    typeof(MainWindow).GetMethod("NavigateTo", flags)!.Invoke(window, new object[] { "gaming" });
                    Wait(tab == 3 ? 6000 : 800); Collect(window); Snap($"en-gaming-tab{tab}");
                }
                typeof(MainWindow).GetMethod("ShowIsos", flags)!.Invoke(window, null);
                Wait(600); Collect(window); Snap("en-isos");
                var picker = new PowerModeWindow(log, closeAfterChoice: false) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, ShowActivated = false };
                picker.Show(); Wait(800); Collect(picker); picker.Close();
                var activation = new ActivationWindow(new LicenseService()) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, ShowActivated = false };
                activation.Show(); Wait(800); Collect(activation); activation.Close();
                foreach (var op in new[] { "padrao", "gamer", "debloat", "quickclean" })
                {
                    ((Task)typeof(MainWindow).GetMethod("PrepareOperationAsync", flags)!.Invoke(window, new object[] { op })!).ContinueWith(_ => { });
                    Wait(op == "quickclean" ? 6000 : 600);
                    Collect(window); Snap("en-review-" + op);
                }
                ((Task)typeof(MainWindow).GetMethod("RunLiveAsync", flags)!.Invoke(window, new object?[] { "analisar", null })!).ContinueWith(_ => { });
                Wait(8000); Collect(window); Snap("en-live-analisar");
                Console.WriteLine($"I18N {found.Count} textos possivelmente em português:");
                foreach (var text in found) Console.WriteLine("  " + text);
                window.Close();
            }

            if (args.Contains("--render"))
            {
                var app = CreateTestApp();
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher)); var window = new MainWindow();
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                // A janela não é exibida, então a animação de abertura nunca roda e a cobertura dela taparia tudo
                ((UIElement)typeof(MainWindow).GetField("IntroOverlay", flags)!.GetValue(window)!).Visibility = Visibility.Collapsed;
                void Pump(Task task)
                {
                    var frame = new DispatcherFrame();
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
                    timer.Tick += (_, _) => { if (task.IsCompleted) frame.Continue = false; };
                    timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
                    task.GetAwaiter().GetResult();
                }
                Pump((Task)typeof(MainWindow).GetMethod("RenderDashboardAsync", flags)!.Invoke(window, null)!);
                void Render(string name, int width, int height)
                {
                    var visual = (FrameworkElement)window.Content;
                    visual.Measure(new Size(width, height)); visual.Arrange(new Rect(0, 0, width, height)); visual.UpdateLayout();
                    var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
                    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(root, name + ".png")); png.Save(stream);
                    Console.WriteLine("RENDER " + name);
                }
                Render("dashboard-dark", 1320, 860);
                typeof(MainWindow).GetMethod("NavigateTo", flags)!.Invoke(window, new object[] { "optimization" });
                Render("optimizations-dark", 1060, 700);
                Pump((Task)typeof(MainWindow).GetMethod("PrepareOperationAsync", flags)!.Invoke(window, new object[] { "gamer" })!);
                Render("review-dark", 1060, 700);
                typeof(MainWindow).GetMethod("NavigateTo", flags)!.Invoke(window, new object[] { "history" });
                Render("history-dark", 1320, 860);
                foreach (var page in new[] { "drivers", "startup", "tools", "gaming", "network", "restore", "resources", "fixes", "services", "apps", "settings", "about", "patchnotes", "bios" })
                {
                    typeof(MainWindow).GetMethod("NavigateTo", flags)!.Invoke(window, new object[] { page });
                    Render(page + "-dark", 1320, 860);
                }
                foreach (var tab in new[] { 1, 2, 3 })
                {
                    typeof(MainWindow).GetField("_gamingTab", flags)!.SetValue(window, tab);
                    typeof(MainWindow).GetMethod("NavigateTo", flags)!.Invoke(window, new object[] { "gaming" });
                    Render($"gaming-tab{tab}-dark", 1320, 860);
                }
                typeof(MainWindow).GetField("_gamingTab", flags)!.SetValue(window, 0);
                // Página exclusiva de licença admin: chamada direto, sem a checagem da navegação
                typeof(MainWindow).GetMethod("ShowIsos", flags)!.Invoke(window, null);
                Render("isos-dark", 1320, 860);
                // Variações de aparência em resoluções menores (aplicadas só na memória, sem salvar)
                void Variant(string name, PQueirozOptimizer.Models.AppearanceSettings look, int width, int height, params string[] pages)
                {
                    PQueirozOptimizer.Services.AppearanceService.Apply(look);
                    foreach (var page in pages)
                    {
                        typeof(MainWindow).GetMethod("NavigateTo", flags)!.Invoke(window, new object[] { page });
                        if (page == "dashboard") Pump((Task)typeof(MainWindow).GetMethod("RenderDashboardAsync", flags)!.Invoke(window, null)!);
                        Render($"{page}-{name}", width, height);
                    }
                }
                Variant("oled-1366", new() { Theme = PQueirozOptimizer.Models.ThemeMode.Oled, Accent = PQueirozOptimizer.Models.AccentIntensity.Vibrant, Density = PQueirozOptimizer.Models.Density.Compact, CardSize = PQueirozOptimizer.Models.CardSize.Compact }, 1366, 728, "dashboard", "settings", "resources", "services");
                Variant("comfort-1600", new() { Accent = PQueirozOptimizer.Models.AccentIntensity.Soft, Density = PQueirozOptimizer.Models.Density.Comfortable, CardSize = PQueirozOptimizer.Models.CardSize.Large }, 1600, 860, "dashboard", "fixes");
                PQueirozOptimizer.Services.AppearanceService.Apply(PQueirozOptimizer.Services.AppearanceService.Defaults());
                typeof(MainWindow).GetMethod("ApplyTheme", flags)!.Invoke(window, new object[] { false, false });
                typeof(MainWindow).GetMethod("NavigateTo", flags)!.Invoke(window, new object[] { "dashboard" });
                Pump((Task)typeof(MainWindow).GetMethod("RenderDashboardAsync", flags)!.Invoke(window, null)!);
                Render("dashboard-light", 1320, 860);
                typeof(MainWindow).GetMethod("NavigateTo", flags)!.Invoke(window, new object[] { "optimization" });
                Render("optimizations-light", 1320, 860);
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}




