using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace PQueirozOptimizer.NotebookEnergy;

internal static class Theme
{
	public static readonly Color Bg = Color.FromArgb(27, 30, 38);

	public static readonly Color Surface = Color.FromArgb(17, 19, 26);

	public static readonly Color Hover = Color.FromArgb(36, 40, 52);

	public static readonly Color Line = Color.FromArgb(42, 46, 56);

	public static readonly Color Text = Color.FromArgb(236, 238, 243);

	public static readonly Color Muted = Color.FromArgb(154, 160, 173);

	public static readonly Color Perf = Color.FromArgb(124, 156, 255);

	public static readonly Color Eco = Color.FromArgb(76, 201, 131);

	public static readonly Color Auto = Color.FromArgb(124, 156, 255);

	public static GraphicsPath Rounded(RectangleF r, float radius)
	{
		GraphicsPath graphicsPath = new GraphicsPath();
		float num = Math.Min(radius * 2f, Math.Min(r.Width, r.Height));
		graphicsPath.AddArc(r.X, r.Y, num, num, 180f, 90f);
		graphicsPath.AddArc(r.Right - num, r.Y, num, num, 270f, 90f);
		graphicsPath.AddArc(r.Right - num, r.Bottom - num, num, num, 0f, 90f);
		graphicsPath.AddArc(r.X, r.Bottom - num, num, num, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}

	public static void DrawBadge(Graphics g, RectangleF r, Effective e, bool forTray = false)
	{
		g.SmoothingMode = SmoothingMode.AntiAlias;
		Color color = ((e == Effective.Performance) ? Color.FromArgb(132, 164, 255) : Color.FromArgb(92, 214, 146));
		Color color2 = ((e == Effective.Performance) ? Color.FromArgb(70, 102, 230) : Color.FromArgb(32, 160, 95));
		using (GraphicsPath path = Rounded(r, r.Width * (forTray ? 0.24f : 0.28f)))
		{
			using LinearGradientBrush brush = new LinearGradientBrush(r, color, color2, 90f);
			g.FillPath(brush, path);
		}
		using (SolidBrush brush2 = new SolidBrush(Color.White))
		{
			if (e == Effective.Performance)
			{
				g.FillPolygon(brush2, new PointF[7]
				{
					P(0.57f, 0.12f),
					P(0.25f, 0.56f),
					P(0.47f, 0.56f),
					P(0.41f, 0.88f),
					P(0.76f, 0.42f),
					P(0.54f, 0.42f),
					P(0.62f, 0.12f)
				});
				return;
			}
			using GraphicsPath graphicsPath = new GraphicsPath();
			graphicsPath.AddBezier(P(0.24f, 0.78f), P(0.18f, 0.36f), P(0.48f, 0.18f), P(0.8f, 0.2f));
			graphicsPath.AddBezier(P(0.8f, 0.2f), P(0.82f, 0.56f), P(0.62f, 0.82f), P(0.24f, 0.78f));
			graphicsPath.CloseFigure();
			g.FillPath(brush2, graphicsPath);
			using Pen pen = new Pen(color2, Math.Max(1.2f, r.Width * 0.05f))
			{
				StartCap = LineCap.Round,
				EndCap = LineCap.Round
			};
			g.DrawLine(pen, P(0.3f, 0.72f), P(0.66f, 0.36f));
		}
		PointF P(float x, float y)
		{
			return new PointF(r.X + r.Width * x, r.Y + r.Height * y);
		}
	}

	public static Icon MakeTrayIcon(Effective e)
	{
		using Bitmap bitmap = new Bitmap(32, 32);
		using (Graphics graphics = Graphics.FromImage(bitmap))
		{
			graphics.Clear(Color.Transparent);
			DrawBadge(graphics, new RectangleF(1f, 1f, 30f, 30f), e, forTray: true);
		}
		nint handle = bitmap.GetHicon();
		try
		{
			using var icon = Icon.FromHandle(handle);
			return (Icon)icon.Clone();
		}
		finally { DestroyIcon(handle); }
	}

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool DestroyIcon(nint icon);
}
