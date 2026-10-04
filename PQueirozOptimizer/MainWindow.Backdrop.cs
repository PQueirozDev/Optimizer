using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>Fundo translúcido: o Acrylic do Windows 11 aparece atrás das superfícies semitransparentes do tema.</summary>
public partial class MainWindow
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmSystemBackdropType = 38;
    private const int BackdropNone = 1, BackdropAcrylic = 3;
    private const int WmSettingChange = 0x001A;

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    private bool _settingsHooked;

    /// <summary>Aplica (ou remove) o fundo translúcido conforme o tema atual; chamado ao abrir e a cada troca de aparência.</summary>
    private void ApplyBackdrop()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        HookSystemSettings(hwnd);
        if (!ThemeService.TranslucencySupported) return;
        var translucent = ThemeService.IsTranslucent;

        // A janela e o vidro do DWM precisam ficar transparentes para o Acrylic aparecer
        if (WindowChrome.GetWindowChrome(this) is { } chrome) chrome.GlassFrameThickness = translucent ? new Thickness(-1) : new Thickness(0);
        var margins = translucent ? new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 } : new Margins();
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target) target.BackgroundColor = translucent ? Colors.Transparent : Colors.Black;
        if (translucent) Background = Brushes.Transparent;
        else SetResourceReference(BackgroundProperty, "BackgroundBrush");

        // O Acrylic segue o modo claro/escuro da janela, não o do app: alinha os dois
        var dark = ThemeService.IsDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref dark, sizeof(int));
        var backdrop = translucent ? BackdropAcrylic : BackdropNone;
        DwmSetWindowAttribute(hwnd, DwmSystemBackdropType, ref backdrop, sizeof(int));
    }

    /// <summary>
    /// Ao mudar as cores do Windows (efeitos de transparência, modo claro/escuro) reaplica a aparência:
    /// o translúcido liga ou desliga na hora e o tema Automático acompanha o sistema.
    /// </summary>
    private void HookSystemSettings(IntPtr hwnd)
    {
        if (_settingsHooked || HwndSource.FromHwnd(hwnd) is not { } source) return;
        _settingsHooked = true;
        source.AddHook((IntPtr _, int msg, IntPtr _, IntPtr lParam, ref bool _) =>
        {
            if (msg == WmSettingChange && lParam != IntPtr.Zero && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet")
                Dispatcher.BeginInvoke(() => AppearanceService.Apply(AppearanceService.Current));
            return IntPtr.Zero;
        });
    }
}
