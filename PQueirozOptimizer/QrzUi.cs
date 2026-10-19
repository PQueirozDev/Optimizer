using System.Windows;

namespace PQueirozOptimizer;

/// <summary>
/// Propriedades anexadas do design system usadas pelos estilos (Themes/Controls.xaml): o raio de um botão
/// muda sem precisar de outro template (pílula da barra superior, botões redondos de ícone).
/// </summary>
public static class QrzUi
{
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius", typeof(CornerRadius), typeof(QrzUi), new FrameworkPropertyMetadata(new CornerRadius(8)));

    public static CornerRadius GetCornerRadius(DependencyObject d) => (CornerRadius)d.GetValue(CornerRadiusProperty);
    public static void SetCornerRadius(DependencyObject d, CornerRadius value) => d.SetValue(CornerRadiusProperty, value);
}
