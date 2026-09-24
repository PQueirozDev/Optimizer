using System.Runtime.InteropServices;
using System.Windows;

namespace PQueirozOptimizer;

public partial class App : Application
{
    public Services.LicenseInfo? ActiveLicense { get; private set; }
    public void SetActiveLicense(Services.LicenseInfo license) => ActiveLicense = license;
    [DllImport("shell32.dll", SetLastError = true)]
    private static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appId);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);

    // Mantido vivo enquanto o app roda; o Windows libera o mutex quando o processo termina
    private static Mutex? _instanceMutex;

    /// <summary>
    /// Garante uma única janela do Optimizer. Se já houver uma aberta, ela vem para frente.
    /// Quando a outra instância ainda está fechando (ex.: reinício após atualização), espera ela sair.
    /// </summary>
    private static bool AcquireSingleInstance()
    {
        _instanceMutex = new Mutex(true, @"Local\PedroQueiroz.Optimizer.SingleInstance", out var created);
        if (created) return true;
        if (BringExistingToFront()) return false;
        try { return _instanceMutex.WaitOne(TimeSpan.FromSeconds(10)); }
        catch (AbandonedMutexException) { return true; }
    }

    private static bool BringExistingToFront()
    {
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        foreach (var other in System.Diagnostics.Process.GetProcessesByName(current.ProcessName))
        {
            using (other)
            {
                if (other.Id == current.Id || other.HasExited || other.MainWindowHandle == IntPtr.Zero) continue;
                if (IsIconic(other.MainWindowHandle)) ShowWindow(other.MainWindowHandle, 9); // SW_RESTORE
                SetForegroundWindow(other.MainWindowHandle);
                return true;
            }
        }
        return false;
    }

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        // O atalho de Limpeza Rápida roda à parte e pode ser usado com o app aberto
        if (!e.Args.Contains("--quick-clean", StringComparer.OrdinalIgnoreCase) && !AcquireSingleInstance())
        {
            Shutdown();
            return;
        }
        try
        {
            SetCurrentProcessExplicitAppUserModelID("PedroQueiroz.Optimizer.App.v1");
        }
        catch { }

        // Tradução e tema valem para todas as janelas, inclusive a de ativação
        Services.Translator.Attach();
        try
        {
            var config = new Services.ConfigService().Config;
            Services.Translator.IsEnglish = string.Equals(config.Language, "en", StringComparison.OrdinalIgnoreCase);
            Services.ThemeService.Apply(Resources, !string.Equals(config.Theme, "Light", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { }

        var licenseService = new Services.LicenseService();
        if (!licenseService.TryGetActiveLicense(out var activeLicense, out _))
        {
            var activation = new ActivationWindow(licenseService);
            if (activation.ShowDialog() != true)
            {
                Shutdown();
                return;
            }
        }
        if (!licenseService.TryGetActiveLicense(out activeLicense, out _) || activeLicense is null)
        {
            Shutdown();
            return;
        }
        ActiveLicense = activeLicense;

        if (e.Args.Contains("--quick-clean", StringComparer.OrdinalIgnoreCase))
        {
            var service = new Services.QuickCleanService(new Services.ActivityLog());
            var status = new System.Windows.Controls.TextBlock { Text = "Removendo arquivos temporários...", Margin = new Thickness(0, 18, 0, 8) };
            var window = new Window { Title = "Limpeza Rápida", Width = 410, Height = 190, WindowStartupLocation = WindowStartupLocation.CenterScreen, ResizeMode = ResizeMode.NoResize, Content = new System.Windows.Controls.StackPanel { Margin = new Thickness(28), Children = { new System.Windows.Controls.TextBlock { Text = "🧹  Limpeza Rápida", FontSize = 24, FontWeight = FontWeights.SemiBold }, status } } };
            window.SetResourceReference(Window.BackgroundProperty, "BackgroundBrush");
            MainWindow = window; window.Show();
            var result = await service.RunAsync(new Progress<string>(value => status.Text = value));
            status.Text = $"Limpeza concluída. {result.BytesFreed / 1024d / 1024d:N1} MB liberados";
            window.Hide();
            await Services.QuickCleanNotification.ShowAsync(result);
            Shutdown(); return;
        }
        MainWindow = new MainWindow();
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        MainWindow.Show();
    }
}
