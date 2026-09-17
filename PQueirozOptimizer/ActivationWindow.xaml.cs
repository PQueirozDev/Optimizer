using System.Windows;
using PQueirozOptimizer.Services;
namespace PQueirozOptimizer;
public partial class ActivationWindow : Window
{
    private readonly LicenseService _licenseService;
    public ActivationWindow(LicenseService licenseService) { InitializeComponent(); _licenseService = licenseService; MachineIdTextBox.Text = _licenseService.MachineId; AccessKeyTextBox.Focus(); }
    private void Activate_Click(object sender, RoutedEventArgs e) { if (_licenseService.TryActivate(AccessKeyTextBox.Text, out _, out var error)) { DialogResult = true; return; } StatusText.Text = error; }
    private void CopyMachineId_Click(object sender, RoutedEventArgs e) { Clipboard.SetText(MachineIdTextBox.Text); StatusText.Foreground = System.Windows.Media.Brushes.MediumSeaGreen; StatusText.Text = "ID copiado. Envie-o ao solicitar uma chave vinculada a este computador."; }
    private void Exit_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
