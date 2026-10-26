using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>
/// Ajuda contextual das funções avançadas: um "?" ao lado do título abre o que a função faz, quando usar,
/// o benefício esperado, os riscos e como desfazer. Só texto: abrir a ajuda nunca altera nada.
/// </summary>
public partial class MainWindow
{
    internal sealed record FeatureHelp(string What, string When, string Benefits, string Risks, string Undo);

    internal static readonly Dictionary<string, FeatureHelp> FeatureHelps = new()
    {
        ["services"] = new(
            "Desliga grupos de serviços do Windows (atualizações, telemetria, impressão, Bluetooth, acesso remoto...) e guarda o tipo de início de cada um.",
            "Quando você não usa o recurso do grupo. Em PC de trabalho ou compartilhado, prefira deixar como está.",
            "Menos processos em segundo plano. O ganho de FPS costuma ser pequeno e varia de PC para PC.",
            "O recurso do grupo para de funcionar. Bloquear o Windows Update deixa o PC sem correções de segurança enquanto estiver desligado.",
            "Desmarque o grupo: os serviços voltam ao tipo de início guardado. O estado atual de todos aparece na aba Estado dos serviços."),
        ["dns"] = new(
            "Troca o servidor DNS dos adaptadores conectados, guardando o DNS de cada um.",
            "Quando sites e o login nos jogos demoram para abrir, ou para usar um DNS com filtro (ex.: bloqueio de anúncios).",
            "Resolução de nomes mais rápida. Não muda a velocidade da internet nem o ping dentro da partida.",
            "Redes corporativas ou com DNS próprio (escola, empresa) podem parar de achar servidores internos.",
            "Escolha \"Automático (provedor)\": cada adaptador volta ao DNS que tinha."),
        ["network-repair"] = new(
            "Limpa o cache DNS, sincroniza o relógio ou reinicia o Winsock e a pilha TCP/IP.",
            "Quando a internet cai, sites não abrem ou o jogo não conecta, mesmo com a rede ligada.",
            "Resolve a maioria dos problemas de configuração de rede corrompida.",
            "Redefinir a rede apaga configurações manuais de IP e de VPN e pede reiniciar o PC.",
            "Limpar o DNS e sincronizar o relógio não precisam ser desfeitos. A redefinição não tem volta automática: anote IPs manuais antes."),
        ["defender"] = new(
            "Exclui pastas de jogos da verificação em tempo real ou desliga a proteção em tempo real.",
            "Exclusões: quando o jogo trava ao carregar arquivos. Desligar a proteção só em último caso e por pouco tempo.",
            "Menos leitura de disco durante o carregamento do jogo.",
            "Uma pasta excluída não é verificada até a exclusão ser removida. Desligar a proteção deixa o PC sem antivírus em tempo real.",
            "Remova a exclusão na mesma tela ou religue a proteção. O Windows também religa a proteção sozinho depois de um tempo."),
        ["win32priority"] = new(
            "Muda o quanto o Windows favorece o programa em primeiro plano (Win32PrioritySeparation).",
            "Para testar se o jogo fica mais estável com a janela em foco. Meça no Laboratório antes e depois.",
            "Pode reduzir pequenas variações de frametime em alguns PCs. Não há ganho garantido.",
            "Programas em segundo plano (gravação, chamada de voz) podem receber menos tempo de CPU.",
            "\"Restaurar original\" volta ao valor que estava antes. Vale na hora, sem reiniciar."),
        ["bios-groups"] = new(
            "Grava opções da BIOS com o SCEWIN, só nos grupos escolhidos, mostrando o valor atual e o novo.",
            "Quando você sabe o que cada opção faz na sua placa e já testou o PC estável.",
            "Ajustes de memória, energia e PCIe que o Windows não alcança.",
            "Uma combinação errada pode impedir o PC de ligar até limpar a CMOS. Atualizar a BIOS invalida a cópia original.",
            "\"Restaurar original\" grava a cópia feita antes da primeira alteração, se a BIOS for a mesma. Em último caso, limpe a CMOS."),
        ["game-profiles"] = new(
            "Ajustes que o Windows aplica sempre que o jogo abrir: GPU preferida, otimizações de tela cheia e prioridade de CPU.",
            "Para um jogo específico que roda na GPU errada ou com prioridade baixa.",
            "O jogo usa a placa dedicada e recebe prioridade sem você mexer em nada a cada partida.",
            "Prioridade alta demais pode deixar outros programas lentos durante o jogo.",
            "Remova o perfil do jogo: as chaves do Windows são apagadas e o jogo volta ao padrão."),
        ["quick-tweaks"] = new(
            "Plano de energia Qrz, limpeza de arquivos temporários e outros ajustes pontuais.",
            "Plano Qrz: em desktop, para jogar. Em notebook na bateria, prefira o Equilibrado.",
            "Plano Qrz: núcleos não entram em economia, o que reduz atrasos ao acordar a CPU.",
            "Mais consumo e calor. Em Ryzen X3D com dois CCDs, o jogo pode ir para o CCD sem 3D V-Cache.",
            "Desative o plano Qrz: o plano anterior volta e o Qrz é removido."),
    };

    /// <summary>Botão "?" que abre a ajuda da função. Só leitura.</summary>
    private Button HelpButton(string title, FeatureHelp help)
    {
        var button = new Button { Content = GlyphIcon(Glyphs.Info, 12, "MutedBrush"), ToolTip = Translator.Tr("Como funciona"), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        button.SetResourceReference(StyleProperty, "RoundIconButton");
        System.Windows.Automation.AutomationProperties.SetName(button, Translator.Tr("Como funciona") + ": " + Translator.Tr(title));
        button.Click += (_, _) =>
        {
            var popup = new Popup { PlacementTarget = button, Placement = PlacementMode.Bottom, VerticalOffset = 6, StaysOpen = false, AllowsTransparency = true, PopupAnimation = AppearanceService.AnimationsEnabled ? PopupAnimation.Fade : PopupAnimation.None };
            popup.Child = HelpCard(title, help);
            popup.IsOpen = true;
        };
        return button;
    }

    private static Border HelpCard(string title, FeatureHelp help)
    {
        var panel = new StackPanel { Width = 360 };
        var head = new TextBlock { Text = Translator.Tr(title), FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
        head.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        head.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        panel.Children.Add(head);
        foreach (var (label, text, tone) in new[]
        {
            ("O QUE FAZ", help.What, "MutedBrush"), ("QUANDO USAR", help.When, "MutedBrush"), ("BENEFÍCIO ESPERADO", help.Benefits, "MutedBrush"),
            ("RISCOS", help.Risks, "WarningBrush"), ("COMO DESFAZER", help.Undo, "MutedBrush"),
        })
        {
            var l = new TextBlock { Text = Translator.Tr(label), FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 2) };
            l.SetResourceReference(TextBlock.ForegroundProperty, tone);
            var t = new TextBlock { Text = Translator.Tr(text), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, LineHeight = 18 };
            t.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            panel.Children.Add(l); panel.Children.Add(t);
        }
        var card = new Border { Child = panel, Padding = new Thickness(16, 14, 16, 16), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "CardElevatedBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        return card;
    }

    /// <summary>Cabeçalho de seção com o "?" da ajuda ao lado do título.</summary>
    private StackPanel SectionHeader(string title, string? subtitle, string helpId)
    {
        var header = SectionHeader(title, subtitle);
        if (!FeatureHelps.TryGetValue(helpId, out var help) || header.Children[0] is not TextBlock titleText) return header;
        header.Children.RemoveAt(0);
        var row = new DockPanel();
        var button = HelpButton(title, help);
        DockPanel.SetDock(button, Dock.Left);
        row.Children.Add(titleText);
        DockPanel.SetDock(titleText, Dock.Left);
        row.Children.Add(button);
        row.LastChildFill = false;
        header.Children.Insert(0, row);
        return header;
    }
}
