using System.Windows;
using System.Windows.Controls;
using PQueirozOptimizer.Engine;

namespace PQueirozOptimizer;

/// <summary>
/// Fase 14: depois de uma execução, cada ajuste que o catálogo sabe ler é conferido no Windows. A tela ao vivo só
/// diz "confirmado" quando o estado mudou de verdade; o resto aparece como não confirmado, com o motivo.
/// </summary>
public partial class MainWindow
{
    private async Task AppendLiveVerificationAsync(Panel root, string operation, IReadOnlyList<string> steps)
    {
        var ids = TweakCatalog.All.Where(t => t.Operation == operation && steps.Contains(t.Step) && !t.OneOff).Select(t => t.Id).ToList();
        if (ids.Count == 0) return;
        List<(TweakDefinition Tweak, bool Verified, string Detail)> results;
        try { results = SmartOptimizer.Verify(ids, MachineReader.QuickContext(await LiveSystemState.LoadAsync())); }
        catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException or UnauthorizedAccessException) { return; }
        var panel = new StackPanel();
        var ok = results.Count(r => r.Verified);
        panel.Children.Add(SectionHeader($"Verificação: {ok} de {results.Count} confirmados", "Estado lido no Windows depois da execução. Ajustes de ação pontual (limpeza, TRIM) não entram aqui."));
        foreach (var (tweak, verified, detail) in results)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var chip = IconChip(verified ? Glyphs.Check : Glyphs.Warning, verified ? "Success" : "Warning", 26); DockPanel.SetDock(chip, Dock.Left); row.Children.Add(chip);
            var text = Label((verified ? "Confirmado · " : "Não confirmado · ") + tweak.Name + " — " + detail, 12.5, !verified);
            text.Margin = new Thickness(10, 3, 0, 0);
            row.Children.Add(text);
            panel.Children.Add(row);
        }
        // Logo acima dos botões de ação do fim da tela
        var index = Math.Max(0, root.Children.Count - 1);
        root.Children.Insert(index, Surface(panel));
    }
}
