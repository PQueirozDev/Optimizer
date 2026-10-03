using System.Windows;
using System.Windows.Controls.Primitives;

namespace PQueirozOptimizer;

/// <summary>Layout responsivo: grades reduzem colunas quando a janela fica estreita (1366x768 e menores).</summary>
public partial class MainWindow
{
    /// <summary>
    /// Ajusta as colunas da grade à largura da área de conteúdo: cabem quantas tiverem pelo menos
    /// <paramref name="minColumnWidth"/>, até <paramref name="maxColumns"/>. Sem textos cortados nem rolagem lateral.
    /// </summary>
    private T Responsive<T>(T grid, double minColumnWidth, int maxColumns) where T : UniformGrid
    {
        // Mede a largura da própria grade (que estica na horizontal e não muda com o número de colunas),
        // então funciona também dentro de colunas menores, como metade da página
        void Fit()
        {
            var width = grid.ActualWidth > 0 ? grid.ActualWidth : ContentHost.ActualWidth;
            var columns = width <= 0 ? maxColumns : Math.Clamp((int)(width / minColumnWidth), 1, maxColumns);
            if (grid.Columns != columns) grid.Columns = columns;
        }
        grid.SizeChanged += (_, _) => Fit();
        Fit();
        return grid;
    }
}
