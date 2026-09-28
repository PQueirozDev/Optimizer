using System.Runtime.InteropServices;
using System.Windows;

namespace PQueirozOptimizer;

public partial class App : Application
{
    public Services.LicenseInfo? ActiveLicense { get; private set; }
    public void SetActiveLicense(Services.LicenseInfo license) => ActiveLicense = license;
    /// <summary>Log único do processo: a janela principal mostra também os erros registrados aqui.</summary>
    public Services.ActivityLog Log { get; } = new();
    private bool _showingError;
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

    /// <summary>
    /// Um erro inesperado em um botão não deve fechar o aplicativo nem apagar o que está na tela:
    /// ele é registrado na atividade e o usuário recebe um aviso.
    /// </summary>
    private void RegisterErrorHandlers()
    {
        static string Describe(Exception ex) => $"{ex.GetType().Name}: {ex.Message} | {ex.StackTrace?.Replace(Environment.NewLine, " ")}";
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Write("ERROR", "Erro inesperado: " + Describe(args.Exception));
            args.Handled = true;
            if (_showingError) return;
            _showingError = true;
            try
            {
                MessageBox.Show(Services.Translator.Tr("Ocorreu um erro inesperado, mas o aplicativo continua aberto. Os detalhes foram salvos em Atividade e reversão.") + "\n\n" + args.Exception.Message,
                    "PQueiroz Optimizer", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally { _showingError = false; }
        };
        TaskScheduler.UnobservedTaskException += (_, args) => { Log.Write("ERROR", "Erro em segundo plano: " + Describe(args.Exception.GetBaseException())); args.SetObserved(); };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => { if (args.ExceptionObject is Exception ex) Log.Write("ERROR", "Erro fatal: " + Describe(ex)); };
    }

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        RegisterErrorHandlers();
        var quickClean = e.Args.Contains("--quick-clean", StringComparer.OrdinalIgnoreCase);
        var powerMode = e.Args.Contains("--power-mode", StringComparer.OrdinalIgnoreCase);
        // Os atalhos de Limpeza Rápida e de modo de energia rodam à parte e podem ser usados com o app aberto
        if (!quickClean && !powerMode && !AcquireSingleInstance())
        {
            Shutdown();
            return;
        }
        // O atalho de modo de energia abre sem elevação (__COMPAT_LAYER=RunAsInvoker); nada aberto daqui deve herdar isso
        if (powerMode) Environment.SetEnvironmentVariable("__COMPAT_LAYER", null);
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

        if (powerMode)
        {
            var picker = new PowerModeWindow(Log, closeAfterChoice: true);
            MainWindow = picker;
            picker.Closed += (_, _) => Shutdown();
            picker.Show();
            return;
        }

        if (quickClean)
        {
            var service = new Services.QuickCleanService(Log);
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
