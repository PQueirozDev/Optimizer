using System.IO;
using System.Windows;
using System.Windows.Controls;
using PQueirozOptimizer.BiosAdvisor;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>
/// BIOS Advisor: leitura dos valores reais da BIOS pelo SCEWIN (export, só leitura) e o banco de perfis atualizável.
/// </summary>
public partial class MainWindow
{
    private bool _advisorDatabaseChecked;

    private Border AdvisorBiosReadCard(AdvisorReport report)
    {
        var profile = report.Profile;
        var tool = BiosService.ToolPath();
        var readings = Advisor.ReadingsFor(profile);
        var stale = readings is null && Advisor.State.Readings != null;
        var (status, tone) = tool is null ? (T("SCEWIN não configurado", "SCEWIN not set up"), "Warning")
            : readings != null ? (T($"{readings.Values.Count} configurações lidas", $"{readings.Values.Count} settings read"), "Success")
            : (T("Ainda não lida", "Not read yet"), "Info");
        var panel = new StackPanel();
        panel.Children.Add(FeatureHeader(Glyphs.Search, "Accent", T("Ler os valores reais da BIOS", "Read the actual BIOS values"),
            T("Com o SCEWIN (ferramenta da AMI, usada pela maioria das placas ASUS, MSI, Gigabyte e ASRock) o app lê o valor atual de cada opção e os itens passam de \"precisa verificar\" para \"lido da BIOS\". É só leitura: nada é gravado. O SCEWIN não vem com o app; aponte o SCEWIN_64.exe (com o amifldrv64.sys na mesma pasta).",
              "With SCEWIN (AMI's tool, used by most ASUS, MSI, Gigabyte and ASRock boards) the app reads the current value of each option, and items move from \"needs check\" to \"read from BIOS\". It's read-only: nothing is written. SCEWIN isn't bundled; point to SCEWIN_64.exe (with amifldrv64.sys in the same folder)."),
            status, tone));

        if (readings != null)
        {
            var info = Label(T($"Leitura de {readings.ReadAt:dd/MM/yyyy HH:mm}: {readings.Values.Count} das configurações do Advisor reconhecidas entre {readings.TotalQuestions} da BIOS. Se você mudou algo na BIOS depois disso, leia de novo.",
                $"Read on {readings.ReadAt:yyyy-MM-dd HH:mm}: {readings.Values.Count} Advisor settings recognized among {readings.TotalQuestions} in the BIOS. If you changed something in the BIOS since then, read again."), 12, true);
            info.Margin = new Thickness(0, 12, 0, 0);
            panel.Children.Add(info);
        }
        else if (stale)
            panel.Children.Add(Notice(T("A leitura salva é de outra versão de BIOS ou de outra placa e foi descartada. Leia de novo.", "The saved reading is from another BIOS version or board and was discarded. Read again."), "Info"));

        var actions = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        if (tool is null)
        {
            var choose = IconButton(Glyphs.Folder, T("Escolher SCEWIN_64.exe", "Choose SCEWIN_64.exe"), primary: true);
            choose.Margin = new Thickness(0, 0, 8, 0);
            choose.Click += (_, _) =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "SCEWIN (SCEWIN_64.exe)|SCEWIN*.exe", Title = T("Escolha o SCEWIN_64.exe (deixe o amifldrv64.sys na mesma pasta)", "Choose SCEWIN_64.exe (keep amifldrv64.sys in the same folder)") };
                if (dialog.ShowDialog(this) != true) return;
                try { BiosService.SetToolPath(dialog.FileName); KeepScroll(ShowBiosAdvisor); }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException) { ShowToast("SCEWIN", ex.Message, "Danger"); }
            };
            actions.Children.Add(choose);
        }
        else
        {
            var read = IconButton(Glyphs.Download, readings is null ? T("Ler valores da BIOS", "Read BIOS values") : T("Ler de novo", "Read again"), primary: readings is null);
            read.Margin = new Thickness(0, 0, 8, 0);
            read.Click += async (_, _) =>
            {
                BiosReadings? result = null;
                var ok = await ExecuteTrackedAsync(T("Lendo a BIOS pelo SCEWIN (só leitura)", "Reading the BIOS via SCEWIN (read-only)"), async _ => result = await Advisor.ReadBiosAsync(Bios, profile));
                if (ok && result != null)
                    ShowToast("BIOS Advisor", T($"{result.Values.Count} configurações reconhecidas na BIOS.", $"{result.Values.Count} settings recognized in the BIOS."), result.Values.Count > 0 ? "Success" : "Warning");
                if (_currentPage == "biosadvisor") KeepScroll(ShowBiosAdvisor);
            };
            actions.Children.Add(read);
        }
        var editor = IconButton(Glyphs.Chip, T("Abrir BIOS / UEFI", "Open BIOS / UEFI"));
        editor.Click += (_, _) => NavigateTo("bios");
        actions.Children.Add(editor);
        panel.Children.Add(actions);

        // Placas que exigem um passo antes de o SCEWIN conseguir ler (ex.: ASUS: Publish HII Resources)
        if (tool != null && readings is null && BiosService.ManufacturerInstructions(BiosService.ReadBoard()) is { } steps)
            panel.Children.Add(Notice(T("Se a leitura falhar: ", "If reading fails: ") + string.Join(" ", steps.Steps.Skip(1)), "Info"));

        var db = BiosDatabase.Current;
        var dbInfo = Label(T($"Banco de perfis v{db.Version} ({(db.Origin == "embutido" ? "embutido no app" : "atualizado pela internet")}, {db.Updated}): {db.Boards.Count} placa(s) com perfil, {db.PathSets.Count} família(s) de BIOS com caminhos confirmados. Placas novas chegam sem precisar atualizar o app.",
            $"Profile database v{db.Version} ({(db.Origin == "embutido" ? "built into the app" : "updated online")}, {db.Updated}): {db.Boards.Count} board profile(s), {db.PathSets.Count} BIOS family(ies) with confirmed paths. New boards arrive without updating the app."), 11.5, true);
        dbInfo.Margin = new Thickness(0, 12, 0, 0);
        panel.Children.Add(dbInfo);
        return Surface(panel);
    }

    /// <summary>Procura um banco mais novo e assinado (uma vez por abertura do app); redesenha se mudou.</summary>
    private async Task RefreshAdvisorDatabaseAsync()
    {
        if (_advisorDatabaseChecked) return;
        _advisorDatabaseChecked = true;
        bool updated;
        try { updated = await Advisor.RefreshDatabaseAsync(); }
        catch (Exception ex) { _log.Write("WARN", "BIOS Advisor: " + ex.GetBaseException().Message); return; }
        if (updated && !_advisorClosed && _currentPage == "biosadvisor" && IsLoaded && AdvisorLayer.Visibility != Visibility.Visible)
        {
            ShowToast("BIOS Advisor", T($"Banco de perfis atualizado para a versão {BiosDatabase.Current.Version}.", $"Profile database updated to version {BiosDatabase.Current.Version}."), "Info");
            KeepScroll(ShowBiosAdvisor);
        }
    }
}
