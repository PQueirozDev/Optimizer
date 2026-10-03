using System.Windows;
using PQueirozOptimizer.Models;

namespace PQueirozOptimizer.Services;

/// <summary>
/// Gerenciador de aparência: guarda as preferências em uso, aplica o tema e avisa a interface quando algo muda.
/// Os componentes desenhados em código leem daqui os tamanhos que dependem de densidade e tamanho dos cards.
/// </summary>
public static class AppearanceService
{
    public static AppearanceSettings Current { get; private set; } = new();

    /// <summary>Disparado depois de aplicar novas preferências (a janela redesenha a página atual).</summary>
    public static event Action? Changed;

    /// <summary>
    /// Animações decorativas, conforme a preferência do app. A opção de animações do Windows não é usada: a
    /// própria otimização de efeitos visuais do app a desliga (mesma decisão da animação de abertura na 1.7.2).
    /// </summary>
    public static bool AnimationsEnabled => Current.Animations;

    public static double DensityFactor => Current.Density switch { Density.Compact => 0.78, Density.Comfortable => 1.22, _ => 1.0 };

    public static double Space(double value) => Math.Round(value * DensityFactor);

    public static Thickness CardPadding => new(Space(22));

    public static double Gap => Space(14);

    /// <summary>Escala dos cards de monitoramento e hardware (Compacto/Médio/Grande).</summary>
    public static double CardScale => Current.CardSize switch { CardSize.Compact => 0.82, CardSize.Large => 1.2, _ => 1.0 };

    public static void Apply(AppearanceSettings settings, bool forceLight = false)
    {
        Current = settings.Clone();
        if (Application.Current is { } app) ThemeService.Apply(app.Resources, Current, forceLight);
        Changed?.Invoke();
    }

    /// <summary>Valores de fábrica: Escuro, roxo Padrão, densidade Padrão, cards Médios, animações ligadas.</summary>
    public static AppearanceSettings Defaults() => new();
}
