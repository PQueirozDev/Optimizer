using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PQueirozOptimizer.Models;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;
public partial class MainWindow
{
    #region Tools Page (Including "Barra de tarefas all black")
    private void ShowTools()
    {
        PageTitle.Text = "Ferramentas";
        PageBadge.Visibility = Visibility.Collapsed;
        var root = new StackPanel();
        var featured = Responsive(new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -14, 0) }, 360, 2);

        // 1. Barra de tarefas All Black
        var isAbActive = IsAllBlackTaskbarActive();
        var allBlack = new DockPanel();
        var abButtons = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        var applyAbBtn = IconButton(Glyphs.Check, "Aplicar All Black", primary: !isAbActive);
        applyAbBtn.Click += async (_, _) => await ApplyAllBlackTaskbarAsync(enable: true);
        var revertAbBtn = IconButton(Glyphs.Undo, "Restaurar padrão", primary: isAbActive);
        revertAbBtn.Click += async (_, _) => await ApplyAllBlackTaskbarAsync(enable: false);
        var copyAbBtn = IconButton(Glyphs.Document, "Copiar comando");
        copyAbBtn.SetResourceReference(StyleProperty, "GhostButton");
        copyAbBtn.Click += (_, _) => CopyText(AllBlackCommand, "Comando PowerShell copiado para a área de transferência.");
        abButtons.Children.Add(applyAbBtn); abButtons.Children.Add(revertAbBtn); abButtons.Children.Add(copyAbBtn);
        DockPanel.SetDock(abButtons, Dock.Bottom); allBlack.Children.Add(abButtons);
        allBlack.Children.Add(FeatureHeader(Glyphs.Monitor, "Accent", "Barra de tarefas All Black",
            "Aplica uma paleta de acentuação preta e desativa a transparência, deixando a barra de tarefas em preto puro.",
            isAbActive ? "Ativa" : "Padrão do Windows", isAbActive ? "Success" : "Accent"));
        var abCard = Surface(allBlack); abCard.Margin = new Thickness(0, 0, 14, 14);
        featured.Children.Add(abCard);

        // 2. Atalho de limpeza rápida
        var configured = _cleaner.IsShortcutConfigured();
        var quickClean = new DockPanel();
        var qcButtons = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        var create = IconButton(configured ? Glyphs.Delete : Glyphs.Check, configured ? "Remover atalhos" : "Criar atalho de limpeza", primary: !configured);
        create.Tag = configured;
        create.Click += (_, _) =>
        {
            try
            {
                if ((bool)create.Tag)
                {
                    _cleaner.RemoveShortcut();
                    Msg("Atalhos de Limpeza Rápida removidos com sucesso.", "Limpeza Rápida", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    _cleaner.CreateShortcut();
                    Msg("Atalho 'Limpeza Rápida' criado na sua Área de Trabalho e no Menu Iniciar.\n\nPara fixá-lo na barra de tarefas, clique com o botão direito no atalho e escolha 'Fixar na barra de tarefas' (o Windows 11 não permite que aplicativos façam isso sozinhos).", "Atalho Criado", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                _log.Write("ERROR", "Atalho de limpeza: " + ex.Message);
                Msg("Não foi possível alterar os atalhos: " + ex.Message, "Limpeza Rápida", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            ShowTools();
        };
        var test = IconButton(Glyphs.Broom, "Limpar agora", primary: configured);
        test.Click += async (_, _) => await PrepareOperationAsync("quickclean");
        qcButtons.Children.Add(create); qcButtons.Children.Add(test);
        DockPanel.SetDock(qcButtons, Dock.Bottom); quickClean.Children.Add(qcButtons);
        quickClean.Children.Add(FeatureHeader(Glyphs.Broom, "Info", "Limpeza rápida em um clique",
            "Cria atalhos na Área de Trabalho e no Menu Iniciar que limpam arquivos temporários sem abrir o aplicativo.",
            configured ? "Atalhos criados" : "Sem atalhos", configured ? "Success" : "Info"));
        var qcCard = Surface(quickClean); qcCard.Margin = new Thickness(0, 0, 14, 14);
        featured.Children.Add(qcCard);

        // 3. Atalho de modo de energia (pensado para notebooks: bateria x desempenho)
        var powerSource = PowerModeService.GetSource();
        var powerShortcut = PowerModeService.IsShortcutCreated();
        var power = new DockPanel();
        var pmButtons = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        var pmShortcut = IconButton(powerShortcut ? Glyphs.Delete : Glyphs.Check, powerShortcut ? "Remover atalho" : "Criar atalho na Área de Trabalho", primary: !powerShortcut);
        pmShortcut.Click += (_, _) =>
        {
            try
            {
                if (powerShortcut) { PowerModeService.RemoveShortcut(_log); OperationStatus.Text = "Atalho de modo de energia removido."; }
                else { PowerModeService.CreateShortcut(_log); OperationStatus.Text = "Atalho 'Modo de Energia' criado na Área de Trabalho. Ele abre o seletor sem pedir permissão de administrador."; }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                _log.Write("ERROR", "Atalho de modo de energia: " + ex.Message);
                Msg("Não foi possível alterar os atalhos: " + ex.Message, "Modo de energia", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            ShowTools();
        };
        var pmOpen = IconButton(Glyphs.Battery, "Abrir seletor", primary: powerShortcut);
        pmOpen.Click += (_, _) => new PowerModeWindow(_log, closeAfterChoice: false) { Owner = this }.ShowDialog();
        pmButtons.Children.Add(pmShortcut); pmButtons.Children.Add(pmOpen);
        // Modo de energia é dos planos completos: no Base e no Intermediário o cartão vira o pedido de upgrade
        if (!PageAllowed(PlanAccess.PowerMode))
        {
            pmButtons.Children.Clear();
            var pmUpgrade = IconButton(Glyphs.Lock, string.Format(Translator.Tr("Disponível a partir do plano {0}"), Translator.Tr(LicensePlans.Advanced)), primary: true);
            pmUpgrade.Click += (_, _) => RequestUpgrade(LicensePlans.Advanced);
            pmButtons.Children.Add(pmUpgrade);
        }
        DockPanel.SetDock(pmButtons, Dock.Bottom); power.Children.Add(pmButtons);
        var (pmStatus, pmTone) = powerShortcut ? ("Atalho criado", "Success") : powerSource.HasBattery ? ("Recomendado para este PC", "Accent") : ("Recomendado para notebooks", "Info");
        power.Children.Add(FeatureHeader(Glyphs.Battery, "Success", "Atalho de modo de energia",
            "Cria um atalho na Área de Trabalho que abre um seletor rápido: economia de energia, equilibrado, melhor desempenho ou outro plano de energia. Abre sem pedir permissão de administrador.",
            pmStatus, pmTone));
        var pmCard = Surface(power); pmCard.Margin = new Thickness(0, 0, 14, 14);
        featured.Children.Add(pmCard);
        root.Children.Add(featured);

        // 3. Utilitários nativos do Windows
        root.Children.Add(SectionHeader("Utilitários do Windows", "Atalhos para as ferramentas administrativas mais usadas."));
        var tools = new[]
        {
            ("Gerenciador de Tarefas", "taskmgr.exe", Glyphs.Speed),
            ("Gerenciador de Dispositivos", "devmgmt.msc", Glyphs.Chip),
            ("Serviços do Windows", "services.msc", Glyphs.Settings),
            ("Configurações do Sistema", "ms-settings:", Glyphs.Settings),
            ("Prompt de Comando", "cmd.exe", Glyphs.Document),
            ("PowerShell", "powershell.exe", Glyphs.Document),
            ("Editor do Registro", "regedit.exe", Glyphs.Repair),
            ("Informações do Sistema", "msinfo32.exe", Glyphs.Info),
            ("Gerenciamento de Disco", "diskmgmt.msc", Glyphs.Drive),
            ("Visualizador de Eventos", "eventvwr.msc", Glyphs.History),
        };
        var grid = Responsive(new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, -12, 0) }, 210, 4);
        foreach (var (name, target, glyph) in tools)
        {
            var tile = new Button { Tag = target, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 12, 12) };
            tile.SetResourceReference(Button.BackgroundProperty, "CardBgBrush");
            tile.SetResourceReference(Button.BorderBrushProperty, "BorderSubtleBrush");
            var row = new DockPanel();
            var chip = IconChip(glyph, "Accent", 34); DockPanel.SetDock(chip, Dock.Left); row.Children.Add(chip);
            var arrow = GlyphIcon(Glyphs.ChevronRight, 11, "MutedBrush"); DockPanel.SetDock(arrow, Dock.Right); row.Children.Add(arrow);
            var label = new TextBlock { Text = name, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = name };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            row.Children.Add(label);
            tile.Content = row;
            tile.Click += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
                catch (Exception ex) { _log.Write("ERROR", $"Não foi possível abrir {name}: {ex.Message}"); OperationStatus.Text = $"Não foi possível abrir {name}."; }
            };
            grid.Children.Add(tile);
        }
        root.Children.Add(grid);

        ContentHost.Children.Clear();
        ContentHost.Children.Add(root);
    }

    private const string AllBlackCommand = @"$p = [byte[]](0xB0,0xB2,0xB4,0xFF,0xCC,0xCE,0xD0,0xFF,0x00,0x00,0x00,0xFF,0x90,0x92,0x94,0xFF,0x00,0x00,0x00,0xFF,0x00,0x00,0x00,0xFF,0x00,0x00,0x00,0xFF,0x00,0x00,0x00,0xFF); Set-ItemProperty -Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Accent' -Name 'AccentPalette' -Value $p; Set-ItemProperty -Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Accent' -Name 'AccentColorMenu' -Type DWord -Value 0xFF000000; Set-ItemProperty -Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Accent' -Name 'StartColorMenu' -Type DWord -Value 0xFF000000; Set-ItemProperty -Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize' -Name 'ColorPrevalence' -Type DWord -Value 1; Set-ItemProperty -Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize' -Name 'EnableTransparency' -Type DWord -Value 0";

    /// <summary>Cabeçalho dos cartões de destaque: ícone, título, status e descrição.</summary>
    private StackPanel FeatureHeader(string glyph, string tone, string title, string description, string status, string statusTone)
    {
        var panel = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12), LastChildFill = false };
        var badge = Pill(status, statusTone); DockPanel.SetDock(badge, Dock.Right); badge.VerticalAlignment = VerticalAlignment.Top;
        head.Children.Add(badge);
        head.Children.Add(IconChip(glyph, tone, 44));
        panel.Children.Add(head);
        var t = Label(title, 16); t.FontWeight = FontWeights.SemiBold; t.Margin = new Thickness(0, 0, 0, 4);
        panel.Children.Add(t);
        var d = Label(description, 12.5, true); d.Margin = new Thickness(0); d.LineHeight = 19;
        panel.Children.Add(d);
        return panel;
    }

    private bool IsAllBlackTaskbarActive()
    {
        try
        {
            using var keyThemes = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            using var keyAccent = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Accent");
            var colorPrev = keyThemes?.GetValue("ColorPrevalence");
            var enableTrans = keyThemes?.GetValue("EnableTransparency");
            var accentMenu = keyAccent?.GetValue("AccentColorMenu");

            return (colorPrev is int cp && cp == 1) &&
                   (enableTrans is int et && et == 0) &&
                   (accentMenu != null);
        }
        catch
        {
            return false;
        }
    }

    private async Task ApplyAllBlackTaskbarAsync(bool enable)
    {
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            string ps;
            if (enable)
            {
                // Exact command provided by user + explorer restart
                ps = @"
if (-not (Test-Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Accent')) {
    New-Item -Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Accent' -Force | Out-Null
}
$p = [byte[]](0xB0,0xB2,0xB4,0xFF,0xCC,0xCE,0xD0,0xFF,0x00,0x00,0x00,0xFF,0x90,0x92,0x94,0xFF,0x00,0x00,0x00,0xFF,0x00,0x00,0x00,0xFF,0x00,0x00,0x00,0xFF,0x00,0x00,0x00,0xFF);
Set-ItemProperty -Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Accent' -Name 'AccentPalette' -Value $p -Force;
Set-ItemProperty -Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Accent' -Name 'AccentColorMenu' -Type DWord -Value 0xFF000000 -Force;
Set-ItemProperty -Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Accent' -Name 'StartColorMenu' -Type DWord -Value 0xFF000000 -Force;
Set-ItemProperty -Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize' -Name 'ColorPrevalence' -Type DWord -Value 1 -Force;
Set-ItemProperty -Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize' -Name 'EnableTransparency' -Type DWord -Value 0 -Force;
Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
";
            }
            else
            {
                ps = @"
Set-ItemProperty -Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize' -Name 'ColorPrevalence' -Type DWord -Value 0 -Force;
Set-ItemProperty -Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize' -Name 'EnableTransparency' -Type DWord -Value 1 -Force;
Remove-ItemProperty -Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Accent' -Name 'AccentPalette' -ErrorAction SilentlyContinue;
Remove-ItemProperty -Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Accent' -Name 'AccentColorMenu' -ErrorAction SilentlyContinue;
Remove-ItemProperty -Path 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Accent' -Name 'StartColorMenu' -ErrorAction SilentlyContinue;
Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
";
            }

            await PowerShellBridge.RunScriptAsync(ps);
            _log.Write("SUCCESS", enable ? "Barra de tarefas All Black aplicada" : "Barra de tarefas padrão restaurada");

            Msg(
                enable 
                    ? "Barra de tarefas All Black aplicada com sucesso!\nO Windows Explorer foi reiniciado para atualizar o visual." 
                    : "Barra de tarefas padrão restaurada com sucesso!\nO Windows Explorer foi reiniciado.",
                "Barra de Tarefas All Black",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );

            ShowTools();
        }
        catch (Exception ex)
        {
            Msg($"Falha ao aplicar alteração: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }
    #endregion
}
