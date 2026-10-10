using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using PQueirozOptimizer;

internal static class BrandingTests
{
    public static void Run()
    {
        static void Check(bool ok, string message)
        {
            if (!ok) throw new Exception(message);
            Console.WriteLine("PASS " + message);
        }
        var app = Program.CreateTestApp();
        try
        {
            var assembly = typeof(App).Assembly;
            var version = FileVersionInfo.GetVersionInfo(assembly.Location);
            Check(assembly.GetName().Name == "Qrztweaks" && version.FileDescription == "Qrztweaks" && version.ProductName == "Qrztweaks",
                "Executable assembly and Windows product metadata use Qrztweaks");
            foreach (var name in new[] { "app.png", "app.ico", "Qrz.powerplan.txt" })
            {
                var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Qrztweaks;component/Assets/" + name));
                Check(resource != null, "Pack resource resolves: " + name);
                using var stream = resource!.Stream;
                if (name == "app.ico")
                {
                    var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    Check(decoder.Frames.Select(f => f.PixelWidth).SequenceEqual(new[] { 16, 24, 32, 48, 64, 128, 256 }),
                        "Windows icon contains all seven sizes");
                }
            }
            var mainWindow = new MainWindow(); // InitializeComponent resolves the renamed assembly's XAML assets.
            Check(mainWindow.Icon != null, "Main window loads the new icon");
            mainWindow.Close();
        }
        finally { app.Shutdown(); }
    }
}
