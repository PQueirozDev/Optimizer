using System.Drawing;
using System.Windows.Forms;

namespace PQueirozOptimizer.NotebookEnergy;

internal class Badge : Control
{
	private Effective _mode;

	public Effective Mode
	{
		get
		{
			return _mode;
		}
		set
		{
			_mode = value;
			Invalidate();
		}
	}

	public Badge()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		BackColor = Theme.Bg;
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Theme.DrawBadge(e.Graphics, new RectangleF(0.5f, 0.5f, base.Width - 1, base.Height - 1), _mode);
	}
}
