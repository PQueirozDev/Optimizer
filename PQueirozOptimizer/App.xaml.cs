using System.Runtime.InteropServices;
using System.Windows;

namespace PQueirozOptimizer;

public partial class App : Application
{
    public Services.LicenseInfo? ActiveLicense { get; private set; }
    [DllImport("shell32.dll", SetLastError = true)]
    private static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appId);

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            SetCurrentProcessExplicitAppUserModelID("PedroQueiroz.Optimizer.App.v1");
        }
        catch { }

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
