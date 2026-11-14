using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PQueirozOptimizer.NotebookEnergy;

internal class Toggle : Control
{
	private bool _checked;

	public bool Checked
	{
		get
		{
			return _checked;
		}
		set
		{
			if (_checked != value)
			{
				_checked = value;
				Invalidate();
				this.CheckedChanged?.Invoke(this, EventArgs.Empty);
			}
		}
	}

	public event EventHandler? CheckedChanged;

	public Toggle(string text)
	{
		Text = text;
		SetStyle(ControlStyles.UserPaint | ControlStyles.Selectable | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		BackColor = Theme.Bg;
		ForeColor = Theme.Text;
		Cursor = Cursors.Hand;
		base.TabStop = true;
		base.AccessibleRole = AccessibleRole.CheckButton;
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		float num = Math.Min(base.Height - 4, (float)base.Height * 0.62f);
		float num2 = num * 1.75f;
		RectangleF rectangleF = new RectangleF((float)base.Width - num2 - 2f, ((float)base.Height - num) / 2f, num2, num);
		using (GraphicsPath path = Theme.Rounded(rectangleF, num / 2f))
		{
			using SolidBrush brush = new SolidBrush(_checked ? Theme.Eco : Theme.Line);
			graphics.FillPath(brush, path);
		}
		float num3 = num - 4f;
		float x = (_checked ? (rectangleF.Right - num3 - 2f) : (rectangleF.X + 2f));
		using (SolidBrush brush2 = new SolidBrush(Color.White))
		{
			graphics.FillEllipse(brush2, x, rectangleF.Y + 2f, num3, num3);
		}
		if (Focused && ShowFocusCues)
		{
			using Pen pen = new Pen(Theme.Perf, 2f);
			using GraphicsPath path2 = Theme.Rounded(RectangleF.Inflate(rectangleF, 2f, 2f), num / 2f + 2f);
			graphics.DrawPath(pen, path2);
		}
		TextRenderer.DrawText(graphics, Text, Font, new Rectangle(0, 0, (int)(rectangleF.X - 8f), base.Height), ForeColor, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
	}

	protected override void OnClick(EventArgs e)
	{
		Focus();
		Checked = !Checked;
		base.OnClick(e);
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		Keys keyCode = e.KeyCode;
		if ((keyCode == Keys.Return || keyCode == Keys.Space) ? true : false)
		{
			Checked = !Checked;
		}
	}

	protected override void OnGotFocus(EventArgs e)
	{
		Invalidate();
	}

	protected override void OnLostFocus(EventArgs e)
	{
		Invalidate();
	}
}
