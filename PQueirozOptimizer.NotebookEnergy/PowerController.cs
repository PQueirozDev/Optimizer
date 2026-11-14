using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PQueirozOptimizer.NotebookEnergy;

public class PowerController
{
	public static readonly Guid OverlayEfficiency = new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a");

	public static readonly Guid OverlayBalanced = Guid.Empty;

	public static readonly Guid OverlayBetterPerf = new Guid("3af9b8d9-7c97-431d-ad78-34a8bfea439f");

	public static readonly Guid OverlayPerformance = new Guid("ded574b5-45a0-4f42-8737-46345c09c238");

	private const int EcoBrightness = 70;

	private readonly Config _cfg;
	private readonly Func<bool> _onAC;
	private readonly Func<Guid, uint> _setOverlay;
	private readonly Func<int?> _getBrightness;
	private readonly Action<int> _setBrightness;
	private readonly Action<bool> _setWallpaperPaused;

	public Effective? Current { get; private set; }

	public string? LastError { get; private set; }

	public static bool OnAC => SystemInformation.PowerStatus.PowerLineStatus != PowerLineStatus.Offline;

	public static int BatteryPercent => (int)Math.Round(SystemInformation.PowerStatus.BatteryLifePercent * 100f);

	internal static Effective TargetFor(Mode mode, bool onAC) => mode switch
	{
		Mode.Performance => Effective.Performance, 
		Mode.Economy => Effective.Economy, 
		_ => !onAC ? Effective.Economy : Effective.Performance,
	};
	public Effective Target => TargetFor(_cfg.Mode, _onAC());

	public static bool WallpaperRunning
	{
		get
		{
			var processes = Process.GetProcessesByName("wallpaper64").Concat(Process.GetProcessesByName("wallpaper32")).ToArray();
			try { return processes.Length != 0; }
			finally { foreach (var process in processes) process.Dispose(); }
		}
	}

	[DllImport("powrprof.dll")]
	private static extern uint PowerSetActiveOverlayScheme(Guid overlaySchemeGuid);

	[DllImport("powrprof.dll")]
	private static extern uint PowerGetEffectiveOverlayScheme(out Guid effectiveOverlayGuid);

	public PowerController(Config cfg) : this(cfg, () => OnAC, PowerSetActiveOverlayScheme, GetBrightness, SetBrightness,
		paused => ApplyWallpaper(paused ? Effective.Economy : Effective.Performance))
	{
	}

	internal PowerController(Config cfg, Func<bool> onAC, Func<Guid, uint> setOverlay, Func<int?> getBrightness,
		Action<int> setBrightness, Action<bool> setWallpaperPaused)
	{
		_cfg = cfg;
		_onAC = onAC;
		_setOverlay = setOverlay;
		_getBrightness = getBrightness;
		_setBrightness = setBrightness;
		_setWallpaperPaused = setWallpaperPaused;
	}

	public bool Apply(bool force = false)
	{
		Effective target = Target;
		if (!force && Current == target)
		{
			return false;
		}
		LastError = null;
		Guid overlaySchemeGuid = ((target == Effective.Performance) ? OverlayPerformance : OverlayEfficiency);
		try
		{
			uint num = _setOverlay(overlaySchemeGuid);
			if (num != 0)
			{
				LastError = $"Windows recusou o modo de energia (código {num}).";
				return false;
			}
		}
		catch (Exception ex)
		{
			LastError = ex.Message;
			return false;
		}
		Current = target;
		if (_cfg.LimitBrightness)
		{
			TryOption(() => ApplyBrightness(target));
		}
		if (_cfg.ControlWallpaper)
		{
			TryOption(() => _setWallpaperPaused(target == Effective.Economy));
		}
		return true;
	}

	private void TryOption(Action apply)
	{
		try { apply(); }
		catch (Exception ex) { LastError = LastError is null ? ex.Message : LastError + " · " + ex.Message; }
	}

	public static string OverlayName()
	{
		try
		{
			PowerGetEffectiveOverlayScheme(out var effectiveOverlayGuid);
			if (effectiveOverlayGuid == OverlayPerformance)
			{
				return "Melhor desempenho";
			}
			if (effectiveOverlayGuid == OverlayEfficiency)
			{
				return "Melhor eficiência";
			}
			if (effectiveOverlayGuid == OverlayBetterPerf)
			{
				return "Equilibrado +";
			}
			if (effectiveOverlayGuid == OverlayBalanced)
			{
				return "Equilibrado";
			}
		}
		catch
		{
		}
		return "Desconhecido";
	}

	public void WallpaperOptionChanged()
	{
		Effective? current = Current;
		if (current.HasValue)
		{
			Effective valueOrDefault = current.GetValueOrDefault();
			_setWallpaperPaused(_cfg.ControlWallpaper && valueOrDefault == Effective.Economy);
		}
	}

	public void BrightnessOptionChanged()
	{
		Effective? current = Current;
		if (current.HasValue)
		{
			Effective valueOrDefault = current.GetValueOrDefault();
			if (_cfg.LimitBrightness)
			{
				ApplyBrightness(valueOrDefault);
			}
			else
			{
				ApplyBrightness(Effective.Performance);
			}
		}
	}

	private void ApplyBrightness(Effective target)
	{
		int? brightness = _getBrightness();
		if (!brightness.HasValue)
		{
			return;
		}
		int valueOrDefault = brightness.GetValueOrDefault();
		if (target == Effective.Economy)
		{
			if (valueOrDefault > 70)
			{
				_cfg.SavedBrightness ??= valueOrDefault;
				_cfg.Save();
				_setBrightness(70);
			}
			return;
		}
		int? savedBrightness = _cfg.SavedBrightness;
		if (savedBrightness.HasValue)
		{
			int valueOrDefault2 = savedBrightness.GetValueOrDefault();
			if (valueOrDefault == 70)
			{
				_setBrightness(valueOrDefault2);
			}
			_cfg.SavedBrightness = null;
			_cfg.Save();
		}
	}

	public static int? GetBrightness()
	{
		try
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
			using ManagementObjectCollection.ManagementObjectEnumerator managementObjectEnumerator = managementObjectSearcher.Get().GetEnumerator();
			if (managementObjectEnumerator.MoveNext())
			{
				ManagementObject managementObject = (ManagementObject)managementObjectEnumerator.Current;
				using (managementObject)
				{
					return Convert.ToInt32(managementObject["CurrentBrightness"]);
				}
			}
		}
		catch
		{
		}
		return null;
	}

	private static void SetBrightness(int value)
	{
		try
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
			using ManagementObjectCollection.ManagementObjectEnumerator managementObjectEnumerator = managementObjectSearcher.Get().GetEnumerator();
			if (managementObjectEnumerator.MoveNext())
			{
				ManagementObject managementObject = (ManagementObject)managementObjectEnumerator.Current;
				using (managementObject)
				{
					managementObject.InvokeMethod("WmiSetBrightness", new object[2]
					{
						1u,
						(byte)value
					});
					return;
				}
			}
		}
		catch
		{
		}
	}

	private static void ApplyWallpaper(Effective target)
	{
		try
		{
			var processes = Process.GetProcessesByName("wallpaper64").Concat(Process.GetProcessesByName("wallpaper32")).ToArray();
			try
			{
			Process? process = processes.FirstOrDefault();
			if (process != null)
			{
				string? text;
				try
				{
					text = process.MainModule?.FileName;
				}
				catch
				{
					text = null;
				}
				if (text != null)
				{
					string text2 = ((target == Effective.Economy) ? "pause" : "play");
					Process.Start(new ProcessStartInfo(text, "-control " + text2)
					{
						UseShellExecute = false,
						CreateNoWindow = true
					})?.Dispose();
				}
			}
			}
			finally { foreach (var process in processes) process.Dispose(); }
		}
		catch
		{
		}
	}
}
