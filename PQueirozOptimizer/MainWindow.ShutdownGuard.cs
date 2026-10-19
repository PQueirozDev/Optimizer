using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PQueirozOptimizer;

/// <summary>
/// Desligar ou reiniciar no meio de um debloat, de uma limpeza de drivers ou do SFC/DISM pode deixar o Windows
/// pela metade. Enquanto uma operação roda, o app pede ao Windows para segurar o desligamento e mostra o motivo
/// na tela "Este app está impedindo o desligamento" (o usuário ainda pode forçar por lá).
/// </summary>
public partial class MainWindow
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool ShutdownBlockReasonCreate(IntPtr hwnd, string reason);
    [DllImport("user32.dll")] private static extern bool ShutdownBlockReasonDestroy(IntPtr hwnd);

    private bool _shutdownGuarded;

    /// <summary>
    /// Para operações que reiniciam o PC de propósito (o DDU reinicia sozinho no meio da instalação limpa):
    /// libera a trava até o fim da operação atual.
    /// </summary>
    private void AllowShutdownDuringOperation() => Dispatcher.Invoke(EndShutdownGuard);

    private void BeginShutdownGuard(string title)
    {
        if (_shutdownGuarded || Application.Current is null) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero) ShutdownBlockReasonCreate(handle, Services.Translator.Tr($"{title} em andamento. Desligar agora pode deixar o Windows com ajustes pela metade."));
        Application.Current.SessionEnding += RefuseSessionEnd;
        _shutdownGuarded = true;
    }

    private void EndShutdownGuard()
    {
        if (!_shutdownGuarded) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero) ShutdownBlockReasonDestroy(handle);
        if (Application.Current != null) Application.Current.SessionEnding -= RefuseSessionEnd;
        _shutdownGuarded = false;
    }

    private void RefuseSessionEnd(object? sender, SessionEndingCancelEventArgs e)
    {
        if (!_operationRunning) return;
        e.Cancel = true;
        _log.Write("WARN", $"Desligamento do Windows adiado: operação em andamento ({e.ReasonSessionEnding})");
    }
}
