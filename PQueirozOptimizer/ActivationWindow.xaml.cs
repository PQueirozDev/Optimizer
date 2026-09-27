using System.Windows;
using PQueirozOptimizer.Services;
namespace PQueirozOptimizer;
public partial class ActivationWindow : Window
{
    private readonly LicenseService _licenseService;
    public ActivationWindow(LicenseService licenseService) { InitializeComponent(); _licenseService = licenseService; MachineIdTextBox.Text = _licenseService.DisplayMachineId; Activated += (_, _) => TryPasteKeyFromClipboard(); AccessKeyTextBox.Focus(); }
    private void Activate_Click(object sender, RoutedEventArgs e) { if (_licenseService.TryActivate(AccessKeyTextBox.Text, out _, out var error)) { DialogResult = true; return; } ShowStatus(error, "DangerBrush"); }
    private void CopyMachineId_Click(object sender, RoutedEventArgs e) { TrySetClipboard(_licenseService.BuildActivationRequest()); ShowStatus("Pedido copiado. Cole e envie na conversa em que você solicita a chave.", "SuccessBrush"); }
    private void Exit_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    // Quem recebe a chave normalmente já a copiou: ao voltar para a janela, ela é colada sozinha.
    private void TryPasteKeyFromClipboard()
    {
        if (!string.IsNullOrWhiteSpace(AccessKeyTextBox.Text)) return;
        string? text = null;
        try { if (Clipboard.ContainsText()) text = Clipboard.GetText(); } catch (System.Runtime.InteropServices.COMException) { }
        if (LicenseService.ExtractKey(text) is not { } key) return;
        AccessKeyTextBox.Text = key;
        ShowStatus("Chave encontrada na área de transferência. Clique em \"Ativar e continuar\".", "SuccessBrush");
    }
    private static void TrySetClipboard(string text) { try { Clipboard.SetText(text); } catch (System.Runtime.InteropServices.COMException) { } }
    private void ShowStatus(string text, string brush) { StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, brush); StatusText.Text = text; }
}
