using System.Windows;
using PQueirozOptimizer.Services;
namespace PQueirozOptimizer;
public partial class ActivationWindow : Window
{
    private readonly LicenseService _licenseService;
    // Na renovação, o pedido copiado leva o titular da licença anterior
    private string? _renewalLicensee;

    /// <param name="storedKeyError">Motivo pelo qual a chave salva não abriu o app; null quando não há o que explicar.</param>
    public ActivationWindow(LicenseService licenseService, string? storedKeyError = null)
    {
        InitializeComponent();
        _licenseService = licenseService;
        MachineIdTextBox.Text = _licenseService.DisplayMachineId;
        if (_licenseService.ExpiredLicense is { ExpiresAtUtc: { } expired } license) ShowRenewal(license.Licensee, expired);
        else if (storedKeyError is not null && _licenseService.ClockRolledBack) ShowProblem("Confira a data e a hora do Windows.", storedKeyError, "WarningBrush", "WarningSoftBrush");
        else if (storedKeyError is not null) ShowProblem("A licença salva neste computador não é mais válida.", storedKeyError, "DangerBrush", "DangerSoftBrush");
        Activated += (_, _) => TryPasteKeyFromClipboard();
        AccessKeyTextBox.Focus();
    }

    private void ShowRenewal(string licensee, DateTime expiredAtUtc)
    {
        _renewalLicensee = licensee;
        Title = "Renovar PQueiroz Optimizer";
        HeadingText.Text = "Renove sua licença";
        ShowProblem($"Sua licença expirou em {expiredAtUtc.ToLocalTime():dd/MM/yyyy}.",
            "Clique em \"Copiar pedido\" e envie na conversa em que você comprou a licença. Ao copiar a nova chave, ela é colada aqui sozinha.", "WarningBrush", "WarningSoftBrush");
    }

    private void ShowProblem(string title, string text, string brush, string softBrush)
    {
        ProblemTitle.Text = title;
        ProblemText.Text = text;
        ProblemIcon.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, brush);
        ProblemBanner.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, softBrush);
        ProblemBanner.Visibility = Visibility.Visible;
    }

    private void Activate_Click(object sender, RoutedEventArgs e) { if (_licenseService.TryActivate(AccessKeyTextBox.Text, out _, out var error)) { DialogResult = true; return; } ShowStatus(error, "DangerBrush"); }
    private void CopyMachineId_Click(object sender, RoutedEventArgs e) { TrySetClipboard(_licenseService.BuildActivationRequest(_renewalLicensee)); ShowStatus("Pedido copiado. Cole e envie na conversa em que você solicita a chave.", "SuccessBrush"); }
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
