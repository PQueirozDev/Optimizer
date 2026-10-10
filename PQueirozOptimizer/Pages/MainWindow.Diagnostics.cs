using System.Windows;
using System.Windows.Controls;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>Central de diagnóstico: verifica os componentes mais comuns sem alterar o sistema.</summary>
public partial class MainWindow
{
    private sealed record DiagnosticItem(string Name, string Glyph, string Script, string RepairPage, string? RepairId = null);

    private void ShowDiagnostics()
    {
        PageTitle.Text = "Central de diagnóstico";
        PageBadge.Visibility = Visibility.Visible;
        PageBadgeText.Text = "somente leitura";
        var root = new StackPanel();
        var results = new StackPanel();
        var run = IconButton(Glyphs.Play, "Executar diagnóstico completo", primary: true);
        run.Click += async (_, _) => await RunDiagnosticsAsync(run, results);
        var intro = new DockPanel { Margin = new Thickness(0, 0, 0, AppearanceService.Space(16)) };
        DockPanel.SetDock(run, Dock.Right); intro.Children.Add(run);
        intro.Children.Add(SectionHeader("Diagnóstico do PC", "Verifique sistema, drivers, serviços, rede, Bluetooth e integridade do Windows."));
        root.Children.Add(SmartDiagnosticsPanel());
        root.Children.Add(Surface(intro));
        root.Children.Add(results);
        ContentHost.Children.Clear();
        ContentHost.Children.Add(root);
    }

    private async Task RunDiagnosticsAsync(Button run, Panel results)
    {
        run.IsEnabled = false;
        results.Children.Clear();
        var items = new[]
        {
            new DiagnosticItem("Sistema", Glyphs.Monitor, "$os=(Get-CimInstance Win32_OperatingSystem).Caption; Write-Output $os", "settings"),
            new DiagnosticItem("Drivers", Glyphs.Monitor, "$bad=Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object Status -ne 'OK'; if($bad){Write-Output ('Atenção: '+$bad.Count+' dispositivo(s) com problema')}else{Write-Output 'Todos os dispositivos presentes estão funcionando'}", "drivers"),
            new DiagnosticItem("Serviços essenciais", Glyphs.Services, "$s=Get-Service bthserv,wuauserv,AudioSrv -ErrorAction SilentlyContinue; $bad=$s|Where-Object Status -ne 'Running'; if($bad){Write-Output ('Atenção: '+(($bad|% Name)-join ', ')+' parado(s)')}else{Write-Output 'Serviços essenciais em execução'}", "services"),
            new DiagnosticItem("Rede", Glyphs.Network, "if(Test-Connection 1.1.1.1 -Count 1 -Quiet){'Internet acessível'}else{'Atenção: sem resposta da internet'}", "network"),
            new DiagnosticItem("Bluetooth", Glyphs.Bluetooth, "$a=Get-PnpDevice -Class Bluetooth -PresentOnly -ErrorAction SilentlyContinue|? Status -eq 'OK'; if($a){'Adaptador Bluetooth disponível'}else{'Atenção: adaptador Bluetooth não disponível'}", "fixes", "bluetooth"),
            new DiagnosticItem("Integridade do Windows", Glyphs.Shield, "if((Get-Command sfc.exe -ErrorAction SilentlyContinue)){'Ferramentas SFC e DISM disponíveis'}else{'Não disponível neste Windows'}", "fixes")
        };
        try
        {
            foreach (var item in items)
            {
                var row = DiagnosticRow(item, "Verificando...");
                results.Children.Add(row);
                try
                {
                    var output = await Services.PowerShellBridge.RunScriptAsync(item.Script, timeout: TimeSpan.FromSeconds(25));
                    UpdateDiagnosticRow(row, item, string.IsNullOrWhiteSpace(output) ? "Sem informação retornada." : output);
                }
                catch (Exception ex) { UpdateDiagnosticRow(row, item, "Não foi possível verificar: " + ex.Message, "Danger"); }
            }
        }
        finally { run.IsEnabled = true; }
    }

    private Border DiagnosticRow(DiagnosticItem item, string status)
    {
        var body = new DockPanel();
        var action = IconButton(Glyphs.OpenInNew, "Abrir");
        action.Click += (_, _) => { if (item.RepairId != null) OpenFixById(item.RepairId); else NavigateTo(item.RepairPage); };
        DockPanel.SetDock(action, Dock.Right); body.Children.Add(action);
        body.Children.Add(IconChip(item.Glyph, "Info", 38));
        var text = new StackPanel { Margin = new Thickness(12, 0, 12, 0) };
        text.Children.Add(Label(item.Name, 13.5));
        text.Children.Add(Label(status, 12, true));
        body.Children.Add(text);
        var card = Surface(body); card.Margin = new Thickness(0, 0, 0, 10); card.Tag = item;
        return card;
    }

    private void UpdateDiagnosticRow(Border row, DiagnosticItem item, string status, string? tone = null)
    {
        if (row.Child is not DockPanel body || body.Children.OfType<StackPanel>().FirstOrDefault() is not { } text) return;
        var label = text.Children.OfType<TextBlock>().Skip(1).FirstOrDefault();
        if (label == null) return;
        label.Text = status;
        label.SetResourceReference(TextBlock.ForegroundProperty, tone ?? (status.Contains("Atenção", StringComparison.OrdinalIgnoreCase) ? "WarningBrush" : "SuccessBrush"));
    }

    private void OpenFixById(string id)
    {
        NavigateTo("fixes");
        Dispatcher.BeginInvoke(() =>
        {
            // A página mantém a busca simples; o cartão correspondente fica visível para execução.
            OperationStatus.Text = $"Correção disponível: {FixesService.Fixes.FirstOrDefault(f => f.Id == id)?.Name ?? id}.";
        });
    }
}
