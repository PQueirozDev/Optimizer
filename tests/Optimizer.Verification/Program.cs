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
        // Plano completo: as telas renderizadas e a checagem --i18n mostram as páginas de verdade, não a tela de upgrade
        app.SetActiveLicense(new LicenseInfo("Teste", null, "TESTE", "Standard", LicensePlans.Lifetime));
        return app;
    }

    /// <summary>
    /// Interruptores e caixas de seleção numa janela de verdade: depois de cada sequência (criar já ligado,
    /// ligar, desligar e religar rápido, reverter dentro do evento), o desenho tem que bater com o estado.
    /// </summary>
    static int Switches()
    {
        var app = CreateTestApp();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var panel = new System.Windows.Controls.StackPanel();
        var window = new Window { Content = panel, Width = 400, Height = 600, Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        window.Show();
        void Wait(int ms) { var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) }; timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); }
        System.Windows.Controls.CheckBox Make(bool isSwitch, bool initial)
        {
            var check = new System.Windows.Controls.CheckBox { IsChecked = initial, Content = "x" };
            if (isSwitch) check.SetResourceReference(FrameworkElement.StyleProperty, "SwitchCheckBox");
            panel.Children.Add(check);
            return check;
        }
        var failures = 0;
        void Check(string name, System.Windows.Controls.CheckBox check)
        {
            check.ApplyTemplate();
            var on = check.IsChecked == true;
            string drawn;
            if (check.Template.FindName("knob", check) is FrameworkElement knob)
            {
                var x = knob.TranslatePoint(new Point(0, 0), (UIElement)VisualTreeHelper.GetParent(knob)).X;
                var trackOn = ((UIElement)check.Template.FindName("trackOn", check)).Opacity;
                var knobOn = x > 12; var colorOn = trackOn > 0.5;
                drawn = $"bolinha x={x:0} cor={trackOn:0.##}";
                if (knobOn != on || colorOn != on) { failures++; Console.WriteLine($"FAIL {name}: estado={(on ? "ligado" : "desligado")} mas {drawn}"); return; }
            }
            else
            {
                var fill = ((UIElement)check.Template.FindName("fill", check)).Opacity;
                drawn = $"preenchimento={fill:0.##}";
                if ((fill > 0.5) != on) { failures++; Console.WriteLine($"FAIL {name}: estado={(on ? "marcado" : "desmarcado")} mas {drawn}"); return; }
            }
            Console.WriteLine($"PASS {name} ({(on ? "ligado" : "desligado")}, {drawn})");
        }
        foreach (var animations in new[] { true, false })
        foreach (var isSwitch in new[] { true, false })
        {
            PQueirozOptimizer.Services.AppearanceService.Apply(new PQueirozOptimizer.Models.AppearanceSettings { Animations = animations });
            var kind = (isSwitch ? "Interruptor" : "Caixa") + (animations ? "" : " sem animação");
            var a = Make(isSwitch, true); var b = Make(isSwitch, false); var c = Make(isSwitch, false);
            var d = Make(isSwitch, false); var e = Make(isSwitch, true); var f = Make(isSwitch, true);
            Wait(500);
            Check($"{kind} criado ligado", a);
            Check($"{kind} criado desligado", b);
            c.IsChecked = true; Wait(500); Check($"{kind} ligado depois", c);
            d.IsChecked = true; d.IsChecked = false; d.IsChecked = true; Wait(500); Check($"{kind} liga/desliga/liga no mesmo quadro", d);
            e.IsChecked = false; Wait(60); e.IsChecked = true; Wait(500); Check($"{kind} desliga e religa no meio da animação", e);
            // Página Serviços: a ação falhou e o evento volta o interruptor
            f.Unchecked += (_, _) => f.IsChecked = true;
            f.IsChecked = false; Wait(500); Check($"{kind} revertido dentro do evento", f);
            var h = Make(isSwitch, false); h.Checked += (_, _) => h.IsChecked = false;
            h.IsChecked = true; Wait(500); Check($"{kind} ligado e revertido para desligado", h);
            // Página redesenhada com o mesmo estado (troca de tema, voltar à página)
            var g = Make(isSwitch, true); Wait(30); panel.Children.Remove(g); panel.Children.Add(g); Wait(500); Check($"{kind} recolocado na tela", g);
        }
        // Serviços: a ação falha na hora (outra operação em andamento). Antes, a volta disparava a ação oposta,
        // que também falhava e voltava... até o app fechar com estouro de pilha.
        var main = new MainWindow();
        var bind = typeof(MainWindow).GetMethod("BindActionToggle", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var calls = 0;
        var failing = Make(true, false);
        bind.Invoke(main, new object?[] { failing, new Func<bool, Task<bool>>(_ => { calls++; return Task.FromResult(false); }), null });
        failing.IsChecked = true; Wait(400);
        if (failing.IsChecked != false || calls != 1) { failures++; Console.WriteLine($"FAIL Ação que falha na hora: estado={failing.IsChecked}, chamadas={calls}"); }
        else Console.WriteLine("PASS Ação que falha na hora roda uma vez e o interruptor volta, sem repetir");
        Check("Interruptor depois da falha", failing);
        var later = Make(true, true); var laterCalls = 0;
        bind.Invoke(main, new object?[] { later, new Func<bool, Task<bool>>(async _ => { laterCalls++; await Task.Delay(900); return false; }), null });
        later.IsChecked = false; Wait(100);
        var lockedWhileRunning = !later.IsEnabled;
        Wait(1200);
        if (later.IsChecked != true || laterCalls != 1 || !lockedWhileRunning || !later.IsEnabled) { failures++; Console.WriteLine($"FAIL Ação que falha depois: estado={later.IsChecked}, chamadas={laterCalls}, bloqueado durante={lockedWhileRunning}"); }
        else Console.WriteLine("PASS Ação demorada bloqueia o interruptor e, ao falhar, ele volta ligado");
        var success = Make(true, false); var okCalls = 0;
        bind.Invoke(main, new object?[] { success, new Func<bool, Task<bool>>(async _ => { okCalls++; await Task.Delay(50); return true; }), null });
        success.IsChecked = true; Wait(400);
        if (success.IsChecked != true || okCalls != 1) { failures++; Console.WriteLine($"FAIL Ação que dá certo: estado={success.IsChecked}, chamadas={okCalls}"); }
        else Console.WriteLine("PASS Ação que dá certo mantém o interruptor ligado");
        main.Close();

        // Tira de quadros da animação (interruptor e caixa ligando), para conferir o movimento
        PQueirozOptimizer.Services.AppearanceService.Apply(new PQueirozOptimizer.Models.AppearanceSettings());
        panel.Children.Clear();
        panel.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty, "BackgroundBrush");
        var sw = Make(true, false); sw.Width = 90; sw.HorizontalAlignment = HorizontalAlignment.Left; sw.Margin = new Thickness(10); var cb = Make(false, false); cb.Margin = new Thickness(10);
        Wait(300);
        var strip = new RenderTargetBitmap(110 * 9, 90, 96, 96, PixelFormats.Pbgra32);
        var frames = new DrawingVisual();
        sw.IsChecked = true; cb.IsChecked = true;
        using (var dc = frames.RenderOpen())
            for (var i = 0; i < 9; i++)
            {
                var shot = new RenderTargetBitmap(110, 90, 96, 96, PixelFormats.Pbgra32); shot.Render(panel);
                dc.DrawImage(shot, new Rect(i * 110, 0, 110, 90));
                Wait(40);
            }
        strip.Render(frames);
        var pngStrip = new PngBitmapEncoder(); pngStrip.Frames.Add(BitmapFrame.Create(strip));
        using (var fs = File.Create(Path.Combine(Path.GetTempPath(), "switch-frames.png"))) pngStrip.Save(fs);
        window.Close();
        Console.WriteLine(failures == 0 ? "SWITCHES ok" : $"SWITCHES {failures} falha(s)");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// VALORANT com os perfis Otimizado e Qrz, numa cópia da pasta de configuração (os arquivos reais deste PC
    /// quando existem; senão, arquivos de exemplo no mesmo formato). Nada é gravado na pasta do jogo.
    /// </summary>
    static void TestValorantProfiles(string root)
    {
        var flags = BindingFlags.NonPublic | BindingFlags.Static;
        var rootProp = typeof(GameConfigService).GetProperty("ValorantConfigRoot", flags)!;
        var backupProp = typeof(GameConfigService).GetProperty("BackupRoot", flags)!;
        var realRoot = (string)rootProp.GetValue(null)!;
        var oldBackup = (string)backupProp.GetValue(null)!;
        var fake = Path.Combine(root, "valorant-config");
        if (Directory.Exists(fake)) Directory.Delete(fake, true);
        var account = Path.Combine(fake, "0ee800ee-7997-595b-a5ce-fb77c3a6648e-br", "Windows");
        Directory.CreateDirectory(account); Directory.CreateDirectory(Path.Combine(fake, "WindowsClient"));
        var realGame = Path.Combine(realRoot, "WindowsClient", "GameUserSettings.ini");
        var gameFile = Path.Combine(fake, "WindowsClient", "GameUserSettings.ini");
        var accountFile = Path.Combine(account, "RiotUserSettings.ini");
        File.WriteAllText(Path.Combine(fake, "WindowsClient", "RiotLocalMachine.ini"), "[UserInfo]\r\nLastKnownUser=0ee800ee-7997-595b-a5ce-fb77c3a6648e\r\n");
        if (File.Exists(realGame)) File.Copy(realGame, gameFile);
        else File.WriteAllText(gameFile, "[/Script/ShooterGame.ShooterGameUserSettings]\r\nbUseVSync=True\r\nFrameRateLimit=144.000000\r\nPreferredFullscreenMode=1\r\n");
        var realAccount = Directory.Exists(realRoot) ? Directory.EnumerateFiles(realRoot, "RiotUserSettings.ini", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
        if (realAccount != null) File.Copy(realAccount, accountFile);
        else File.WriteAllText(accountFile, "[Settings]\r\nEAresFloatSettingName::MouseSensitivity=0.4\r\nEAresIntSettingName::MaterialQuality=2\r\n");
        // Outra conta mais nova no disco: o arquivo certo é o da última conta que entrou (RiotLocalMachine.ini)
        var other = Path.Combine(fake, "1f43e3c8-2cb6-5ad9-97d7-bf7f8f056538-br", "Windows");
        Directory.CreateDirectory(other); File.WriteAllText(Path.Combine(other, "RiotUserSettings.ini"), "[Settings]\r\n");
        var originalGame = File.ReadAllBytes(gameFile); var originalAccount = File.ReadAllBytes(accountFile);
        var sensitivity = File.ReadAllLines(accountFile).FirstOrDefault(l => l.StartsWith("EAresFloatSettingName::MouseSensitivity="));
        try
        {
            rootProp.SetValue(null, fake);
            backupProp.SetValue(null, Path.Combine(root, "valorant-backup"));
            var valorant = GameConfigService.Presets.Single(p => p.Id == "valorant");
            Assert(GameConfigService.ConfigPath(valorant) == accountFile, "VALORANT: arquivo da última conta que entrou (RiotLocalMachine.ini)");
            var service = new GameConfigService(new ActivityLog(Path.Combine(root, "valorant.log")));
            service.Apply(valorant, "qrz");
            string Value(string file, string key) => File.ReadAllLines(file).FirstOrDefault(l => l.StartsWith(key + "="))?[(key.Length + 1)..] ?? "";
            Assert(Value(accountFile, "EAresIntSettingName::UIQuality") == "2" && Value(accountFile, "EAresIntSettingName::AnisotropicFiltering") == "16"
                && Value(accountFile, "EAresBoolSettingName::ShowBulletTracers") == "False" && Value(gameFile, "bUseVSync") == "False" && Value(gameFile, "PreferredFullscreenMode") == "0",
                "VALORANT: perfil Qrz grava a configuração do Qrz nos dois arquivos");
            Assert(GameConfigService.AppliedProfile(valorant) == "qrz", "VALORANT: perfil aplicado lembrado");
            Assert(sensitivity is null || File.ReadAllLines(accountFile).Contains(sensitivity), "VALORANT: sensibilidade do jogador não é alterada");
            service.Apply(valorant, "otimizado");
            Assert(Value(accountFile, "EAresIntSettingName::UIQuality") == "0" && Value(accountFile, "EAresIntSettingName::AnisotropicFiltering") == "4" && GameConfigService.AppliedProfile(valorant) == "otimizado",
                "VALORANT: troca para o perfil Otimizado");
            service.Restore(valorant);
            Assert(File.ReadAllBytes(gameFile).SequenceEqual(originalGame) && File.ReadAllBytes(accountFile).SequenceEqual(originalAccount) && GameConfigService.AppliedProfile(valorant) is null,
                "VALORANT: restaurar devolve os dois arquivos originais, byte a byte");
        }
        finally
        {
            rootProp.SetValue(null, realRoot);
            backupProp.SetValue(null, oldBackup);
        }
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
            if (eventLog[0].Signature != SignatureStatus.Verified)
            {
                var diagnostic = "try { $s = Get-AuthenticodeSignature -LiteralPath $env:PQO_SIGNATURE_TEST -ErrorAction Stop; $s | Select-Object Status,StatusMessage | ConvertTo-Json -Compress } catch { $_.Exception.Message }";
                Console.WriteLine("Signature diagnostic: " + PowerShellBridge.RunScriptAsync(diagnostic,
                    new Dictionary<string, string> { ["PQO_SIGNATURE_TEST"] = eventLog[0].ImagePath! }).GetAwaiter().GetResult());
            }
            Assert(eventLog[0].Signature == SignatureStatus.Verified && eventLog[0].IsWindows, $"Assinatura de catálogo do Windows: {eventLog[0].ImagePath}, status={eventLog[0].Signature}, signer={eventLog[0].Signer}, Windows={eventLog[0].IsWindows}");
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
        // Backup gravado por versões anteriores (sem "Status") continua valendo como aplicado após atualizar o app
        Directory.CreateDirectory(Path.Combine(root, "tweaks"));
        File.WriteAllText(Path.Combine(root, "tweaks", "legado.json"), """{"Id":"legado","Title":"Legado","Entries":[]}""");
        Assert(store.IsApplied("legado"), "Ajuste aplicado em versão anterior continua aparecendo como aplicado");
        Assert(StartupService.PackageFamily("OpenAI.ChatGPT-Desktop_1.2025.1.0_x64__2p2nqsd0c76g0") == "OpenAI.ChatGPT-Desktop_2p2nqsd0c76g0" && StartupService.PackageFamily("invalido") is null, "Família do pacote calculada a partir do nome completo");
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
        TestValorantProfiles(root);

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

        // Visão geral: leitura nativa (antes era um PowerShell com WMI de ~3 s a cada abertura)
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var snap = new SystemInfoService().Read();
        watch.Stop();
        Console.WriteLine($"  Sistema: {snap.OperatingSystem} | {snap.Build} | {snap.Architecture} | {snap.Processor} | {snap.Graphics} | {snap.MemoryGb} GB | {snap.StorageGb}/{snap.FreeGb} GB | ligado desde {snap.BootTime:g} | {watch.ElapsedMilliseconds} ms");
        Assert(snap.OperatingSystem.StartsWith("Microsoft Windows") && snap.Build.All(char.IsDigit), "Nome e build do Windows lidos do registro");
        Assert(int.Parse(snap.Build) < 22000 || !snap.OperatingSystem.Contains("Windows 10"), "Windows 11 (e Server 2022+) não aparece como Windows 10");
        Assert(snap.Processor != "Não disponível" && snap.MemoryGb > 0 && snap.StorageGb > 0 && snap.FreeGb > 0 && snap.FreeGb <= snap.StorageGb, "Processador, memória e disco lidos sem WMI");
        Assert(snap.BootTime is { } boot && boot < DateTime.Now && boot > DateTime.Now.AddYears(-1), "Horário de inicialização coerente");
        Assert(watch.ElapsedMilliseconds < 500, "Leitura do sistema leva menos de meio segundo");
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
        var arguments = PowerModeService.ShortcutArguments(@"C:\Program Files\Qrztweaks\PQueirozOptimizer.exe");
        Assert(arguments.Contains("__COMPAT_LAYER=RunAsInvoker") && arguments.EndsWith("\"C:\\Program Files\\Qrztweaks\\PQueirozOptimizer.exe\" --power-mode"), "Atalho abre o seletor sem pedir administrador");
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

    /// <summary>BIOS Advisor nas telas de teste: perfil fixo (TUF B460M-PLUS) e verificação de BIOS simulada, sem WMI nem internet.</summary>
    static void PrepareBiosAdvisor(MainWindow window)
    {
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var advisor = (PQueirozOptimizer.BiosAdvisor.BiosAdvisorService)typeof(MainWindow).GetProperty("Advisor", flags)!.GetValue(window)!;
        advisor.UseProfile(BiosAdvisorTests.TufB460());
        // Sem internet nas telas de teste: o banco usado é o embutido
        typeof(MainWindow).GetField("_advisorDatabaseChecked", flags)!.SetValue(window, true);
        typeof(MainWindow).GetField("_biosUpdateCheck", flags)!.SetValue(window, Task.FromResult(new PQueirozOptimizer.BiosAdvisor.Updates.BiosUpdateResult(
            PQueirozOptimizer.BiosAdvisor.Updates.BiosUpdateStatus.UpdateAvailable, "0708", "2003", new DateTime(2026, 3, 16), "https://www.asus.com/supportonly/tuf%20gaming%20b460m-plus/helpdesk_bios/", "asus.com")));
    }

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            var root = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/verification");
            Directory.CreateDirectory(root);
            if (args.Contains("--recovery")) { RecoveryTests.Run(); return 0; }
            // Fotos e vídeo do site (não rodam os testes)
            if (args.Contains("--shots")) return Media.Shots(root);
            if (args.Contains("--tour")) return Media.Tour(root);
            if (args.Contains("--switches")) return Switches();
            if (args.Contains("--videoshots")) return Media.VideoShots(root);
            RecoveryTests.Run();
            var log = new ActivityLog(Path.Combine(root, "verification.log"));
            BiosAdvisorTests.Run();
            if (args.Contains("--bios-advisor-live")) return BiosAdvisorTests.Live(log);
            if (args.Contains("--bios-advisor")) return 0;
            var bridge = new PowerShellBridge(log);
            // Algumas etapas só aparecem quando se aplicam ao PC (desktop, GPU AMD, Windows 11 24H2...)
            int If(string requirement) => SystemConditions.Satisfies(requirement) ? 1 : 0;
            Assert(bridge.GetSteps("padrao").Count == 7 + If("desktop"), "Plano padrão contém as etapas atuais (+ hibernação em desktop)");
            Assert(bridge.GetSteps("gamer").Count == 14 + If("win11") + If("desktop") + If("amd"), "Plano avançado contém Qrz e as políticas preservadas (+ Windows 11, Power Throttling e ULPS quando aplicáveis)");
            Assert(PowerShellBridge.ClassifyRisk("@{ Nome = \"X\"; Risco = \"alto\"; Acao = {") == StepRisk.High && PowerShellBridge.ClassifyRisk("{\n    # risco: moderado\n") == StepRisk.Moderate && PowerShellBridge.ClassifyRisk("{ ipconfig /flushdns }") == StepRisk.Safe, "Risco lido da tabela, do comentário ou seguro por padrão");
            Assert(!bridge.GetSteps("gamer").Any(s => s.Name.Contains("MSI")), "Modo MSI não é aplicado automaticamente");
            Assert(SystemConditions.Satisfies(null) && SystemConditions.Satisfies("desconhecida") && SystemConditions.Satisfies("win10") != SystemConditions.Satisfies("win11"), "Condições: vazia e desconhecida liberam; Windows 10 e 11 se excluem");
            var keepServices = bridge.GetSteps("gamerservicos");
            var gamerNames = bridge.GetSteps("gamer").Select(s => s.Name).ToHashSet();
            Console.WriteLine($"  Sem parar serviços: {keepServices.Count} de {gamerNames.Count} etapas (sem: {string.Join(", ", gamerNames.Except(keepServices.Select(s => s.Name)))})");
            Assert(keepServices.SequenceEqual(bridge.GetSteps("gamer")), "Plano sem parar serviços não muda etapas sem ações de serviço");
            Assert(UpdateService.ReleaseNotes("## Novidades\n\n- Um\n* Dois\nTexto solto\n**Full Changelog**: x\n- Full Changelog: y").SequenceEqual(new[] { "Um", "Dois" }), "Novidades da release lidas só dos itens de lista");
            Assert(!UpdateService.CanAutoInstall(new UpdateInfo(true, "1.0.0", "1.1.0", null, "https://github.com/PQueirozDev/Optimizer/releases/download/v1.1.0/Setup.exe", "Setup.exe", null)), "Atualização sem hash publicado não é instalada automaticamente");
            Assert(new LicenseService().MachineId.Length == 20, "ID do computador gerado");
            Assert(new LicenseService().DisplayMachineId.Replace("-", "") == new LicenseService().MachineId, "ID exibido em blocos equivale ao ID real");
            var sampleKey = "PQO1-eyJQcm9kdWN0IjoiUFEifQ." + new string('A', 342);
            Assert(LicenseService.ExtractKey($"Sua chave:\n{sampleKey[..60]}\n{sampleKey[60..]}\nObrigado!") == sampleKey, "Chave extraída de mensagem com quebras de linha");
            Assert(LicenseService.ExtractKey("sem chave aqui") is null, "Texto sem chave é ignorado");
            Assert(!new LicenseService().TryActivate(sampleKey, out _, out _), "Chave com assinatura falsa é recusada");
            Assert(bridge.GetSteps("debloat").Count == 15 + If("win11") + 2 * If("win11-24h2") + If("win11-pre24h2"), "Plano debloat contém as etapas atuais e condições do Windows");
            // O aviso da tela de revisão vem do código da etapa, não de palavras do nome
            StepEffect Effect(string operation, string step) => bridge.GetSteps(operation).First(s => s.Name == step).Effect;
            Assert(Effect("debloat", "Pesquisa na web removida do menu Iniciar") == StepEffect.Backup, "Ajuste de registro com backup não é marcado como irreversível pelo nome");
            Assert(Effect("debloat", "Cortana removida") == StepEffect.Irreversible && Effect("debloat", "OneDrive removido") == StepEffect.Irreversible, "Remoção de apps marcada como não reversível");
            Assert(Effect("padrao", "Limpando cache DNS") == StepEffect.OneOff && Effect("gamer", "Otimizando unidades de disco") == StepEffect.OneOff, "Cache DNS e TRIM são ações pontuais");
            Assert(Effect("padrao", "Executando limpeza de disco (cleanmgr)") == StepEffect.Irreversible && (If("desktop") == 0 || Effect("padrao", "Plano de energia Qrz") == StepEffect.Backup), "Limpeza de disco não reversível e plano de energia com backup");
            Assert(If("desktop") == 0 || new[] { "padrao", "gamer", "inteligente" }.All(op => bridge.GetSteps(op).Any(s => s.Name == "Plano de energia Qrz")), "Otimizações usam o plano de energia Qrz");
            Assert(new[] { "padrao", "gamer", "gamerservicos", "inteligente" }.All(op => !bridge.GetSteps(op).Any(s => s.Name.Contains("Desempenho Maximo") || s.Name.Contains("Alto Desempenho"))), "Nenhuma otimização ativa Desempenho Máximo ou Alto Desempenho");
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
            Assert(MainWindow.ParseLiveMarker("[PLANO] {\"Etapas\":[\"A\",\"B\"],\"Atividade\":\"Otimizacao Padrao\"}") is { Kind: "plan", Activity: "Otimizacao Padrao", Steps: ["A", "B"] }, "Plano de etapas lido");
            Assert(MainWindow.ParseLiveMarker("[PLANO] {\"Etapas\":\"Só uma\",\"Atividade\":\"X\"}") is { Steps: ["Só uma"] }, "Plano com uma etapa (PowerShell desembrulha a lista)");
            Assert(MainWindow.ParseLiveMarker("[ETAPA] Limpando cache DNS") is { Kind: "step", Steps: ["Limpando cache DNS"] }, "Início de etapa lido");
            Assert(MainWindow.ParseLiveMarker("[OK] Limpando cache DNS") is null && MainWindow.ParseLiveMarker("[PLANO] {quebrado") is null, "Linhas comuns e plano inválido ignorados");
            Assert(Translator.Tr("3 de 12 etapas · faltam 9") == "3 of 12 steps · 9 left", "Progresso das etapas traduzido");
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
            // Níveis: Base < Intermediário < completo; Mensal, Personalizado e chaves sem plano continuam com tudo
            LicenseInfo Plan(string? plan, string role = "Standard") => new("A", plan == "Vitalício" ? null : DateTime.UtcNow.AddDays(30), "X", role, plan);
            Assert(Plan("Base").Tier == PlanTier.Base && Plan("Intermediário").Tier == PlanTier.Intermediate && Plan("Avançado").Tier == PlanTier.Full
                && Plan("Vitalício").Tier == PlanTier.Full && Plan("Mensal").Tier == PlanTier.Full && Plan("Personalizado").Tier == PlanTier.Full
                && Plan(null).Tier == PlanTier.Full && Plan("Inventado").Tier == PlanTier.Base && Plan("Base", "Admin").Tier == PlanTier.Full, "Nível liberado por plano");
            Assert(PlanAccess.Allows(Plan("Base"), "optimization") && !PlanAccess.Allows(Plan("Base"), "services") && !PlanAccess.Allows(Plan("Base"), "gaming")
                && PlanAccess.Allows(Plan("Intermediário"), "services") && !PlanAccess.Allows(Plan("Intermediário"), "customize")
                && PlanAccess.Allows(Plan("Avançado"), "bios") && PlanAccess.Allows(Plan("Avançado"), PlanAccess.PowerMode) && !PlanAccess.Allows(Plan("Intermediário"), PlanAccess.PowerMode)
                && new[] { "dashboard", "history", "restore", "settings", "patchnotes", "about" }.All(p => PlanAccess.Allows(Plan("Base"), p))
                && !PlanAccess.Allows(null, "dashboard"), "Páginas liberadas por plano (reversão sempre disponível)");
            Assert(PlanAccess.Allows(Plan("Base"), PlanAccess.OperationPage("padrao")) && PlanAccess.Allows(Plan("Base"), PlanAccess.OperationPage("quickclean"))
                && !PlanAccess.Allows(Plan("Base"), PlanAccess.OperationPage("debloat")) && PlanAccess.Allows(Plan("Intermediário"), PlanAccess.OperationPage("debloat"))
                && !PlanAccess.Allows(Plan("Intermediário"), PlanAccess.OperationPage("gamer")) && !PlanAccess.Allows(Plan("Intermediário"), PlanAccess.OperationPage("inteligente"))
                && !PlanAccess.Allows(Plan("Base"), PlanAccess.OperationPage("gamerservicos"))
                && PlanAccess.Allows(Plan("Avançado"), PlanAccess.OperationPage("gamer")), "Operações em lote seguem o plano dos ajustes que aplicam");
            Assert(Plan("Base").Upgrades.Select(u => u.Name).SequenceEqual(new[] { "Intermediário", "Avançado", "Vitalício" })
                && Plan("Avançado").Upgrades.Select(u => u.Name).SequenceEqual(new[] { "Vitalício" })
                && Plan("Mensal").Upgrades.Select(u => u.Name).SequenceEqual(new[] { "Vitalício" }) && !Plan("Vitalício").Upgrades.Any(), "Upgrades oferecidos por plano");
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
                // Tutoriais: cada passo desenhado de verdade (o destaque mede o alvo na tela) e traduzido.
                // Encerra sem chamar EndTutorial, que gravaria o progresso nas preferências deste PC.
                var tutorialLayer = (System.Windows.Controls.Grid)typeof(MainWindow).GetField("TutorialLayer", flags)!.GetValue(window)!;
                void WalkTutorial(string id, string? page, System.Collections.IList steps)
                {
                    if (page != null) { typeof(MainWindow).GetMethod("NavigateTo", flags)!.Invoke(window, new object[] { page }); Wait(900); }
                    // O tutorial automático da página pode já ter aberto: recomeça do zero
                    typeof(MainWindow).GetField("_tutorialSteps", flags)!.SetValue(window, null);
                    var visible = (System.Collections.IList)Activator.CreateInstance(steps.GetType())!;
                    foreach (var s in steps)
                        if (page is null || s!.GetType().GetProperty("Target")!.GetValue(s) is Func<FrameworkElement?> t && t() != null) visible.Add(s);
                    typeof(MainWindow).GetMethod("StartTutorial", flags)!.Invoke(window, new object[] { id, visible });
                    for (var i = 0; i < visible.Count; i++)
                    {
                        typeof(MainWindow).GetField("_tutorialIndex", flags)!.SetValue(window, i);
                        typeof(MainWindow).GetMethod("ShowTutorialStep", flags)!.Invoke(window, new object[] { false });
                        Wait(120); Collect(tutorialLayer);
                        if (page is null && i == 2) Snap("en-tutorial-step");
                    }
                    typeof(MainWindow).GetField("_tutorialSteps", flags)!.SetValue(window, null);
                    tutorialLayer.Visibility = Visibility.Collapsed; tutorialLayer.Children.Clear();
                    Console.WriteLine($"PASS Tutorial {id}: {visible.Count} passos desenhados sem erro");
                }
                WalkTutorial("inicio", null, (System.Collections.IList)typeof(MainWindow).GetMethod("WelcomeTour", flags)!.Invoke(window, null)!);
                foreach (var page in new[] { "resources", "gaming", "network", "services", "bios" })
                    if (typeof(MainWindow).GetMethod("PageTutorial", flags)!.Invoke(window, new object[] { page }) is System.Collections.IList pageSteps) WalkTutorial("pagina-" + page, page, pageSteps);
                PrepareBiosAdvisor(window);
                foreach (var page in new[] { "dashboard", "optimization", "startup", "drivers", "tools", "gaming", "network", "restore", "resources", "fixes", "services", "apps", "history", "settings", "about", "patchnotes", "bios", "biosadvisor" })
                {
                    typeof(MainWindow).GetMethod("NavigateTo", flags)!.Invoke(window, new object[] { page });
                    Wait(page == "dashboard" ? 4000 : page == "startup" ? 8000 : 600);
                    Collect(window); Snap("en-" + page);
                }
                typeof(MainWindow).GetField("_servicesTab", flags)!.SetValue(window, 1);
                typeof(MainWindow).GetMethod("NavigateTo", flags)!.Invoke(window, new object[] { "services" });
                Wait(800); Collect(window); Snap("en-services-status");
                typeof(MainWindow).GetField("_servicesTab", flags)!.SetValue(window, 0);
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
                PrepareBiosAdvisor(window);
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
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
                // Tela de abertura (montada aqui na thread do teste, sem exibir)
                void RenderSplash(string name)
                {
                    var palette = StartupSplash.CurrentPalette() with { Animations = false };
                    var splash = (Window)typeof(StartupSplash).GetMethod("Build", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { palette, "1.8.1", "Montando a interface..." })!;
                    typeof(StartupSplash).GetMethod("SetRing", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { 0.6 });
                    var content = (FrameworkElement)splash.Content;
                    var host = new System.Windows.Controls.Grid { Width = 540, Height = 360 };
                    splash.Content = null; host.Children.Add(content);
                    host.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty, "BackgroundBrush");
                    host.Measure(new Size(540, 360)); host.Arrange(new Rect(0, 0, 540, 360)); host.UpdateLayout();
                    var bitmap = new RenderTargetBitmap(540, 360, 96, 96, PixelFormats.Pbgra32); bitmap.Render(host);
                    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(root, name + ".png")); png.Save(stream);
                    Console.WriteLine("RENDER " + name);
                }
                RenderSplash("splash-dark");
                Render("dashboard-dark", 1320, 860);
                // Janela de atualização com uma versão simulada (notas iguais às da release)
                var fakeUpdate = new UpdateInfo(true, "1.8.2", "1.8.3", "https://github.com/PQueirozDev/Optimizer/releases", "https://x/setup.exe", "setup.exe", "https://x/SHA256SUMS.txt",
                    UpdateService.ReleaseNotes("## Novidades\n\n- Corrigido o fechamento do app ao ligar grupos em Serviços.\n- Nova aba Estado dos serviços.\n- 4 temas novos e cores personalizadas.\n\n**Full Changelog**: x"));
                typeof(MainWindow).GetMethod("ShowUpdateDialog", flags)!.Invoke(window, new object[] { fakeUpdate });
                Render("update-dialog-dark", 1320, 860);
                typeof(MainWindow).GetMethod("CloseUpdateDialog", flags)!.Invoke(window, null);
                typeof(MainWindow).GetMethod("NavigateTo", flags)!.Invoke(window, new object[] { "optimization" });
                Render("optimizations-dark", 1060, 700);
                Pump((Task)typeof(MainWindow).GetMethod("PrepareOperationAsync", flags)!.Invoke(window, new object[] { "gamer" })!);
                Render("review-dark", 1060, 700);
                typeof(MainWindow).GetMethod("NavigateTo", flags)!.Invoke(window, new object[] { "history" });
                Render("history-dark", 1320, 860);
                foreach (var page in new[] { "drivers", "startup", "tools", "gaming", "network", "restore", "resources", "fixes", "services", "apps", "settings", "about", "patchnotes", "bios", "biosadvisor" })
                {
                    typeof(MainWindow).GetMethod("NavigateTo", flags)!.Invoke(window, new object[] { page });
                    Render(page + "-dark", 1320, 860);
                }
                typeof(MainWindow).GetField("_servicesTab", flags)!.SetValue(window, 1);
                typeof(MainWindow).GetMethod("NavigateTo", flags)!.Invoke(window, new object[] { "services" });
                Render("services-status-dark", 1320, 860);
                typeof(MainWindow).GetField("_servicesTab", flags)!.SetValue(window, 0);
                foreach (var tab in new[] { 1, 2, 3 })
                {
                    typeof(MainWindow).GetField("_gamingTab", flags)!.SetValue(window, tab);
                    typeof(MainWindow).GetMethod("NavigateTo", flags)!.Invoke(window, new object[] { "gaming" });
                    Render($"gaming-tab{tab}-dark", 1320, 860);
                }
                typeof(MainWindow).GetField("_gamingTab", flags)!.SetValue(window, 1);
                typeof(MainWindow).GetMethod("NavigateTo", flags)!.Invoke(window, new object[] { "gaming" });
                Render("gaming-games-tall", 1320, 2000);
                typeof(MainWindow).GetField("_gamingTab", flags)!.SetValue(window, 0);
                // BIOS Advisor inteiro e o guia "Ver como configurar" (perfil fixo de teste)
                typeof(MainWindow).GetMethod("NavigateTo", flags)!.Invoke(window, new object[] { "biosadvisor" });
                Render("biosadvisor-tall", 1320, 4600);
                var advisorReport = typeof(MainWindow).GetField("_advisorReport", flags)!.GetValue(window)!;
                var memProfile = ((PQueirozOptimizer.BiosAdvisor.AdvisorReport)advisorReport).Recommendations.First(r => r.Id == "mem-profile");
                typeof(MainWindow).GetMethod("ShowAdvisorGuide", flags)!.Invoke(window, new object[] { advisorReport, memProfile, false });
                Render("biosadvisor-guide", 1320, 860);
                typeof(MainWindow).GetMethod("CloseAdvisorGuide", flags)!.Invoke(window, null);
                // Efeitos visuais (fim de Personalizar Windows) e a tela de upgrade de um plano Base
                typeof(MainWindow).GetMethod("NavigateTo", flags)!.Invoke(window, new object[] { "customize" });
                Render("customize-tall", 1320, 3600);
                ((App)Application.Current!).SetActiveLicense(new LicenseInfo("Teste", DateTime.UtcNow.AddDays(30), "TESTE", "Standard", LicensePlans.Base));
                typeof(MainWindow).GetMethod("NavigateTo", flags)!.Invoke(window, new object[] { "gaming" });
                Render("locked-gaming-dark", 1320, 860);
                ((App)Application.Current!).SetActiveLicense(new LicenseInfo("Teste", null, "TESTE", "Standard", LicensePlans.Lifetime));
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
                Variant("ocean-orange", new() { Theme = PQueirozOptimizer.Models.ThemeMode.Ocean, AccentColor = "#F97316", SecondaryColor = "#FACC15" }, 1320, 860, "settings", "dashboard");
                Variant("graphite-green", new() { Theme = PQueirozOptimizer.Models.ThemeMode.Graphite, AccentColor = "#10B981" }, 1320, 860, "dashboard");
                Variant("light-blue", new() { Theme = PQueirozOptimizer.Models.ThemeMode.Light, AccentColor = "#3B82F6" }, 1320, 860, "dashboard");
                Variant("forest", new() { Theme = PQueirozOptimizer.Models.ThemeMode.Forest }, 1320, 860, "dashboard");
                PQueirozOptimizer.Services.AppearanceService.Apply(PQueirozOptimizer.Services.AppearanceService.Defaults());
                typeof(MainWindow).GetMethod("ApplyTheme", flags)!.Invoke(window, new object[] { false, false });
                typeof(MainWindow).GetMethod("NavigateTo", flags)!.Invoke(window, new object[] { "dashboard" });
                Pump((Task)typeof(MainWindow).GetMethod("RenderDashboardAsync", flags)!.Invoke(window, null)!);
                Render("dashboard-light", 1320, 860);
                RenderSplash("splash-light");
                typeof(MainWindow).GetMethod("NavigateTo", flags)!.Invoke(window, new object[] { "optimization" });
                Render("optimizations-light", 1320, 860);
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}

