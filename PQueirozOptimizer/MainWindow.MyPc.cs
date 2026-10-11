using System.Windows.Controls;

namespace PQueirozOptimizer;

/// <summary>Meu PC: hardware, sistema e estado do computador (montado na Etapa D do redesign).</summary>
public partial class MainWindow
{
    private void ShowMyPc()
    {
        PageTitle.Text = "Meu PC";
        PageBadge.Visibility = System.Windows.Visibility.Collapsed;
        ContentHost.Children.Clear();
        ContentHost.Children.Add(new StackPanel());
    }
}
