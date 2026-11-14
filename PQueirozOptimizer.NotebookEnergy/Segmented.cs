using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace PQueirozOptimizer.NotebookEnergy;

internal class Segmented : Control
{
	private readonly string[] _items;

	private int _selected;

	private int _hover = -1;

	public int Selected
	{
		get
		{
			return _selected;
		}
		set
		{
			if (value != _selected)
			{
				_selected = value;
				Invalidate();
				this.SelectionChanged?.Invoke(this, EventArgs.Empty);
			}
		}
	}

	public event EventHandler? SelectionChanged;

	public Segmented(params string[] items)
	{
		_items = items;
		SetStyle(ControlStyles.UserPaint | ControlStyles.Selectable | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		BackColor = Theme.Bg;
		Cursor = Cursors.Hand;
		base.TabStop = true;
		base.AccessibleRole = AccessibleRole.PageTabList;
	}

	private RectangleF ItemRect(int i)
	{
		float num = (float)base.Height * 0.1f;
		float num2 = ((float)base.Width - num * 2f) / (float)_items.Length;
		return new RectangleF(num + num2 * (float)i, num, num2, (float)base.Height - num * 2f);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
		using (GraphicsPath path = Theme.Rounded(new RectangleF(0f, 0f, base.Width - 1, base.Height - 1), (float)base.Height * 0.28f))
		{
			using SolidBrush brush = new SolidBrush(Theme.Surface);
			graphics.FillPath(brush, path);
		}
		for (int i = 0; i < _items.Length; i++)
		{
			RectangleF rectangleF = ItemRect(i);
			bool flag = i == _selected;
			if (flag || i == _hover)
			{
				Color color = ((!flag) ? Theme.Hover : ((i == 2) ? Theme.Eco : Theme.Perf));
				using GraphicsPath path2 = Theme.Rounded(rectangleF, rectangleF.Height * 0.3f);
				using SolidBrush brush2 = new SolidBrush(color);
				graphics.FillPath(brush2, path2);
			}
			Color foreColor = (flag ? Theme.Surface : Theme.Muted);
			TextRenderer.DrawText(graphics, _items[i], Font, Rectangle.Round(rectangleF), foreColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
		}
		if (!Focused || !ShowFocusCues)
		{
			return;
		}
		using Pen pen = new Pen(Theme.Perf, 2f);
		using GraphicsPath path3 = Theme.Rounded(new RectangleF(1f, 1f, base.Width - 3, base.Height - 3), (float)base.Height * 0.28f);
		graphics.DrawPath(pen, path3);
	}

	private int HitTest(Point pt)
	{
		for (int i = 0; i < _items.Length; i++)
		{
			if (ItemRect(i).Contains(pt))
			{
				return i;
			}
		}
		return -1;
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		int num = HitTest(e.Location);
		if (num != _hover)
		{
			_hover = num;
			Invalidate();
		}
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		_hover = -1;
		Invalidate();
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		Focus();
		int num = HitTest(e.Location);
		if (num >= 0)
		{
			Selected = num;
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

	protected override bool IsInputKey(Keys k)
	{
		if ((k != Keys.Left && k != Keys.Right) || 1 == 0)
		{
			return base.IsInputKey(k);
		}
		return true;
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		if (e.KeyCode == Keys.Left && _selected > 0)
		{
			Selected--;
		}
		if (e.KeyCode == Keys.Right && _selected < _items.Length - 1)
		{
			Selected++;
		}
	}
}
