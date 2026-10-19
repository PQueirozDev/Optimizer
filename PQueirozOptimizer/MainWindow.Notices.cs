using System.Windows;
using System.Windows.Controls;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>
/// Avisos da Visão geral que pedem uma ação rápida: ajustes do app que o Windows desfez sozinho e monitor
/// rodando abaixo da taxa de atualização que ele aceita.
/// </summary>
public partial class MainWindow
{
    /// <summary>Lê os dois estados fora da tela (registro e modos do monitor) e mostra só o que precisar de atenção.</summary>
    private async Task FillDashboardNoticesAsync(Panel slot)
    {
        var store = new RegistryTweakStore();
        IReadOnlyList<RegistryTweakStore.AppliedTweak> reset;
        IReadOnlyList<DriftedValue> scriptReset;
        DisplayRefresh? display;
        try
        {
            (reset, scriptReset, display) = await Task.Run(() =>
                (store.ListReset(), ScriptTweakDrift.Find(System.IO.Path.Combine(SnapshotDirectory, "ultima_otimizacao.json")), DisplayService.Primary()));
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Security.SecurityException or System.Text.Json.JsonException)
        {
            _log.Write("WARN", "Avisos do painel: " + ex.Message);
            return;
        }
        // O painel pode ter sido redesenhado (ou trocado de página) durante a leitura
        if (_currentPage != "dashboard" || slot.Parent is not Panel root || !ContentHost.Children.Contains(root)) return;
        slot.Children.Clear();
        if (reset.Count > 0 || scriptReset.Count > 0) slot.Children.Add(ResetTweaksBanner(store, reset, scriptReset, slot));
        if (display is { BelowMax: true }) slot.Children.Add(RefreshRateBanner(display, slot));

        // A hora do servidor chega com a conferência da licença, logo depois de abrir: espera um pouco por ela
        var reading = ClockCheck.FirstReadingAsync;
        if (await Task.WhenAny(reading, Task.Delay(12000)) != reading) return;
        if (ClockCheck.IsOff(ClockCheck.Offset) && _currentPage == "dashboard" && slot.Parent is Panel again && ContentHost.Children.Contains(again))
            slot.Children.Add(ClockBanner(ClockCheck.Offset!.Value, slot));
    }

    private Border ClockBanner(TimeSpan offset, Panel slot)
    {
        var sync = IconButton(Glyphs.Clock, "Sincronizar agora", primary: true);
        Border? card = null;
        sync.Click += async (_, _) =>
        {
            sync.IsEnabled = false;
            if (await Network.SyncTimeAsync())
            {
                ClockCheck.Clear();
                ShowToast("Relógio sincronizado", "A hora do Windows foi acertada pela internet.");
                slot.Children.Remove(card);
            }
            else
            {
                ShowToast("Não foi possível sincronizar", "Confira em Configurações → Hora e idioma → Data e hora se \"Definir horário automaticamente\" está ligado.", "Warning");
                sync.IsEnabled = true;
            }
        };
        card = NoticeBanner(Glyphs.Clock, "Warning", $"O relógio do Windows está {ClockCheck.Describe(offset)}",
            "Relógio errado faz launchers e jogos recusarem o login, quebra sites com HTTPS e atrapalha o Windows Update.", sync);
        return card;
    }

    private Border NoticeBanner(string glyph, string tone, string title, string detail, params Button[] actions)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        foreach (var action in actions) buttons.Children.Add(action);
        var banner = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right); banner.Children.Add(buttons);
        var chip = IconChip(glyph, tone, 40); DockPanel.SetDock(chip, Dock.Left); banner.Children.Add(chip);
        var text = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var head = Label(title, 14); head.FontWeight = FontWeights.SemiBold; head.Margin = new Thickness(0);
        text.Children.Add(head);
        var sub = Label(detail, 12, true); sub.Margin = new Thickness(0, 2, 0, 0);
        text.Children.Add(sub);
        banner.Children.Add(text);
        var card = Surface(banner); card.Padding = new Thickness(18, 14, 18, 14);
        card.SetResourceReference(Border.BorderBrushProperty, tone + "Brush");
        return card;
    }

    private Border ResetTweaksBanner(RegistryTweakStore store, IReadOnlyList<RegistryTweakStore.AppliedTweak> reset, IReadOnlyList<DriftedValue> scriptReset, Panel slot)
    {
        // Um nome por ajuste: os do app pelo título, os das otimizações pela etapa que gravou o valor
        var titles = reset.Select(t => t.Title).Concat(scriptReset.Select(v => v.Step.Length > 0 ? v.Step : v.Name)).Distinct().ToList();
        var names = string.Join(", ", titles.Take(3)) + (titles.Count > 3 ? $" e mais {titles.Count - 3}" : "");
        var reapply = IconButton(Glyphs.Refresh, "Reaplicar", primary: true);
        Border? card = null;
        reapply.Click += async (_, _) =>
        {
            reapply.IsEnabled = false;
            // Valores das otimizações: o script regrava, conferindo a mesma lista permitida do Reverter
            if (scriptReset.Count > 0 && !await ExecuteTrackedAsync("Reaplicando ajustes desfeitos", token => _powershell.RunAsync("reaplicar", null, null, token)))
            {
                reapply.IsEnabled = true;
                return;
            }
            var failed = new List<string>();
            await Task.Run(() =>
            {
                foreach (var tweak in reset)
                {
                    try { store.Reapply(tweak.Id); }
                    catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.IO.IOException or System.Text.Json.JsonException or System.Security.SecurityException)
                    { failed.Add($"{tweak.Title}: {ex.Message}"); }
                }
            });
            foreach (var failure in failed) _log.Write("WARN", "Reaplicar ajuste: " + failure);
            if (reset.Count > 0) _log.Write(failed.Count == 0 ? "SUCCESS" : "WARN", $"Ajustes desfeitos pelo Windows reaplicados: {reset.Count - failed.Count} de {reset.Count}");
            if (failed.Count == 0) { ShowToast("Ajustes reaplicados", "Os valores do app voltaram ao registro."); slot.Children.Remove(card); }
            else { ShowToast("Alguns ajustes não voltaram", failed[0], "Warning"); reapply.IsEnabled = true; }
        };
        card = NoticeBanner(Glyphs.Undo, "Warning",
            titles.Count == 1 ? "O Windows desfez 1 ajuste do app" : $"O Windows desfez {titles.Count} ajustes do app",
            $"Uma atualização ou outro programa trocou os valores de: {names}. Reaplique para voltar ao que você tinha escolhido.", reapply);
        return card;
    }

    private Border RefreshRateBanner(DisplayRefresh display, Panel slot)
    {
        var apply = IconButton(Glyphs.Monitor, $"Usar {display.MaxHz} Hz", primary: true);
        Border? card = null;
        apply.Click += (_, _) =>
        {
            try
            {
                DisplayService.ApplyMax(display);
                _log.Write("SUCCESS", $"Monitor principal: {display.CurrentHz} Hz → {display.MaxHz} Hz ({display.Width}x{display.Height})");
                ShowToast("Taxa de atualização ajustada", $"O monitor agora roda a {display.MaxHz} Hz.");
                slot.Children.Remove(card);
            }
            catch (InvalidOperationException ex) { ShowToast("Não foi possível alterar", ex.Message, "Warning"); }
        };
        card = NoticeBanner(Glyphs.Monitor, "Info", $"Seu monitor está em {display.CurrentHz} Hz, mas aceita {display.MaxHz} Hz",
            $"Em {display.Width}x{display.Height}, a taxa maior deixa o movimento mais fluido e reduz o atraso nos jogos. Para voltar: Configurações → Sistema → Tela → Tela avançada.", apply);
        return card;
    }
}
