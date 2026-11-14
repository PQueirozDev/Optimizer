using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PQueirozOptimizer.NotebookEnergy;

internal class MainForm : Form
{
	private readonly Config _cfg;

	private readonly PowerController _power;
	private bool _syncing;

	private readonly Badge _badge = new Badge();

	private readonly Label _title = new Label();

	private readonly Label _sub = new Label();

	private readonly Segmented _seg = new Segmented("Automático", "Desempenho", "Economia");

	private readonly Label _vPower = new Label();

	private readonly Label _vSource = new Label();

	private readonly Label _vBright = new Label();

	private readonly Label _vWall = new Label();

	private readonly Toggle _tStart = new Toggle("Iniciar com o Windows");

	private readonly Toggle _tNotify = new Toggle("Avisar quando trocar de modo");

	private readonly Toggle _tWall = new Toggle("Pausar Wallpaper Engine na economia");

	private readonly Toggle _tBright = new Toggle("Limitar brilho a 70% na economia");

	private readonly Toggle _tBar = new Toggle("Barra de tarefas transparente");

	private readonly Timer _refresh = new Timer
	{
		Interval = 3000
	};

	public bool AllowClose { get; set; }

	public event Action? ModeChosen;

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

	public MainForm(Config cfg, PowerController power)
	{
		_cfg = cfg;
		_power = power;
		Text = "Modo de energia · Qrztweaks";
		base.AutoScaleMode = AutoScaleMode.None;
		base.FormBorderStyle = FormBorderStyle.FixedSingle;
		base.MaximizeBox = false;
		BackColor = Theme.Bg;
		ForeColor = Theme.Text;
		Font = new Font("Segoe UI", 9.75f);
		base.ShowInTaskbar = true;
		base.StartPosition = FormStartPosition.Manual;
		try
		{
			string? processPath = Environment.ProcessPath;
			if (processPath != null)
			{
				base.Icon = System.Drawing.Icon.ExtractAssociatedIcon(processPath);
			}
		}
		catch
		{
		}
		BuildLayout();
		_seg.Selected = (int)_cfg.Mode;
		_seg.SelectionChanged += delegate
		{
			if (!_syncing) ChooseMode((Mode)_seg.Selected);
		};
		BindOption(_tStart, () => _cfg.StartWithWindows, value => _cfg.StartWithWindows = value, _cfg.ApplyStartup);
		BindOption(_tNotify, () => _cfg.Notify, value => _cfg.Notify = value);
		BindOption(_tWall, () => _cfg.ControlWallpaper, value => _cfg.ControlWallpaper = value, _power.WallpaperOptionChanged);
		BindOption(_tBright, () => _cfg.LimitBrightness, value => _cfg.LimitBrightness = value, _power.BrightnessOptionChanged);
		BindOption(_tBar, () => _cfg.TransparentTaskbar, value => _cfg.TransparentTaskbar = value,
			() => { bool transparent = _cfg.TransparentTaskbar; Task.Run(() => TaskbarController.Enforce(transparent)); });
		if (!TaskbarController.Installed)
		{
			_tBar.Text = "Barra transparente (instale o TranslucentTB)";
			_tBar.Enabled = false;
			_tBar.ForeColor = Theme.Muted;
		}
		_refresh.Tick += delegate
		{
			RefreshView();
		};
		base.VisibleChanged += delegate
		{
			_refresh.Enabled = base.Visible;
			if (base.Visible)
			{
				RefreshView();
			}
		};
	}

	private void BindOption(Toggle toggle, Func<bool> read, Action<bool> write, Action? apply = null)
	{
		toggle.Checked = read();
		toggle.CheckedChanged += delegate
		{
			if (_syncing) return;
			bool previous = read();
			write(toggle.Checked);
			try
			{
				_cfg.Save();
				apply?.Invoke();
			}
			catch (Exception ex)
			{
				write(previous);
				try { _cfg.Save(); apply?.Invoke(); } catch { }
				_syncing = true;
				try { toggle.Checked = previous; } finally { _syncing = false; }
				ShowError(ex);
			}
			RefreshView();
		};
	}

	public void ChooseMode(Mode mode)
	{
		Mode previous = _cfg.Mode;
		_cfg.Mode = mode;
		try { _cfg.Save(); }
		catch (Exception ex)
		{
			_cfg.Mode = previous;
			SyncMode();
			ShowError(ex);
			return;
		}
		SyncMode();
		ModeChosen?.Invoke();
	}

	private void ShowError(Exception error) => MessageBox.Show(this,
		"Não foi possível salvar ou aplicar esta opção.\n\n" + error.Message,
		"Modo de energia · Qrztweaks", MessageBoxButtons.OK, MessageBoxIcon.Warning);

	private int S(float v)
	{
		return (int)Math.Round(v * (float)base.DeviceDpi / 96f);
	}

	private void BuildLayout()
	{
		int W = 380;
		int pad = 20;
		int y = 20;
		base.ClientSize = new Size(S(W), S(10f));
		_badge.SetBounds(S(pad), S(y), S(46f), S(46f));
		_title.Font = new Font("Segoe UI Semibold", 13f);
		_title.SetBounds(S(pad + 60), S(y + 1), S(W - pad * 2 - 60), S(26f));
		_sub.ForeColor = Theme.Muted;
		_sub.SetBounds(S(pad + 60), S(y + 26), S(W - pad * 2 - 60), S(20f));
		base.Controls.AddRange(new Control[3] { _badge, _title, _sub });
		y += 66;
		_seg.Font = new Font("Segoe UI Semibold", 9.75f);
		_seg.SetBounds(S(pad), S(y), S(W - pad * 2), S(42f));
		base.Controls.Add(_seg);
		y += 58;
		Row("Modo do Windows", _vPower);
		Row("Energia", _vSource);
		Row("Brilho", _vBright);
		Row("Wallpaper Engine", _vWall);
		y += 8;
		Label label = new Label
		{
			BackColor = Theme.Line
		};
		label.SetBounds(S(pad), S(y), S(W - pad * 2), Math.Max(1, S(1f)));
		base.Controls.Add(label);
		y += 14;
		Toggle[] array = new Toggle[5] { _tBar, _tWall, _tBright, _tNotify, _tStart };
		foreach (Toggle toggle in array)
		{
			toggle.SetBounds(S(pad), S(y), S(W - pad * 2), S(30f));
			base.Controls.Add(toggle);
			y += 34;
		}
		y += 6;
		Label label2 = new Label
		{
			Text = "Fechar esta janela não desliga o app: ele continua ao lado do relógio.",
			ForeColor = Theme.Muted,
			Font = new Font("Segoe UI", 8.75f)
		};
		label2.SetBounds(S(pad), S(y), S(W - pad * 2), S(36f));
		base.Controls.Add(label2);
		y += 44;
		base.ClientSize = new Size(S(W), S(y));
		void Row(string text, Label value)
		{
			Label label3 = new Label
			{
				Text = text,
				ForeColor = Theme.Muted,
				AutoSize = false
			};
			label3.SetBounds(S(pad), S(y), S(170f), S(22f));
			value.ForeColor = Theme.Text;
			value.TextAlign = ContentAlignment.MiddleRight;
			value.Font = new Font("Segoe UI Semibold", 9.75f);
			value.SetBounds(S(pad + 170), S(y), S(W - pad * 2 - 170), S(22f));
			label3.TextAlign = ContentAlignment.MiddleLeft;
			base.Controls.Add(label3);
			base.Controls.Add(value);
			y += 28;
		}
	}

	public void PlaceNearTray()
	{
		Rectangle rectangle = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
		base.Location = new Point(rectangle.Right - base.Width - S(12f), rectangle.Bottom - base.Height - S(12f));
	}

	public void SyncMode()
	{
		if (_seg.Selected != (int)_cfg.Mode)
		{
			_syncing = true;
			try { _seg.Selected = (int)_cfg.Mode; } finally { _syncing = false; }
		}
	}

	public void RefreshView()
	{
		if (!base.IsDisposed)
		{
			Effective effective = _power.Current ?? _power.Target;
			bool onAC = PowerController.OnAC;
			int batteryPercent = PowerController.BatteryPercent;
			_badge.Mode = effective;
			if (_cfg.Mode == Mode.Auto)
			{
				_title.Text = (onAC ? "Na tomada" : "Na bateria");
				_sub.Text = ((effective == Effective.Performance) ? "Desempenho máximo (automático)" : "Economia de energia (automático)");
			}
			else
			{
				_title.Text = ((_cfg.Mode == Mode.Performance) ? "Desempenho fixo" : "Economia fixa");
				_sub.Text = ((_cfg.Mode == Mode.Performance) ? "Máximo mesmo na bateria" : "Mais bateria, mais frio e silencioso");
			}
			if (_power.LastError != null)
			{
				_sub.Text = _power.LastError;
			}
			_vPower.Text = PowerController.OverlayName();
			_vSource.Text = (onAC ? $"Tomada · {batteryPercent}%" : $"Bateria · {batteryPercent}%");
			Label vBright = _vBright;
			int? brightness = PowerController.GetBrightness();
			object text;
			if (brightness.HasValue)
			{
				int valueOrDefault = brightness.GetValueOrDefault();
				text = $"{valueOrDefault}%";
			}
			else
			{
				text = "—";
			}
			vBright.Text = (string)text;
			_vWall.Text = ((!PowerController.WallpaperRunning) ? "Fechado" : ((_cfg.ControlWallpaper && effective == Effective.Economy) ? "Pausado" : "Ligado"));
		}
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		int value = 1;
		DwmSetWindowAttribute(base.Handle, 20, ref value, 4);
		int value2 = Theme.Bg.R | (Theme.Bg.G << 8) | (Theme.Bg.B << 16);
		DwmSetWindowAttribute(base.Handle, 35, ref value2, 4);
		int value3 = Theme.Line.R | (Theme.Line.G << 8) | (Theme.Line.B << 16);
		DwmSetWindowAttribute(base.Handle, 34, ref value3, 4);
	}

	protected override void OnDpiChanged(DpiChangedEventArgs e)
	{
		base.OnDpiChanged(e);
		base.Controls.Clear();
		BuildLayout();
		RefreshView();
	}

	protected override void OnFormClosing(FormClosingEventArgs e)
	{
		if (!AllowClose && e.CloseReason == CloseReason.UserClosing)
		{
			e.Cancel = true;
			Hide();
		}
		else
		{
			base.OnFormClosing(e);
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing) _refresh.Dispose();
		base.Dispose(disposing);
	}
}
