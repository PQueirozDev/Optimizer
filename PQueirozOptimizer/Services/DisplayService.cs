using System.Runtime.InteropServices;

namespace PQueirozOptimizer.Services;

/// <summary>Resolução e taxa de atualização do monitor principal: a atual e a maior que ele aceita nessa resolução.</summary>
public sealed record DisplayRefresh(string Device, int Width, int Height, int CurrentHz, int MaxHz)
{
    /// <summary>Monitor de alta frequência rodando abaixo do máximo (ex.: 144 Hz ligado em 60 Hz).</summary>
    public bool BelowMax => MaxHz > CurrentHz && CurrentHz > 1;
}

/// <summary>
/// Muitos monitores de 144/165/240 Hz saem de fábrica (ou depois de trocar o cabo/driver) em 60 Hz. O Windows só lista
/// os modos que o monitor e a placa anunciam, então trocar para um deles é seguro e vale na hora.
/// </summary>
public static class DisplayService
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public short SpecVersion, DriverVersion, Size, DriverExtra;
        public int Fields, PositionX, PositionY, DisplayOrientation, DisplayFixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public short LogPixels;
        public int BitsPerPel, PelsWidth, PelsHeight, DisplayFlags, DisplayFrequency;
        public int IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice info, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplaySettings(string device, int mode, ref DevMode info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ChangeDisplaySettingsEx(string device, ref DevMode mode, IntPtr hwnd, uint flags, IntPtr param);

    private const int CurrentSettings = -1;
    private const int PrimaryDevice = 0x4, AttachedToDesktop = 0x1;
    private const int FieldsFrequency = 0x400000, FieldsWidth = 0x80000, FieldsHeight = 0x100000;

    private static DevMode NewMode() => new() { Size = (short)Marshal.SizeOf<DevMode>(), DeviceName = "", FormName = "" };

    private static string? PrimaryDeviceName()
    {
        var info = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
        for (uint i = 0; EnumDisplayDevices(null, i, ref info, 0); i++, info = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() })
            if ((info.StateFlags & PrimaryDevice) != 0 && (info.StateFlags & AttachedToDesktop) != 0) return info.DeviceName;
        return null;
    }

    /// <summary>Estado do monitor principal, ou null se o Windows não informar (área de trabalho remota, sem monitor).</summary>
    public static DisplayRefresh? Primary()
    {
        if (PrimaryDeviceName() is not { } device) return null;
        var current = NewMode();
        if (!EnumDisplaySettings(device, CurrentSettings, ref current)) return null;
        var max = current.DisplayFrequency;
        var mode = NewMode();
        for (var i = 0; EnumDisplaySettings(device, i, ref mode); i++, mode = NewMode())
            // DM_INTERLACED (0x2): TVs anunciam 1080i com "Hz" maior, mas a imagem fica pior
            if (mode.PelsWidth == current.PelsWidth && mode.PelsHeight == current.PelsHeight && mode.BitsPerPel == current.BitsPerPel
                && (mode.DisplayFlags & 0x2) == 0 && mode.DisplayFrequency > max)
                max = mode.DisplayFrequency;
        return new DisplayRefresh(device, current.PelsWidth, current.PelsHeight, current.DisplayFrequency, max);
    }

    /// <summary>Liga a maior taxa da resolução atual e grava como padrão do monitor.</summary>
    public static void ApplyMax(DisplayRefresh display)
    {
        var mode = NewMode();
        if (!EnumDisplaySettings(display.Device, CurrentSettings, ref mode)) throw new InvalidOperationException("O Windows não informou o modo atual do monitor.");
        mode.DisplayFrequency = display.MaxHz;
        mode.Fields = FieldsFrequency | FieldsWidth | FieldsHeight;
        // CDS_UPDATEREGISTRY: vale agora e nas próximas vezes; 0 = DISP_CHANGE_SUCCESSFUL
        var result = ChangeDisplaySettingsEx(display.Device, ref mode, IntPtr.Zero, 0x1, IntPtr.Zero);
        if (result != 0) throw new InvalidOperationException($"O Windows recusou a troca para {display.MaxHz} Hz (código {result}).");
    }
}
