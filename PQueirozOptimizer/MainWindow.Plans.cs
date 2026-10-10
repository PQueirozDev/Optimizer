using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>Planos Base, Intermediário e Avançado/Vitalício: o que fica bloqueado e como pedir o upgrade.</summary>
public partial class MainWindow
{
    private static LicenseInfo? CurrentLicense => (Application.Current as App)?.ActiveLicense;

    // Os mesmos títulos do menu lateral, para a tela de bloqueio
    private static readonly Dictionary<string, string> PageTitles = new()
    {
        ["optimization"] = "Otimizações", ["startup"] = "Inicialização", ["fixes"] = "Correções", ["tools"] = "Ferramentas",
        ["services"] = "Serviços", ["apps"] = "Apps", ["drivers"] = "Drivers", ["network"] = "Rede", ["resources"] = "Recursos",
        ["diagnostics"] = "Central de diagnóstico", ["smart"] = "Smart Optimize", ["perflab"] = "Performance Lab", ["profiles"] = "Perfis", ["gaming"] = "Modo Jogo", ["customize"] = "Personalizar Windows", ["bios"] = "BIOS / UEFI", ["biosadvisor"] = "BIOS Advisor",
    };

    private static bool PageAllowed(string page) => PlanAccess.Allows(CurrentLicense, page);

    /// <summary>O que cada nível acrescenta, para a tela de bloqueio e a seção Minha licença.</summary>
    internal static string TierFeatures(PlanTier tier) => tier switch
    {
        PlanTier.Base => "Otimizações, Inicialização, Correções, Ferramentas e Limpeza rápida.",
        PlanTier.Intermediate => "Tudo do Base + Serviços, Apps, Drivers, Rede, Recursos e Central de diagnóstico.",
        _ => "O app completo: tudo do Intermediário + Modo Jogo, Personalizar Windows, BIOS / UEFI e Modo de energia.",
    };

    /// <summary>Itens do menu fora do plano ficam esmaecidos com um aviso; o clique abre a tela de upgrade.</summary>
    private void UpdateNavLocks()
    {
        foreach (var button in new[] { NavPerfLab, NavSmart, NavProfiles, NavOpt, NavStartup, NavDrivers, NavTools, NavCustomize, NavGaming, NavNetwork, NavResources, NavFixes, NavDiagnostics, NavServices, NavApps, NavBios, NavBiosAdvisor })
        {
            var page = button.Tag?.ToString() ?? "";
            var allowed = PageAllowed(page);
            button.Opacity = allowed ? 1 : 0.5;
            button.ToolTip = allowed ? null : string.Format(Translator.Tr("Disponível a partir do plano {0}"), Translator.Tr(LicensePlans.TierName(PlanAccess.Required(page))));
        }
    }

    /// <summary>Copia o pedido de upgrade (titular, plano desejado e ID do PC) e abre o Discord para o ticket.</summary>
    private void RequestUpgrade(string plan)
    {
        var license = CurrentLicense;
        try { Clipboard.SetText(new LicenseService().BuildActivationRequest(license?.Licensee, plan)); }
        catch (System.Runtime.InteropServices.COMException) { OperationStatus.Text = "A área de transferência está ocupada. Tente novamente."; return; }
        OperationStatus.Text = string.Format(Translator.Tr("Pedido de upgrade para o plano {0} copiado. No Discord, abra um ticket, cole o pedido e pague via Pix."), Translator.Tr(plan));
        OpenUrl(LicensePlans.DiscordUrl);
    }

    /// <summary>Página fora do plano: explica o que falta e mostra os planos que a liberam, com o pedido de upgrade.</summary>
    // Operações em lote bloqueadas mostram o próprio nome, não o da página equivalente
    private static readonly Dictionary<string, string> OperationTitles = new()
    {
        ["debloat"] = "Debloat e privacidade", ["gamer"] = "Versão Avançada (jogos)", ["gamerservicos"] = "Avançada sem parar serviços", ["inteligente"] = "Otimização inteligente",
    };

    private void ShowLockedPage(string page, string? pageTitle = null)
    {
        var required = PlanAccess.Required(page);
        PageTitle.Text = pageTitle ?? PageTitles.GetValueOrDefault(page, "Recurso bloqueado");
        PageBadge.Visibility = Visibility.Collapsed;
        var root = new StackPanel();

        var head = new StackPanel();
        var top = new DockPanel();
        var chip = IconChip(Glyphs.Lock, "Warning", 44); DockPanel.SetDock(chip, Dock.Left); top.Children.Add(chip);
        var titles = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var title = Label(string.Format(Translator.Tr("Disponível a partir do plano {0}"), Translator.Tr(LicensePlans.TierName(required))), 17.5);
        title.FontWeight = FontWeights.Bold; title.Margin = new Thickness(0);
        titles.Children.Add(title);
        var current = CurrentLicense?.PlanName ?? "—";
        var subtitle = Label(string.Format(Translator.Tr("Seu plano atual é o {0}. Faça o upgrade para liberar esta página; o que você já aplicou continua funcionando e pode ser revertido em Atividade e reversão."), Translator.Tr(current)), 12.5, true);
        subtitle.Margin = new Thickness(0, 3, 0, 0);
        titles.Children.Add(subtitle);
        top.Children.Add(titles);
        head.Children.Add(top);
        var headCard = Surface(head); headCard.SetResourceReference(Border.BackgroundProperty, "HeroBrush");
        root.Children.Add(headCard);

        var plans = new UniformGrid { Columns = 2 };
        foreach (var (name, tier, price) in LicensePlans.ForSale.Where(p => p.Tier >= required))
        {
            var card = new StackPanel();
            var row = new DockPanel();
            var pill = Pill(Translator.Tr(price), tier == PlanTier.Full ? "Success" : "Accent"); DockPanel.SetDock(pill, Dock.Right); row.Children.Add(pill);
            var planName = Label(name, 15); planName.FontWeight = FontWeights.SemiBold; planName.Margin = new Thickness(0);
            row.Children.Add(planName);
            card.Children.Add(row);
            var features = Label(name == LicensePlans.Lifetime ? "O app completo, para sempre: pague uma vez, sem renovação." : TierFeatures(tier), 12.5, true);
            features.Margin = new Thickness(0, 8, 0, 14);
            card.Children.Add(features);
            var upgrade = IconButton(Glyphs.OpenInNew, string.Format(Translator.Tr("Quero o {0}"), Translator.Tr(name)), primary: name == LicensePlans.Lifetime);
            upgrade.HorizontalAlignment = HorizontalAlignment.Left;
            upgrade.ToolTip = "Copia o pedido de upgrade e abre o Discord: abra um ticket, cole o pedido e pague via Pix";
            upgrade.Click += (_, _) => RequestUpgrade(name);
            card.Children.Add(upgrade);
            var surface = Surface(card); surface.Margin = new Thickness(0, 0, 12, 12);
            plans.Children.Add(surface);
        }
        root.Children.Add(plans);

        var activate = IconButton(Glyphs.Key, "Já tenho a chave nova");
        activate.HorizontalAlignment = HorizontalAlignment.Left;
        activate.Click += (_, _) => ActivateAdminLicense();
        root.Children.Add(activate);

        ContentHost.Children.Clear();
        ContentHost.Children.Add(root);
    }
}
