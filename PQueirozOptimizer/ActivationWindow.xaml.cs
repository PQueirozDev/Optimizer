using System.Windows;
using PQueirozOptimizer.Services;
namespace PQueirozOptimizer;
public partial class ActivationWindow : Window
{
    private readonly LicenseService _licenseService;
    // Na renovação, o pedido copiado leva o titular da licença anterior
    private string? _renewalLicensee;

    /// <param name="storedKeyError">Motivo pelo qual a chave salva não abriu o app; null quando não há o que explicar.</param>
    /// <param name="allowDemo">Mostra "Explorar sem licença" (só na abertura do app, não ao trocar a chave por dentro).</param>
    public ActivationWindow(LicenseService licenseService, string? storedKeyError = null, bool allowDemo = false)
    {
        InitializeComponent();
        _licenseService = licenseService;
        MachineIdTextBox.Text = _licenseService.DisplayMachineId;
        PlansText.Text = string.Join(" · ", LicensePlans.ForSale.Select(p => $"{Translator.Tr(p.Name)}: {Translator.Tr(p.Price)}")) + ". " +
            Translator.Tr("A compra e a ativação são feitas pelo Discord, com pagamento via Pix.");
        // Chave salva de outro computador (troca de PC ou Windows reinstalado): o pedido de transferência é o caminho
        if (storedKeyError?.Contains("outro computador", StringComparison.Ordinal) == true)
            ShowProblem("Esta chave pertence a outro computador.", "Se você trocou de PC ou reinstalou o Windows, o ID mudou. Clique em \"Troquei de PC / reinstalei o Windows\" para copiar o pedido de transferência e envie no Discord.", "WarningBrush", "WarningSoftBrush");
        if (_licenseService.ExpiredLicense is { ExpiresAtUtc: { } expired } license) ShowRenewal(license.Licensee, expired);
        else if (storedKeyError is not null && _licenseService.ClockRolledBack) ShowProblem("Confira a data e a hora do Windows.", storedKeyError, "WarningBrush", "WarningSoftBrush");
        else if (storedKeyError is not null && !storedKeyError.Contains("outro computador", StringComparison.Ordinal)) ShowProblem("A licença salva neste computador não é mais válida.", storedKeyError, "DangerBrush", "DangerSoftBrush");
        DemoButton.Visibility = allowDemo ? Visibility.Visible : Visibility.Collapsed;
        Activated += (_, _) => TryPasteKeyFromClipboard();
        AccessKeyTextBox.Focus();
    }

    private void ShowRenewal(string licensee, DateTime expiredAtUtc)
    {
        _renewalLicensee = licensee;
        Title = "Renovar Qrztweaks";
        HeadingText.Text = "Renove sua licença";
        ShowProblem($"Sua licença expirou em {expiredAtUtc.ToLocalTime():dd/MM/yyyy}.",
            "Clique em \"Copiar pedido\", abra um ticket de renovação no Discord e cole o pedido lá. O pagamento é via Pix; ao copiar a nova chave, ela é colada aqui sozinha.", "WarningBrush", "WarningSoftBrush");
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

    /// <summary>O usuário escolheu o modo demonstração (sem licença, só leitura).</summary>
    public bool DemoChosen { get; private set; }
    private void Demo_Click(object sender, RoutedEventArgs e) { DemoChosen = true; DialogResult = true; }

    private void Plans_Click(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(LicensePlans.SiteUrl) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { ShowStatus(LicensePlans.SiteUrl, "MutedBrush"); }
    }

    private void Transfer_Click(object sender, RoutedEventArgs e)
    {
        TrySetClipboard(_licenseService.BuildTransferRequest(_renewalLicensee ?? _licenseService.StoredKeyLicensee()));
        ShowStatus("Pedido de transferência copiado. No Discord, abra um ticket, cole o pedido e informe o motivo.", "SuccessBrush");
    }

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
