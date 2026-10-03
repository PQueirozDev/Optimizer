using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>Otimização completa: o passo a passo da Visão geral.</summary>
public partial class MainWindow
{
    // ================= Otimização completa (Visão geral) =================
    /// <summary>Passo a passo das otimizações recomendadas, na ordem certa (como o "Fix All" do Paragon).</summary>
    private Border FixAllCard()
    {
        var panel = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var pill = Pill("6 passos", "Accent"); DockPanel.SetDock(pill, Dock.Right); pill.VerticalAlignment = VerticalAlignment.Top; head.Children.Add(pill);
        var titles = new StackPanel();
        var t = Label("Otimização completa", 16); t.FontWeight = FontWeights.SemiBold; t.Margin = new Thickness(0, 0, 0, 2); titles.Children.Add(t);
        var d = Label("Siga na ordem para revisar, aplicar e conferir as otimizações recomendadas.", 12, true); d.Margin = new Thickness(0); titles.Children.Add(d);
        head.Children.Add(titles);
        panel.Children.Add(head);

        var steps = new (string Glyph, string Title, string Detail, Action Run)[]
        {
            (Glyphs.Broom, "Limpeza", "Arquivos temporários seguros", () => _ = PrepareOperationAsync("quickclean")),
            (Glyphs.Monitor, "Driver de vídeo", "Instalação limpa recomendada", () => NavigateTo("drivers")),
            (Glyphs.Lightning, "Ajustes recomendados", "Revisados item por item", () => _ = PrepareOperationAsync("padrao")),
            (Glyphs.Services, "Serviços", "Segundo plano mais leve", () => NavigateTo("services")),
            (Glyphs.Game, "Perfis de jogos", "Presets dos jogos instalados", () => OpenGamingTab(1)),
            (Glyphs.Chip, "BIOS", "Grupos de ajustes da placa", () => NavigateTo("bios")),
        };
        var grid = Responsive(new UniformGrid { Columns = 6, Margin = new Thickness(0, 0, -10, 0) }, 150, 6);
        for (var i = 0; i < steps.Length; i++)
        {
            var (glyph, title, detail, run) = steps[i];
            var stack = new StackPanel();
            var top = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var number = new TextBlock { Text = (i + 1).ToString("00"), FontSize = 11, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Tag = Translator.SystemDataTag };
            number.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            DockPanel.SetDock(number, Dock.Right); top.Children.Add(number);
            top.Children.Add(IconChip(glyph, "Accent", 30));
            stack.Children.Add(top);
            var st = new TextBlock { Text = title, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            st.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            var sd = new TextBlock { Text = detail, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            sd.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            stack.Children.Add(st); stack.Children.Add(sd);
            var tile = new Button { Content = stack, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(12, 12, 12, 12), Margin = new Thickness(0, 0, 10, 0), ToolTip = title };
            tile.SetResourceReference(Button.BackgroundProperty, "PanelBrush");
            tile.SetResourceReference(Button.BorderBrushProperty, "BorderSubtleBrush");
            tile.Click += (_, _) => run();
            grid.Children.Add(tile);
        }
        panel.Children.Add(grid);
        return Surface(panel);
    }
}
