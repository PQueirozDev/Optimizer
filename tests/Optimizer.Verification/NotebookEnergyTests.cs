using System.IO;
using PQueirozOptimizer.NotebookEnergy;
using PQueirozOptimizer.Services;

internal static class NotebookEnergyTests
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }

    public static void Run(string directory)
    {
        Directory.CreateDirectory(directory);
        Check(PowerController.TargetFor(Mode.Auto, true) == Effective.Performance
            && PowerController.TargetFor(Mode.Auto, false) == Effective.Economy
            && PowerController.TargetFor(Mode.Performance, false) == Effective.Performance
            && PowerController.TargetFor(Mode.Economy, true) == Effective.Economy,
            "Notebook: automático acompanha a fonte; os modos fixos ignoram tomada e bateria");

        var legacy = Path.Combine(directory, "legacy.json");
        var own = Path.Combine(directory, "energy.json");
        File.WriteAllText(legacy, """{"Mode":"Economy","StartWithWindows":true,"Notify":false,"ControlWallpaper":true,"LimitBrightness":true,"TransparentTaskbar":false,"SavedBrightness":100}""");
        if (File.Exists(own)) File.Delete(own);
        var config = Config.LoadFrom(own, legacy);
        Check(config.Mode == Mode.Economy && !config.Notify && config.ControlWallpaper && config.LimitBrightness && !config.TransparentTaskbar
            && !config.StartWithWindows && config.SavedBrightness is null,
            "Notebook: importa preferências sem assumir inicialização ou brilho pendente da ferramenta anterior");
        config.Save();
        Check(Config.LoadFrom(own).Mode == Mode.Economy && !File.Exists(own + ".tmp"), "Notebook: preferências salvas e lidas no diretório próprio");
        Check(File.ReadAllText(legacy).Contains("\"StartWithWindows\":true"), "Notebook: preferências do aplicativo independente preservadas");

        config.Mode = Mode.Auto;
        var onAC = false;
        var brightness = 95;
        var paused = false;
        uint nativeResult = 0;
        var overlays = new List<Guid>();
        var changes = new List<int>();
        var power = new PowerController(config, () => onAC, overlay => { overlays.Add(overlay); return nativeResult; },
            () => brightness, value => { brightness = value; changes.Add(value); }, value => paused = value);
        Check(power.Apply() && power.Current == Effective.Economy && brightness == 70 && config.SavedBrightness == 95 && paused,
            "Notebook: na bateria limita o brilho e pausa o Wallpaper Engine");
        Check(!power.Apply() && overlays.Count == 1 && changes.Count == 1, "Notebook: não reaplica o mesmo estado a cada leitura");
        onAC = true;
        Check(power.Apply() && power.Current == Effective.Performance && brightness == 95 && config.SavedBrightness is null && !paused,
            "Notebook: ao ligar na tomada restaura o brilho anterior e retoma o Wallpaper Engine");

        onAC = false;
        power.Apply();
        brightness = 45;
        onAC = true;
        power.Apply();
        Check(brightness == 45 && config.SavedBrightness is null, "Notebook: restaurar não sobrescreve o brilho ajustado manualmente pelo usuário");
        brightness = 90;
        onAC = false;
        power.Apply();
        config.LimitBrightness = false;
        power.BrightnessOptionChanged();
        config.ControlWallpaper = false;
        power.WallpaperOptionChanged();
        Check(brightness == 90 && !paused && config.SavedBrightness is null, "Notebook: desligar as opções devolve o brilho e retoma o wallpaper");

        onAC = true;
        nativeResult = 5;
        Check(!power.Apply() && power.Current == Effective.Economy && power.LastError is not null,
            "Notebook: falha do Windows não vira sucesso nem troca o estado confirmado");
        var arguments = NotebookEnergyService.Arguments(@"C:\Program Files\Qrztweaks\Qrztweaks.exe", tray: true);
        Check(arguments.EndsWith("\"C:\\Program Files\\Qrztweaks\\Qrztweaks.exe\" --notebook-energy --tray")
            && !arguments.Contains("--power-mode"), "Notebook: inicialização e atalhos chamam o módulo completo em processo próprio");
        var malformed = Path.Combine(directory, "invalid.json");
        File.WriteAllText(malformed, "{invalid");
        var rejected = false;
        try { Config.LoadFrom(malformed); } catch (System.Text.Json.JsonException) { rejected = true; }
        Check(rejected && File.ReadAllText(malformed) == "{invalid", "Notebook: preferências inválidas são preservadas e não geram aplicação de ajustes padrão");

        // O painel é renderizado sem iniciar o controlador nativo ou gravar no Registro deste PC.
        using var form = new MainForm(config, power);
        var chosen = 0;
        form.ModeChosen += () => chosen++;
        config.Mode = Mode.Performance;
        form.SyncMode();
        Check(chosen == 0, "Notebook: sincronizar o menu da bandeja não reaplica o modo");
        form.ChooseMode(Mode.Auto);
        Check(chosen == 1 && config.Mode == Mode.Auto, "Notebook: escolher modo pelo painel ou bandeja aplica uma vez");
        form.RefreshView();
        form.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
        form.Location = new System.Drawing.Point(-20000, -20000);
        form.ShowInTaskbar = false;
        form.Show();
        System.Windows.Forms.Application.DoEvents();
        using var bitmap = new System.Drawing.Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
        bitmap.Save(Path.Combine(directory, "notebook-energy.png"), System.Drawing.Imaging.ImageFormat.Png);
        Check(form.Controls.OfType<Toggle>().Count() == 5 && form.Controls.OfType<Segmented>().Single().Selected == 0,
            "Notebook: painel completo contém os cinco controles e os três modos");
    }
}
