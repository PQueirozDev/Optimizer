using System;
using System.Threading;
using System.Windows.Forms;

namespace PQueirozOptimizer.NotebookEnergy;

/// <summary>Painel completo de energia, executado em um processo próprio do Qrztweaks.</summary>
public static class NotebookEnergyHost
{
    public static void Run(bool startHidden, string startupCommand)
    {
        // Compartilha a instância com a ferramenta anterior: dois controladores não disputam os mesmos ajustes.
        using var mutex = new Mutex(true, "Local\\ModoEnergia_Instance", out var created);
        if (!created)
        {
            try { using var show = EventWaitHandle.OpenExisting("Local\\ModoEnergia_Show"); show.Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
            return;
        }
        Config.StartupCommand = startupCommand;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\ModoEnergia_Show");
        using var context = new TrayContext(startHidden, showEvent);
        Application.Run(context);
    }
}
