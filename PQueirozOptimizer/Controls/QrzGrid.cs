using System.Windows;
using System.Windows.Controls;

namespace PQueirozOptimizer.Controls;

/// <summary>
/// Grade responsiva (QrzGrid): cabem quantas colunas tiverem pelo menos <see cref="MinColumnWidth"/>, até
/// <see cref="MaxColumns"/>. Cada item ocupa <c>QrzGrid.Span</c> colunas (limitado às que existem) e os itens de
/// uma linha ficam com a mesma altura, como os cards da referência. O encaixe é "denso": um item pequeno que vem
/// depois preenche o buraco deixado por um grande que não coube na linha, então não sobram vãos em telas médias.
/// </summary>
public sealed class QrzGrid : Panel
{
    public static readonly DependencyProperty SpanProperty = DependencyProperty.RegisterAttached("Span", typeof(int), typeof(QrzGrid),
        new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsParentMeasure));
    public static int GetSpan(UIElement element) => (int)element.GetValue(SpanProperty);
    public static void SetSpan(UIElement element, int value) => element.SetValue(SpanProperty, value);

    public double MinColumnWidth { get; set; } = 300;
    public int MaxColumns { get; set; } = 4;
    public double ColumnGap { get; set; } = 16;
    public double RowGap { get; set; } = 16;

    /// <summary>Colunas em uso na última medida (para quem quiser reagir à quebra).</summary>
    public int Columns { get; private set; } = 1;

    private readonly List<(UIElement Element, int Row, int Column, int Span)> _placement = new();
    private readonly List<double> _rowHeights = new();
    private double _columnWidth;

    private int ColumnsFor(double width) =>
        double.IsInfinity(width) || width <= 0 ? MaxColumns : Math.Clamp((int)((width + ColumnGap) / (MinColumnWidth + ColumnGap)), 1, Math.Max(1, MaxColumns));

    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? MaxColumns * (MinColumnWidth + ColumnGap) - ColumnGap : available.Width;
        Columns = ColumnsFor(width);
        _columnWidth = Math.Max(0, (width - ColumnGap * (Columns - 1)) / Columns);
        _placement.Clear(); _rowHeights.Clear();
        var occupied = new List<bool[]>();
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            var span = Math.Clamp(GetSpan(child), 1, Columns);
            // Primeiro lugar (de cima para baixo, da esquerda para a direita) onde o item cabe inteiro
            int row = 0, column = 0;
            for (; ; row++)
            {
                if (row == occupied.Count) occupied.Add(new bool[Columns]);
                column = FirstFit(occupied[row], span);
                if (column >= 0) break;
            }
            for (var c = column; c < column + span; c++) occupied[row][c] = true;
            _placement.Add((child, row, column, span));
            child.Measure(new Size(span * _columnWidth + (span - 1) * ColumnGap, double.PositiveInfinity));
            while (_rowHeights.Count <= row) _rowHeights.Add(0);
            _rowHeights[row] = Math.Max(_rowHeights[row], child.DesiredSize.Height);
        }
        var height = _rowHeights.Sum() + RowGap * Math.Max(0, _rowHeights.Count - 1);
        return new Size(double.IsInfinity(available.Width) ? width : available.Width, height);
    }

    private static int FirstFit(bool[] cells, int span)
    {
        for (var start = 0; start + span <= cells.Length; start++)
        {
            var free = true;
            for (var c = start; c < start + span && free; c++) free = !cells[c];
            if (free) return start;
        }
        return -1;
    }

    protected override Size ArrangeOverride(Size final)
    {
        var tops = new double[_rowHeights.Count];
        for (var r = 1; r < tops.Length; r++) tops[r] = tops[r - 1] + _rowHeights[r - 1] + RowGap;
        foreach (var (element, row, column, span) in _placement)
        {
            var x = column * (_columnWidth + ColumnGap);
            element.Arrange(new Rect(x, tops[row], span * _columnWidth + (span - 1) * ColumnGap, _rowHeights[row]));
        }
        return final;
    }
}
