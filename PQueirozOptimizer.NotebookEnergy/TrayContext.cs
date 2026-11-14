using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PQueirozOptimizer.NotebookEnergy;

internal class TrayContext : ApplicationContext
{
	private readonly Config _cfg = Config.Load();

	private readonly PowerController _power;

	private readonly MainForm _form;

	private readonly NotifyIcon _tray;

	private readonly ToolStripMenuItem _miAuto;

	private readonly ToolStripMenuItem _miPerf;

	private readonly ToolStripMenuItem _miEco;

	private readonly Icon _icoPerf = Theme.MakeTrayIcon(Effective.Performance);

	private readonly Icon _icoEco = Theme.MakeTrayIcon(Effective.Economy);

	private readonly System.Windows.Forms.Timer _watch = new System.Windows.Forms.Timer
	{
		Interval = 15000
	};

	private readonly RegisteredWaitHandle _showWait;
	private bool _disposed;

	public TrayContext(bool startHidden, EventWaitHandle showEvent)
	{
		_power = new PowerController(_cfg);
		_cfg.ApplyStartup();
		_form = new MainForm(_cfg, _power);
		_form.ModeChosen += delegate
		{
			_power.Apply(force: true);
			UpdateTray();
			_form.RefreshView();
		};
		_miAuto = new ToolStripMenuItem("Automático", null, delegate
		{
			SetMode(Mode.Auto);
		});
		_miPerf = new ToolStripMenuItem("Desempenho", null, delegate
		{
			SetMode(Mode.Performance);
		});
		_miEco = new ToolStripMenuItem("Economia", null, delegate
		{
			SetMode(Mode.Economy);
		});
		ContextMenuStrip contextMenuStrip = new ContextMenuStrip();
		contextMenuStrip.Items.Add(new ToolStripMenuItem("Abrir Modo de Energia", null, delegate
		{
			ShowForm();
		})
		{
			Font = new Font(contextMenuStrip.Font, FontStyle.Bold)
		});
		contextMenuStrip.Items.Add(new ToolStripSeparator());
		contextMenuStrip.Items.AddRange(new ToolStripItem[3] { _miAuto, _miPerf, _miEco });
		contextMenuStrip.Items.Add(new ToolStripSeparator());
		contextMenuStrip.Items.Add(new ToolStripMenuItem("Sair", null, delegate
		{
			ExitApp();
		}));
		_tray = new NotifyIcon
		{
			ContextMenuStrip = contextMenuStrip,
			Visible = true,
			Icon = _icoPerf,
			Text = "Modo de Energia"
		};
		_tray.MouseClick += delegate(object? _, MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				ToggleForm();
			}
		};
		_power.Apply(force: true);
		UpdateTray();
		Task.Run(delegate
		{
			TaskbarController.TakeOver();
			TaskbarController.Enforce(_cfg.TransparentTaskbar);
		});
		SystemEvents.PowerModeChanged += OnPowerModeChanged;
		_watch.Tick += delegate
		{
			CheckAndApply();
			bool bar = _cfg.TransparentTaskbar;
			Task.Run(delegate
			{
				TaskbarController.Enforce(bar);
			});
		};
		_watch.Start();
		_showWait = ThreadPool.RegisterWaitForSingleObject(showEvent, delegate
		{
			try
			{
				_form.BeginInvoke(ShowForm);
			}
			catch
			{
			}
		}, null, -1, executeOnlyOnce: false);
		if (!startHidden)
		{
			ShowForm();
		}
		else
		{
			_ = _form.Handle;
		}
	}

	private void OnPowerModeChanged(object? s, PowerModeChangedEventArgs e)
	{
		PowerModes mode = e.Mode;
		if ((uint)(mode - 1) <= 1u)
		{
			_form.BeginInvoke(CheckAndApply);
		}
	}

	private void CheckAndApply()
	{
		if (_power.Apply())
		{
			UpdateTray();
			if (_cfg.Notify)
			{
				bool flag = _power.Current == Effective.Performance;
				string tipTitle = ((_cfg.Mode != Mode.Auto) ? "Modo de Energia" : (PowerController.OnAC ? "Na tomada" : "Na bateria"));
				_tray.ShowBalloonTip(3000, tipTitle, flag ? "Desempenho máximo ativado" : "Modo economia ativado", ToolTipIcon.None);
			}
		}
		if (_form.Visible)
		{
			_form.RefreshView();
		}
	}

	private void SetMode(Mode m)
	{
		_form.ChooseMode(m);
	}

	private void UpdateTray()
	{
		bool flag = _power.Current == Effective.Performance;
		_tray.Icon = (flag ? _icoPerf : _icoEco);
		Mode mode = _cfg.Mode;
		_tray.Text = "Modo de Energia — " + mode switch
		{
			Mode.Performance => "Desempenho (fixo)", 
			Mode.Economy => "Economia (fixa)", 
			_ => flag ? "Automático · desempenho" : "Automático · economia", 
		};
		_miAuto.Checked = _cfg.Mode == Mode.Auto;
		_miPerf.Checked = _cfg.Mode == Mode.Performance;
		_miEco.Checked = _cfg.Mode == Mode.Economy;
	}

	private void ShowForm()
	{
		_form.PlaceNearTray();
		_form.Show();
		if (_form.WindowState == FormWindowState.Minimized)
		{
			_form.WindowState = FormWindowState.Normal;
		}
		_form.Activate();
		_form.RefreshView();
	}

	private void ToggleForm()
	{
		if (_form.Visible && _form.WindowState != FormWindowState.Minimized)
		{
			_form.Hide();
		}
		else
		{
			ShowForm();
		}
	}

	private void ExitApp()
	{
		_watch.Stop();
		ExitThread();
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing && !_disposed)
		{
			_disposed = true;
			SystemEvents.PowerModeChanged -= OnPowerModeChanged;
			_showWait.Unregister(null);
			_watch.Dispose();
			_tray.Visible = false;
			_tray.ContextMenuStrip?.Dispose();
			_tray.Dispose();
			_form.AllowClose = true;
			_form.Dispose();
			_icoPerf.Dispose();
			_icoEco.Dispose();
		}
		base.Dispose(disposing);
	}
}
