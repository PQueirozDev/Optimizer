using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace PQueirozOptimizer.Services;

/// <summary>
/// Tradução central português → inglês. O app é escrito em português; quando o idioma é
/// inglês, todo TextBlock que aparece na tela (páginas, botões, dicas, listas e a saída do
/// script) passa por aqui, inclusive quando o texto muda depois de exibido.
/// </summary>
public static partial class Translator
{
    public static bool IsEnglish { get; set; }

    // Guarda no próprio TextBlock o último texto já processado, para cada texto ser traduzido uma vez só
    private static readonly DependencyProperty ProcessedProperty = DependencyProperty.RegisterAttached("Processed", typeof(string), typeof(Translator));
    private static readonly FrameworkElement LayoutProbe = new();
    private static bool _scheduled;

    /// <summary>Registra o gancho global. Chamar uma vez, antes de abrir qualquer janela.</summary>
    public static void Attach()
    {
        // LayoutUpdated de qualquer elemento dispara a cada passada de layout do Dispatcher,
        // inclusive de janelas, dicas e listas suspensas: basta um elemento "sonda".
        LayoutProbe.LayoutUpdated -= OnLayoutUpdated;
        LayoutProbe.LayoutUpdated += OnLayoutUpdated;
    }

    private static void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (!IsEnglish || _scheduled) return;
        _scheduled = true;
        Application.Current?.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, TranslateAllWindows);
    }

    /// <summary>Traduz tudo o que estiver visível agora (janelas e popups).</summary>
    public static void TranslateAllWindows()
    {
        _scheduled = false;
        if (!IsEnglish) return;
        foreach (PresentationSource source in PresentationSource.CurrentSources)
        {
            if (source.RootVisual is not DependencyObject root) continue;
            if (root is Window window) window.Title = Tr(window.Title);
            Walk(root);
        }
    }

    private static void Walk(DependencyObject node)
    {
        if (node is TextBlock tb) Apply(tb);
        if (node is FrameworkElement { ToolTip: string tip } element && Tr(tip) is var tipEn && tipEn != tip) element.ToolTip = tipEn;
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++) Walk(System.Windows.Media.VisualTreeHelper.GetChild(node, i));
    }

    private static void Apply(TextBlock tb)
    {
        var text = tb.Text;
        if (string.IsNullOrWhiteSpace(text) || text == (string?)tb.GetValue(ProcessedProperty)) return;
        var translated = Tr(text);
        // SetCurrentValue preserva bindings (ex.: conteúdo de botões gerado pelo template)
        if (translated != text) tb.SetCurrentValue(TextBlock.TextProperty, translated);
        tb.SetValue(ProcessedProperty, translated);
    }

    /// <summary>Traduz um texto do português para o inglês quando o idioma ativo é inglês.</summary>
    public static string Tr(string? text)
    {
        if (!IsEnglish || string.IsNullOrWhiteSpace(text)) return text ?? "";
        return TranslateCore(text) ?? text;
    }

    private static string? TranslateCore(string text)
    {
        var core = text.Trim();
        if (core.Length == 0) return null;
        var lead = text[..text.IndexOf(core[0])];
        var trail = text[(lead.Length + core.Length)..];
        var result = TranslateTrimmed(core);
        return result is null ? null : lead + result + trail;
    }

    private static readonly Regex SymbolPrefix = new(@"^([^\p{L}\p{N}'""(\[]+)(.+)$", RegexOptions.Compiled);

    private static string? TranslateTrimmed(string core)
    {
        if (Exact.TryGetValue(core, out var exact)) return exact;

        foreach (var (regex, build) in Patterns)
        {
            var m = regex.Match(core);
            if (m.Success) return build(m);
        }

        // Texto com vários trechos: "A · B", várias linhas, "A + B"
        foreach (var separator in new[] { "\n", " · ", " • ", " + ", " — " })
        {
            if (!core.Contains(separator)) continue;
            var parts = core.Split(separator);
            var translated = parts.Select(p => TranslateCore(p)).ToArray();
            if (translated.All(t => t is null)) continue;
            return string.Join(separator, parts.Select((p, i) => translated[i] ?? p));
        }

        // Ícone/símbolo na frente: "● Disponível", "+ Adicionar", "⬇ Baixar"
        var symbol = SymbolPrefix.Match(core);
        if (symbol.Success && symbol.Groups[1].Value.Trim().Length > 0 && TranslateCore(symbol.Groups[2].Value) is { } rest)
            return symbol.Groups[1].Value + rest;

        // "Rótulo: valor" em que só o rótulo é conhecido
        var colon = core.IndexOf(": ", StringComparison.Ordinal);
        if (colon > 0 && Exact.TryGetValue(core[..colon], out var label))
            return label + ": " + (TranslateCore(core[(colon + 2)..]) ?? core[(colon + 2)..]);
        if (core.EndsWith(':') && Exact.TryGetValue(core[..^1], out var labelOnly)) return labelOnly + ":";

        return null;
    }

    private static string T(Match m, int group) => Tr(m.Groups[group].Value);
    private static (Regex, Func<Match, string>) P(string pattern, Func<Match, string> build) => (new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant), build);
}
