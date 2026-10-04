using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>Win32 Priority (Modo Jogo → Sistema).</summary>
public partial class MainWindow
{
    // ================= Win32 Priority (Modo Jogo → Sistema) =================
    private Border Win32PriorityCard()
    {
        var panel = new StackPanel();
        var service = new Win32PriorityService(_log);
        var current = Win32PriorityService.Current();
        var head = new DockPanel();
        if (service.IsChanged)
        {
            var restore = IconButton(Glyphs.Undo, "Restaurar original");
            restore.VerticalAlignment = VerticalAlignment.Top;
            restore.Click += (_, _) => { try { service.Restore(); ShowGaming(); } catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException) { ShowToast("Win32 Priority", ex.Message, "Danger"); } };
            DockPanel.SetDock(restore, Dock.Right); head.Children.Add(restore);
        }
        head.Children.Add(SectionHeader("Win32 Priority", $"Quanto o Windows favorece o programa em foco (o jogo). Valor atual: 0x{current:X2}. Vale na hora, sem reiniciar."));
        panel.Children.Add(head);
        var grid = Responsive(new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -10, 0) }, 260, 3);
        foreach (var level in Win32PriorityService.Levels)
        {
            var selected = level.Value == current;
            var tile = new Button { HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 10, 10) };
            var stack = new StackPanel();
            var top = new DockPanel();
            var code = Pill($"0x{level.Value:X2}", selected ? "Success" : "Info"); DockPanel.SetDock(code, Dock.Right); top.Children.Add(code);
            ((TextBlock)code.Child).Tag = Translator.SystemDataTag;
            var t = new TextBlock { Text = level.Label, FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            t.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            top.Children.Add(t);
            stack.Children.Add(top);
            var d = new TextBlock { Text = level.Description, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
            d.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            stack.Children.Add(d);
            tile.Content = stack;
            tile.SetResourceReference(Button.BackgroundProperty, selected ? "AccentSoftBrush" : "PanelBrush");
            tile.SetResourceReference(Button.BorderBrushProperty, selected ? "AccentBrush" : "BorderSubtleBrush");
            tile.Click += (_, _) =>
            {
                try { service.Set(level); ShowToast("Win32 Priority", $"{level.Label} aplicado.", "Success"); ShowGaming(); }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or System.Security.SecurityException) { ShowToast("Win32 Priority", ex.Message, "Danger"); }
            };
            grid.Children.Add(tile);
        }
        panel.Children.Add(grid);
        return Surface(panel);
    }
}
