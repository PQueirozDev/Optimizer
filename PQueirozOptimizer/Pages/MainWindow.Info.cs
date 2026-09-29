using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;
public partial class MainWindow
{
    private void ActivateAdminLicense()
    {
        var service = new LicenseService();
        var activation = new ActivationWindow(service) { Owner = this };
        if (activation.ShowDialog() == true && service.TryGetActiveLicense(out var license, out _) && license is not null)
        {
            (Application.Current as App)?.SetActiveLicense(license);
            UpdateLicenseUi();
            // A mesma tela serve para liberar os recursos de administrador e para renovar a licença
            Msg(license.IsAdmin ? "Chave ativada. Os recursos de administrador já estão disponíveis." : "Chave ativada. Sua licença foi atualizada.", "Ativação concluída", MessageBoxButton.OK, MessageBoxImage.Information);
            NavigateTo("dashboard");
        }
    }

    private static readonly (string Version, string Date, string[] Notes)[] PatchNotes =
    {
        ("v1.6.0", "29/09/2026", new[]
        {
            "Licença perto de vencer: o painel avisa nos últimos 7 dias, com \"Copiar pedido de renovação\" e \"Ativar nova chave\".",
            "Licença vencida ou recusada: a tela de ativação explica o motivo e já gera o pedido de renovação com o titular.",
            "Chaves revogadas pelo suporte deixam de funcionar ao abrir o aplicativo com internet.",
            "Atrasar o relógio do Windows não estende mais licenças com validade; a hora da internet corrige relógios adiantados.",
            "Os campos da chave e do ID na tela de ativação não cortam mais o texto.",
        }),
        ("v1.5.0", "28/09/2026", new[]
        {
            "Nova página Inicialização no estilo do Autoruns: itens de logon, tarefas agendadas e serviços, com editor, assinatura digital verificada e liga/desliga reversível.",
            "Atalho de modo de energia na Área de Trabalho (Ferramentas): troca entre eficiência, equilibrado e desempenho sem pedir administrador — ideal para notebooks.",
            "Operações longas podem ser canceladas, e erros inesperados não fecham mais o aplicativo.",
            "Painel: discos grandes não aparecem mais com 0% livre, e a memória mostra o valor instalado (16 GB, não 15,9 GB).",
            "Debloat: tarefas de telemetria ausentes no Windows 11 não interrompem mais a etapa, e o OneDrive é removido de verdade no Windows 11.",
            "Versão Padrão: mantém um plano de energia de desempenho já ativo, recria o Alto Desempenho quando apagado e a limpeza de disco agora limpa de fato.",
            "A revisão de ajustes mostra o que tem backup, o que não é reversível e o que é ação pontual; a análise ficou colorida e mais precisa.",
            "Ativação mais fácil: \"Copiar pedido\" gera a mensagem com o ID do computador, e a chave copiada é colada sozinha.",
        }),
        ("v1.4.0", "24/09/2026", new[]
        {
            "Análise do PC mostra placa-mãe, BIOS, memória (XMP/EXPO e dual channel), temperatura e Integridade de Memória.",
            "Nova medição de latência DPC e interrupções na análise e no benchmark, com comparação antes → depois.",
            "Versão Avançada ativa o modo MSI na placa de vídeo (reversível).",
            "Efeitos visuais e precisão do ponteiro agora são aplicados na hora; antes só mudavam o registro.",
            "Serviços já removidos do Windows não geram mais falha, e o plano Desempenho Máximo não é mais duplicado.",
            "O aplicativo abre uma única vez: clicar de novo traz a janela já aberta para frente.",
        }),
        ("v1.3.0", "23/09/2026", new[]
        {
            "Todo o aplicativo agora muda de idioma, incluindo mensagens e a saída das operações.",
            "Saída completa das operações ao vivo, com cores, contadores e botão para copiar o resultado.",
            "Nova página Inicialização do Windows para ativar e desativar programas que abrem com o PC.",
            "Corrige a Versão Avançada, que apagava configurações do agendador multimídia; nova ferramenta Reparar Configurações do Windows.",
            "Novos ajustes reversíveis: anúncios e apps automáticos, Delivery Optimization, jogos em janela, Power Throttling, ID de publicidade, Bing, histórico de atividades e Copilot.",
            "SFC, DISM e CHKDSK exibem a saída corretamente; benchmark de disco usa o winsat (sem cache).",
        }),
        ("v1.2.0", "23/09/2026", new[]
        {
            "Visual novo: tema escuro/claro redesenhado, ícones na navegação e painel com pontuação de saúde.",
            "Atualização automática verificada por SHA256; o aplicativo reabre sozinho ao terminar.",
            "Backups de reversão protegidos contra alteração por outros usuários do Windows.",
            "Rodar várias otimizações seguidas agora reverte para o estado original.",
            "TRIM só em SSDs, GPU Scheduling só em placas compatíveis e SysMain preservado em HDs.",
            "Ponto de restauração criado mesmo quando o Windows já criou um nas últimas 24h.",
            "Licença vinculada ao ID do Windows (chaves antigas continuam válidas).",
        }),
        ("v1.1.1", "17/09/2026", new[]
        {
            "Ativação por chave assinada vinculada ao computador.",
            "Plano de energia com nome PQueiroz Optimizer.",
            "Redução de latência para mouse, teclado e USB.",
            "Melhorias nas políticas avançadas de privacidade e desempenho.",
        }),
        ("v1.0.5", "16/09/2026", new[]
        {
            "Modo Avançado com políticas de privacidade e desempenho.",
            "Backups, histórico e reversão de alterações.",
            "Verificação de atualização ao abrir o aplicativo.",
        }),
    };

    private void ShowPatchNotes()
    {
        PageTitle.Text = "Patch notes"; PageBadge.Visibility = Visibility.Visible; PageBadgeText.Text = "Versão atual " + AppVersion;
        var root = new StackPanel { MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };
        for (var i = 0; i < PatchNotes.Length; i++)
        {
            var (version, date, notes) = PatchNotes[i];
            var content = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            var title = Label(version, 20); title.Margin = new Thickness(0); title.VerticalAlignment = VerticalAlignment.Center;
            title.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            head.Children.Add(title);
            if (i == 0) { var latest = Pill("Mais recente", "Success"); latest.Margin = new Thickness(12, 0, 0, 0); head.Children.Add(latest); }
            var when = Label(date, 12, true); when.Margin = new Thickness(12, 0, 0, 0); when.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(when);
            content.Children.Add(head);
            foreach (var note in notes)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                var mark = GlyphIcon(Glyphs.Check, 12, i == 0 ? "AccentBrush" : "MutedBrush"); mark.VerticalAlignment = VerticalAlignment.Top; mark.Margin = new Thickness(0, 3, 12, 0);
                row.Children.Add(mark);
                var text = Label(note, 13, i != 0); text.Margin = new Thickness(0);
                row.Children.Add(text);
                content.Children.Add(row);
            }
            var card = Surface(content);
            if (i == 0) card.SetResourceReference(Border.BorderBrushProperty, "AccentSoftBrush");
            root.Children.Add(card);
        }
        ContentHost.Children.Clear(); ContentHost.Children.Add(root);
    }

    private void ShowBios()
    {
        PageTitle.Text = "BIOS / UEFI"; PageBadge.Visibility = Visibility.Collapsed;
        var root = new StackPanel { MaxWidth = 1000, HorizontalAlignment = HorizontalAlignment.Left };

        var hero = new DockPanel();
        var open = IconButton(Glyphs.Refresh, "Reiniciar na BIOS/UEFI", primary: true);
        open.VerticalAlignment = VerticalAlignment.Center; open.Margin = new Thickness(16, 0, 0, 0);
        open.Click += (_, _) =>
        {
            var answer = Msg(
                "O computador será reiniciado AGORA e abrirá diretamente as configurações de firmware (BIOS/UEFI).\n\nSalve seus trabalhos e feche outros programas antes de continuar.\n\nDeseja reiniciar agora?",
                "Reiniciar na BIOS/UEFI", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;
            if (_operationRunning) { OperationStatus.Text = "Aguarde a operação em andamento terminar antes de reiniciar."; return; }
            try
            {
                _log.Write("INFO", "Reinício para a BIOS/UEFI solicitado pelo usuário.");
                Process.Start(new ProcessStartInfo("shutdown.exe", "/r /fw /t 5") { UseShellExecute = true, CreateNoWindow = true });
            }
            catch (Exception ex) { Msg("Não foi possível reiniciar na BIOS: " + ex.Message, "BIOS / UEFI", MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
        DockPanel.SetDock(open, Dock.Right); hero.Children.Add(open);
        var chip = IconChip(Glyphs.Chip, "Accent", 52); DockPanel.SetDock(chip, Dock.Left); hero.Children.Add(chip);
        var heroText = new StackPanel { Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var ht = Label("Assistente seguro de BIOS", 20); ht.Margin = new Thickness(0, 0, 0, 4);
        heroText.Children.Add(ht);
        var hs = Label("O Optimizer não grava firmware. Aqui estão os ajustes que costumam trazer ganho real — faça-os manualmente, conferindo o manual da sua placa-mãe.", 12.5, true); hs.Margin = new Thickness(0);
        heroText.Children.Add(hs);
        hero.Children.Add(heroText);
        var heroCard = Surface(hero); heroCard.SetResourceReference(Border.BackgroundProperty, "HeroBrush");
        root.Children.Add(heroCard);

        var tips = new[]
        {
            (Glyphs.Memory, "Info", "XMP / EXPO", "Faz a memória rodar na velocidade anunciada. Costuma dar o maior ganho em jogos."),
            (Glyphs.Monitor, "Accent", "Resizable BAR", "Permite à placa de vídeo acessar toda a VRAM de uma vez. Exige CSM desativado."),
            (Glyphs.Speed, "Success", "Modo de energia", "Mantenha C-States habilitados em notebooks; em desktops, o perfil padrão da placa já é bom."),
            (Glyphs.Shield, "Warning", "Atualização de BIOS", "Use apenas o atualizador oficial da fabricante e nunca desligue o PC durante o processo."),
        };
        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -14, 0) };
        foreach (var (glyph, tone, title, text) in tips)
        {
            var body = new DockPanel();
            var c = IconChip(glyph, tone, 40); c.VerticalAlignment = VerticalAlignment.Top; DockPanel.SetDock(c, Dock.Left); body.Children.Add(c);
            var t = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
            var tt = Label(title, 14); tt.FontWeight = FontWeights.SemiBold; tt.Margin = new Thickness(0, 0, 0, 4);
            t.Children.Add(tt);
            var td = Label(text, 12, true); td.Margin = new Thickness(0);
            t.Children.Add(td);
            body.Children.Add(t);
            var card = Surface(body); card.Margin = new Thickness(0, 0, 14, 14);
            grid.Children.Add(card);
        }
        root.Children.Add(grid);
        ContentHost.Children.Clear(); ContentHost.Children.Add(root);
    }

    #region About Page
    private const string AuthorSiteUrl = "https://pqueiroz.vercel.app/";

    private void ShowAbout()
    {
        PageTitle.Text = _loc.T("Sobre", "About");
        PageBadge.Visibility = Visibility.Collapsed;
        var root = new StackPanel { MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };

        var hero = new DockPanel();
        var logo = new Border { Width = 72, Height = 72, CornerRadius = new CornerRadius(20), VerticalAlignment = VerticalAlignment.Top };
        logo.SetResourceReference(Border.BackgroundProperty, "AccentGradientBrush");
        try { logo.Child = new Image { Source = new BitmapImage(new Uri("pack://application:,,,/PQueirozOptimizer;component/Assets/app.png")), Width = 44, Height = 44 }; }
        catch (Exception ex) when (ex is System.IO.IOException or UriFormatException) { logo.Child = GlyphIcon(Glyphs.Lightning, 30, "OnAccentBrush"); }
        DockPanel.SetDock(logo, Dock.Left); hero.Children.Add(logo);
        var heroText = new StackPanel { Margin = new Thickness(22, 0, 0, 0) };
        var name = new TextBlock { Text = "PQueiroz Optimizer", FontSize = 26, FontWeight = FontWeights.Bold };
        name.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        heroText.Children.Add(name);
        var badges = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 12) };
        badges.Children.Add(Pill(AppVersion, "Accent"));
        var dev = Pill(_loc.T("Desenvolvido por Pedro Queiroz", "Developed by Pedro Queiroz"), "Info"); dev.Margin = new Thickness(8, 0, 0, 0);
        badges.Children.Add(dev);
        heroText.Children.Add(badges);
        var desc = Label(_loc.T(
            "Otimização, manutenção e gerenciamento do Windows em um só lugar — com revisão de cada ajuste antes de aplicar, backup automático e reversão com um clique.",
            "Windows optimization, maintenance and management in one place — every change is reviewed before applying, with automatic backup and one-click restore."), 13.5, true);
        desc.LineHeight = 21; desc.Margin = new Thickness(0);
        heroText.Children.Add(desc);
        hero.Children.Add(heroText);
        var heroCard = Surface(hero); heroCard.Padding = new Thickness(28); heroCard.SetResourceReference(Border.BackgroundProperty, "HeroBrush");
        root.Children.Add(heroCard);

        var grid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -14, 0) };
        grid.Children.Add(Card(_loc.T("SEGURANÇA", "SAFETY"), _loc.T("Ponto de restauração e backup antes de cada ajuste", "Restore point and backup before every change"), Glyphs.Shield, "SuccessBrush"));
        grid.Children.Add(Card(_loc.T("TRANSPARÊNCIA", "TRANSPARENCY"), _loc.T("Você escolhe item por item o que aplicar", "You choose exactly what to apply"), Glyphs.Check, "AccentBrush"));
        grid.Children.Add(Card(_loc.T("ATUALIZAÇÕES", "UPDATES"), _loc.T("Instalador verificado por SHA256", "SHA256-verified installer"), Glyphs.Download, "InfoBrush"));
        root.Children.Add(grid);

        var promo = new DockPanel();
        var promoBtn = IconButton(Glyphs.ChevronRight, _loc.T("Visitar site", "Visit website"), primary: true);
        promoBtn.VerticalAlignment = VerticalAlignment.Center; promoBtn.Margin = new Thickness(16, 0, 0, 0);
        promoBtn.Click += (_, _) => OpenAuthorSite();
        DockPanel.SetDock(promoBtn, Dock.Right); promo.Children.Add(promoBtn);
        var promoText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var pt = Label(_loc.T("Precisa de um serviço ou automação?", "Need a service or automation?"), 15); pt.FontWeight = FontWeights.SemiBold; pt.Margin = new Thickness(0, 0, 0, 3);
        promoText.Children.Add(pt);
        var ps = Label(AuthorSiteUrl, 12.5); ps.Margin = new Thickness(0); ps.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        promoText.Children.Add(ps);
        promo.Children.Add(promoText);
        root.Children.Add(Surface(promo));

        var disclaimer = Label(_loc.T(
            "Remoções de arquivos e aplicativos não são desfeitas pelo backup de configurações; consulte os detalhes antes de aplicar.",
            "File and app removals cannot be undone by the configuration backup; review the details before applying."), 12, true);
        disclaimer.Margin = new Thickness(4, 0, 0, 0);
        root.Children.Add(disclaimer);

        ContentHost.Children.Clear();
        ContentHost.Children.Add(root);
    }

    private void OpenAuthorSite() => OpenUrl(AuthorSiteUrl);
    #endregion
}
