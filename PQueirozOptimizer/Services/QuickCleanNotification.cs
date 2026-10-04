using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace PQueirozOptimizer.Services;

/// <summary>Exibe um resumo não bloqueante da limpeza no canto inferior direito.</summary>
public static class QuickCleanNotification
{
    public static Task ShowAsync(QuickCleanResult result, TimeSpan? duration = null)
    {
        var completion = new TaskCompletionSource();
        var toast = CreateWindow(result);
        var workArea = SystemParameters.WorkArea;
        toast.Left = workArea.Right - toast.Width - 20;
        toast.Top = workArea.Bottom - toast.Height - 20;
        toast.Show();

        var timer = new DispatcherTimer { Interval = duration ?? TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) => { timer.Stop(); toast.Close(); completion.TrySetResult(); };
        toast.Closed += (_, _) => { timer.Stop(); completion.TrySetResult(); };
        timer.Start();
        return completion.Task;
    }

    private static Window CreateWindow(QuickCleanResult result)
    {
        var freed = result.BytesFreed / 1024d / 1024d;
        var content = new StackPanel { Margin = new Thickness(18, 15, 18, 16) };
        content.Children.Add(new TextBlock
        {
            Text = "Limpeza rápida concluída", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White
        });
        content.Children.Add(new TextBlock
        {
            Text = $"Temporários do usuário e do Windows processados.\n{result.Removed:N0} arquivos removidos • {result.Ignored:N0} ignorados • {freed:N1} MB liberados",
            Margin = new Thickness(0, 7, 0, 0), FontSize = 12.5,
            Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)), TextWrapping = TextWrapping.Wrap
        });

        return new Window
        {
            Width = 390, Height = 126, Content = content,
            Background = new SolidColorBrush(Color.FromRgb(22, 24, 34)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129)), BorderThickness = new Thickness(1),
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
            Topmost = true, WindowStartupLocation = WindowStartupLocation.Manual
        };
    }
}
