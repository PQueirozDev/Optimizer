using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Win32;
using PQueirozOptimizer.Models;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;
public partial class MainWindow
{
    #region ISOs Page
    private const string OfficialWindowsIsoUrl = "https://www.microsoft.com/software-download/windows11";

    private void ShowIsos()
    {
        PageTitle.Text = "ISOs do Windows";
        PageBadge.Visibility = Visibility.Visible;
        PageBadgeText.Text = "Download & Gerenciamento";
        var root = new StackPanel();

        // 1. Destaque com a ação de adicionar
        var hero = new DockPanel();
        var addBtn = IconButton(Glyphs.Folder, "Adicionar Imagem ISO", primary: true);
        addBtn.VerticalAlignment = VerticalAlignment.Center; addBtn.Margin = new Thickness(16, 0, 0, 0);
        addBtn.Click += AddIso_Click;
        DockPanel.SetDock(addBtn, Dock.Right); hero.Children.Add(addBtn);
        var heroChip = IconChip(Glyphs.Disc, "Accent", 52); DockPanel.SetDock(heroChip, Dock.Left); hero.Children.Add(heroChip);
        var heroText = new StackPanel { Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var heroTitle = Label("Central de Instalação & Download do Windows 11", 18); heroTitle.Margin = new Thickness(0, 0, 0, 4);
        heroText.Children.Add(heroTitle);
        var heroSub = Label("Qualquer usuário pode baixar a ISO oficial do Windows 11 diretamente dos servidores da Microsoft ou utilizar a versão personalizada Pedro Queiroz para máxima performance.", 12.5, true); heroSub.Margin = new Thickness(0);
        heroText.Children.Add(heroSub);
        hero.Children.Add(heroText);
        var heroCard = Surface(hero); heroCard.SetResourceReference(Border.BackgroundProperty, "HeroBrush");
        root.Children.Add(heroCard);

        var cards = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -14, 0) };

        // 2. Windows 11 oficial
        var officialButtons = new WrapPanel();
        var downloadOfficial = IconButton(Glyphs.Download, "Baixar ISO Oficial", primary: true);
        downloadOfficial.Click += (_, _) => OpenUrl(OfficialWindowsIsoUrl);
        var mediaTool = IconButton(Glyphs.Download, "Media Creation Tool");
        mediaTool.Click += (_, _) => OpenUrl("https://go.microsoft.com/fwlink/?linkid=2156295");
        var copyLink = IconButton(Glyphs.Document, "Copiar Link");
        copyLink.Click += (_, _) => CopyText(OfficialWindowsIsoUrl, "Link oficial copiado para a área de transferência!");
        foreach (var b in new[] { downloadOfficial, mediaTool, copyLink }) officialButtons.Children.Add(b);
        cards.Children.Add(IsoCard("Windows 11 Oficial (Microsoft)", "Oficial 24H2", ("Disponível para Download Gratuito", "Success"),
            "Imagem original e limpa direto da Microsoft. Permite gerar pendrive bootável via Media Creation Tool ou baixar a imagem ISO oficial para instalação limpa.", null, officialButtons));

        // 3. Catálogo (a ISO personalizada e as adicionadas pelo usuário)
        foreach (var iso in _configService.Config.IsoCatalog)
        {
            // O config.json de fábrica repete o Windows oficial, que já tem o cartão acima
            if (iso.DownloadUrl.Equals(OfficialWindowsIsoUrl, StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(iso.LocalPath)) continue;

            var exists = !string.IsNullOrWhiteSpace(iso.LocalPath) && File.Exists(iso.LocalPath);
            var status = ("Imagem disponível para download / associação", "Muted");
            if (exists)
            {
                try { status = ($"Disponível Localmente • {new FileInfo(iso.LocalPath).Length / 1073741824d:N2} GB", "Success"); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { status = ("Disponível Localmente", "Success"); }
            }

            var buttons = new WrapPanel();
            if (exists)
            {
                var mount = IconButton(Glyphs.Disc, "Montar ISO", primary: true);
                mount.Click += async (_, _) => await MountIsoAsync(iso.LocalPath);
                var unmount = IconButton(Glyphs.Undo, "Desmontar");
                unmount.Click += async (_, _) => await DismountIsoAsync(iso.LocalPath);
                var folder = IconButton(Glyphs.Folder, "Abrir Pasta");
                folder.Click += (_, _) => ShowInFolder(iso.LocalPath);
                foreach (var b in new[] { mount, unmount, folder }) buttons.Children.Add(b);
            }
            else
            {
                var locate = IconButton(Glyphs.Folder, "Localizar no Meu PC", primary: string.IsNullOrWhiteSpace(iso.DownloadUrl));
                locate.Click += (_, _) =>
                {
                    var dlg = new OpenFileDialog { Filter = "Imagens ISO (*.iso)|*.iso", Title = $"Localizar arquivo para {iso.Name}" };
                    if (dlg.ShowDialog(this) != true) return;
                    iso.LocalPath = dlg.FileName;
                    SaveIsoCatalog();
                    ShowIsos();
                };
                buttons.Children.Add(locate);
            }
            if (!string.IsNullOrWhiteSpace(iso.DownloadUrl))
            {
                var fromDrive = Uri.TryCreate(iso.DownloadUrl, UriKind.Absolute, out var uri) && uri.Host.EndsWith("drive.google.com", StringComparison.OrdinalIgnoreCase);
                var download = IconButton(Glyphs.Download, fromDrive ? "Baixar via Google Drive" : "Baixar", primary: !exists);
                download.Click += (_, _) => OpenUrl(iso.DownloadUrl);
                buttons.Children.Add(download);
            }
            if (!string.IsNullOrWhiteSpace(iso.LocalPath))
            {
                var copyPath = IconButton(Glyphs.Document, "Copiar Caminho");
                copyPath.Click += (_, _) => CopyText(iso.LocalPath, "Caminho copiado para a área de transferência!");
                buttons.Children.Add(copyPath);
            }
            cards.Children.Add(IsoCard(iso.Name, string.IsNullOrWhiteSpace(iso.Version) ? "Personalizada" : iso.Version, status, iso.Description, iso.LocalPath, buttons));
        }

        root.Children.Add(cards);
        ContentHost.Children.Clear();
        ContentHost.Children.Add(root);
    }

    private Border IsoCard(string name, string version, (string Text, string Tone) status, string description, string? localPath, Panel buttons)
    {
        var body = new DockPanel();
        buttons.Margin = new Thickness(0, 14, 0, 0);
        DockPanel.SetDock(buttons, Dock.Bottom); body.Children.Add(buttons);

        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var badge = Pill(version, "Accent"); badge.VerticalAlignment = VerticalAlignment.Top; badge.Margin = new Thickness(12, 2, 0, 0);
        DockPanel.SetDock(badge, Dock.Right); head.Children.Add(badge);
        var chip = IconChip(Glyphs.Disc, "Info", 40); chip.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(chip, Dock.Left); head.Children.Add(chip);
        var titles = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var title = Label(name, 15); title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(0, 0, 0, 2);
        titles.Children.Add(title);
        var state = Label(status.Text, 12); state.Margin = new Thickness(0);
        state.SetResourceReference(TextBlock.ForegroundProperty, status.Tone + "Brush");
        titles.Children.Add(state);
        head.Children.Add(titles);
        DockPanel.SetDock(head, Dock.Top); body.Children.Add(head);

        var text = new StackPanel();
        if (!string.IsNullOrWhiteSpace(description))
        {
            var d = Label(description, 12, true); d.Margin = new Thickness(0); d.LineHeight = 18;
            text.Children.Add(d);
        }
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            var path = Label(localPath, 11, true); path.Margin = new Thickness(0, 10, 0, 0);
            path.TextWrapping = TextWrapping.NoWrap; path.TextTrimming = TextTrimming.CharacterEllipsis; path.ToolTip = localPath;
            path.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            text.Children.Add(path);
        }
        body.Children.Add(text);

        var card = Surface(body); card.Margin = new Thickness(0, 0, 14, 14); card.Padding = new Thickness(20, 18, 20, 18);
        return card;
    }

    private void SaveIsoCatalog()
    {
        try { _configService.Save(); }
        catch (IOException ex) { _log.Write("WARN", "Preferências não foram salvas: " + (ex.InnerException?.Message ?? ex.Message)); }
    }

    private void AddIso_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecionar Imagem ISO do Windows",
            Filter = "Imagens ISO (*.iso)|*.iso|Todos os Arquivos (*.*)|*.*",
            InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
        };
        if (dialog.ShowDialog(this) != true) return;

        var fileName = Path.GetFileNameWithoutExtension(dialog.FileName);
        _configService.Config.IsoCatalog.Add(new IsoEntry
        {
            Name = fileName,
            LocalPath = dialog.FileName,
            Version = "Custom",
            Architecture = "x64",
            Description = "Imagem ISO personalizada adicionada pelo usuário."
        });
        SaveIsoCatalog();
        OperationStatus.Text = $"ISO '{fileName}' adicionada com sucesso!";
        ShowIsos();
    }

    private async Task MountIsoAsync(string isoPath)
    {
        if (!File.Exists(isoPath))
        {
            Msg($"O arquivo ISO não foi localizado:\n{isoPath}", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            var driveLetter = await PowerShellBridge.RunScriptAsync(
                "$img = Mount-DiskImage -ImagePath $env:PQO_ISO -PassThru; $vol = ($img | Get-Volume); if ($vol) { $vol.DriveLetter }",
                new Dictionary<string, string> { ["PQO_ISO"] = isoPath });

            if (!string.IsNullOrWhiteSpace(driveLetter))
            {
                var path = $"{driveLetter}:\\";
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", path) { UseShellExecute = true }); }
                catch (System.ComponentModel.Win32Exception ex) { _log.Write("WARN", "Não foi possível abrir a pasta: " + ex.Message); }
                Msg($"A imagem ISO foi montada na unidade {path} e aberta no Explorador de Arquivos.", "ISO Montada", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                Msg("A imagem ISO foi montada com sucesso. Acesse 'Este Computador' para visualizar a unidade.", "ISO Montada", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            Msg($"Falha ao montar ISO: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private async Task DismountIsoAsync(string isoPath)
    {
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            await PowerShellBridge.RunScriptAsync(
                "Dismount-DiskImage -ImagePath $env:PQO_ISO | Out-Null",
                new Dictionary<string, string> { ["PQO_ISO"] = isoPath });
            Msg("A imagem ISO foi desmontada com sucesso.", "ISO Desmontada", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Msg($"Falha ao desmontar ISO: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }
    #endregion
}
