using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace PQueirozOptimizer.NotebookEnergy;

internal static class TaskbarController
{
	private delegate bool EnumProc(nint h, nint l);

	private const string Family = "28017CharlesMilette.TranslucentTB_v826wp6bftszj";

	private const string AppUserModelId = "28017CharlesMilette.TranslucentTB_v826wp6bftszj!TranslucentTB";

	private const string ProcName = "TranslucentTB";

	private static readonly object Gate = new object();

	private static DateTime _lastStart = DateTime.MinValue;

	private static string PackageDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", "28017CharlesMilette.TranslucentTB_v826wp6bftszj");

	public static bool Installed => Directory.Exists(PackageDir);

	public static bool Running
	{
		get
		{
			var processes = Process.GetProcessesByName(ProcName);
			try { return processes.Length != 0; }
			finally { foreach (var process in processes) process.Dispose(); }
		}
	}

	public static void TakeOver()
	{
		if (!Installed)
		{
			return;
		}
		try
		{
			string path = Path.Combine(PackageDir, "RoamingState", "settings.json");
			if (File.Exists(path))
			{
				string text = File.ReadAllText(path);
				if (!text.Contains("\"hide_tray\""))
				{
					int num = text.IndexOf("\"$schema\"", StringComparison.Ordinal);
					int num2 = ((num >= 0) ? text.IndexOf('\n', num) : (-1));
					if (num2 > 0)
					{
						File.WriteAllText(path, text.Insert(num2 + 1, "  \"hide_tray\": true,\r\n"));
					}
				}
			}
		}
		catch
		{
		}
		SetOwnStartup(enabled: false);
	}

	public static void SetOwnStartup(bool enabled)
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.CreateSubKey("Software\\Classes\\Local Settings\\Software\\Microsoft\\Windows\\CurrentVersion\\AppModel\\SystemAppData\\28017CharlesMilette.TranslucentTB_v826wp6bftszj\\TranslucentTB");
			registryKey.SetValue("State", (!enabled) ? 1 : 2, RegistryValueKind.DWord);
		}
		catch
		{
		}
	}

	public static void Enforce(bool transparent)
	{
		if (!Installed)
		{
			return;
		}
		lock (Gate)
		{
			if (transparent && !Running)
			{
				if (!((DateTime.Now - _lastStart).TotalSeconds < 10.0))
				{
					_lastStart = DateTime.Now;
					Start();
				}
			}
			else if (!transparent && Running)
			{
				Stop();
			}
		}
	}

	private static void Start()
	{
		try
		{
			Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\28017CharlesMilette.TranslucentTB_v826wp6bftszj!TranslucentTB")
			{
				UseShellExecute = true
			})?.Dispose();
		}
		catch
		{
		}
	}

	private static void Stop()
	{
		Process[] processesByName = Process.GetProcessesByName("TranslucentTB");
		foreach (Process process in processesByName)
		{
			using (process)
			{
				nint num = FindWindowOf((uint)process.Id, "TrayWindow");
				if (num != IntPtr.Zero)
				{
					PostMessage(num, 16u, IntPtr.Zero, IntPtr.Zero);
				}
				try
				{
					if (!process.WaitForExit(3000))
					{
						process.Kill();
					}
				}
				catch
				{
				}
			}
		}
	}

	[DllImport("user32.dll")]
	private static extern bool EnumWindows(EnumProc p, nint l);

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(nint h, out uint pid);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetClassName(nint h, StringBuilder s, int n);

	[DllImport("user32.dll")]
	private static extern bool PostMessage(nint h, uint m, nint w, nint l);

	private static nint FindWindowOf(uint pid, string cls)
	{
		nint found = IntPtr.Zero;
		EnumWindows(delegate(nint h, nint _)
		{
			GetWindowThreadProcessId(h, out var pid2);
			if (pid2 == pid)
			{
				StringBuilder stringBuilder = new StringBuilder(256);
				GetClassName(h, stringBuilder, 256);
				if (stringBuilder.ToString() == cls)
				{
					found = h;
					return false;
				}
			}
			return true;
		}, IntPtr.Zero);
		return found;
	}
}
