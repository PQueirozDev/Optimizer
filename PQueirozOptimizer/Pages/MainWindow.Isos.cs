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
    #region ISOs Page
    private void ShowIsos()
    {
        PageTitle.Text = "ISOs do Windows";
        PageBadge.Visibility = Visibility.Visible;
        PageBadgeText.Text = "Download & Gerenciamento";
        var root = new StackPanel();

        // 1. Prominent Download & Mount Banner
        var banner = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(22, 18, 22, 18),
            Margin = new Thickness(0, 0, 0, 20),
            BorderThickness = new Thickness(1)
        };
        banner.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        banner.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var bannerGrid = new Grid();
        bannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var bText = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        var bTitle = new TextBlock
        {
            Text = "Central de Instalação & Download do Windows 11",
            FontSize = 16.5,
            FontWeight = FontWeights.Bold
        };
        bTitle.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

        var bSub = new TextBlock
        {
            Text = "Qualquer usuário pode baixar a ISO oficial do Windows 11 diretamente dos servidores da Microsoft ou utilizar a versão personalizada Pedro Queiroz para máxima performance.",
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 18,
            Margin = new Thickness(0, 4, 0, 0)
        };
        bSub.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        bText.Children.Add(bTitle);
        bText.Children.Add(bSub);
        Grid.SetColumn(bText, 0);
        bannerGrid.Children.Add(bText);

        var addBtn = new Button
        {
            Content = "+ Adicionar Imagem ISO",
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(14, 8, 14, 8)
        };
        addBtn.Click += AddIso_Click;
        Grid.SetColumn(addBtn, 1);
        bannerGrid.Children.Add(addBtn);

        banner.Child = bannerGrid;
        root.Children.Add(banner);

        // 2. Official Windows 11 Card
        var officialCard = new Border
        {
            Width = 470,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20),
            Margin = new Thickness(0, 0, 18, 18)
        };
        officialCard.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        officialCard.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var offContent = new StackPanel();
        var offHead = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var offTitle = new TextBlock { Text = " Windows 11 Oficial (Microsoft)", FontSize = 16, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        offTitle.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        DockPanel.SetDock(offTitle, Dock.Left);
        offHead.Children.Add(offTitle);

        var offBadge = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(7, 2, 7, 2),
            VerticalAlignment = VerticalAlignment.Center
        };
        offBadge.SetResourceReference(Border.BackgroundProperty, "PanelHoverBrush");
        var offBadgeText = new TextBlock { Text = "Oficial 24H2", FontSize = 10.5, FontWeight = FontWeights.SemiBold };
        offBadgeText.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        offBadge.Child = offBadgeText;
        DockPanel.SetDock(offBadge, Dock.Right);
        offHead.Children.Add(offBadge);
        offContent.Children.Add(offHead);

        var offStatus = new TextBlock
        {
            Text = "● Disponível para Download Gratuito",
            Foreground = (Brush)FindResource("SuccessBrush"),
            FontSize = 11.5,
            FontWeight = FontWeights.Medium,
            Margin = new Thickness(0, 0, 0, 8)
        };
        offContent.Children.Add(offStatus);

        var offDesc = new TextBlock
        {
            Text = "Imagem original e limpa direto da Microsoft. Permite gerar pendrive bootável via Media Creation Tool ou baixar a imagem ISO oficial para instalação limpa.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 14)
        };
        offDesc.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        offContent.Children.Add(offDesc);

        var offBtns = new WrapPanel();
        var dlOfficialBtn = new Button
        {
            Content = "⬇ Baixar ISO Oficial",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 8, 8)
        };
        dlOfficialBtn.Click += (_, _) => Process.Start(new ProcessStartInfo("https://www.microsoft.com/software-download/windows11") { UseShellExecute = true });

        var dlToolBtn = new Button
        {
            Content = " Media Creation Tool",
            Margin = new Thickness(0, 0, 8, 8)
        };
        dlToolBtn.Click += (_, _) => Process.Start(new ProcessStartInfo("https://go.microsoft.com/fwlink/?linkid=2156295") { UseShellExecute = true });

        var copyOffLink = new Button
        {
            Content = " Copiar Link",
            Margin = new Thickness(0, 0, 8, 8)
        };
        copyOffLink.Click += (_, _) =>
        {
            Clipboard.SetText("https://www.microsoft.com/software-download/windows11");
            MessageBox.Show("Link oficial copiado para a área de transferência!", "Copiado", MessageBoxButton.OK, MessageBoxImage.Information);
        };

        offBtns.Children.Add(dlOfficialBtn);
        offBtns.Children.Add(dlToolBtn);
        offBtns.Children.Add(copyOffLink);
        offContent.Children.Add(offBtns);

        officialCard.Child = offContent;

        var panel = new WrapPanel();
        panel.Children.Add(officialCard);

        var isos = _configService.Config.IsoCatalog;
        foreach (var iso in isos)
        {
            var card = new Border
            {
                Width = 470,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(20),
                Margin = new Thickness(0, 0, 18, 18)
            };
            card.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

            var content = new StackPanel();

            var headerDock = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var isoTitle = new TextBlock
            {
                Text = " " + iso.Name,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            isoTitle.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            DockPanel.SetDock(isoTitle, Dock.Left);
            headerDock.Children.Add(isoTitle);

            var vBadge = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(7, 2, 7, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            vBadge.SetResourceReference(Border.BackgroundProperty, "PanelHoverBrush");
            var vt = new TextBlock { Text = string.IsNullOrWhiteSpace(iso.Version) ? "Personalizada" : iso.Version, FontSize = 10.5, FontWeight = FontWeights.SemiBold };
            vt.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            vBadge.Child = vt;
            DockPanel.SetDock(vBadge, Dock.Right);
            headerDock.Children.Add(vBadge);
            content.Children.Add(headerDock);

            var exists = !string.IsNullOrWhiteSpace(iso.LocalPath) && File.Exists(iso.LocalPath);
            string sizeStr = "";
            if (exists)
            {
                try
                {
                    var len = new FileInfo(iso.LocalPath).Length;
                    sizeStr = $" • {len / 1024.0 / 1024.0 / 1024.0:N2} GB";
                }
                catch { }
            }

            var statusText = new TextBlock
            {
                Text = exists ? $"● Disponível Localmente{sizeStr}" : "○ Imagem disponível para download / associação",
                Foreground = exists ? (Brush)FindResource("SuccessBrush") : (Brush)FindResource("MutedBrush"),
                FontSize = 11.5,
                FontWeight = FontWeights.Medium,
                Margin = new Thickness(0, 0, 0, 8)
            };
            content.Children.Add(statusText);

            if (!string.IsNullOrWhiteSpace(iso.Description))
            {
                var desc = new TextBlock
                {
                    Text = iso.Description,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    LineHeight = 17,
                    Margin = new Thickness(0, 0, 0, 10)
                };
                desc.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                content.Children.Add(desc);
            }

            if (!string.IsNullOrWhiteSpace(iso.LocalPath))
            {
                var pathBox = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(8, 5, 8, 5),
                    Margin = new Thickness(0, 0, 0, 12)
                };
                pathBox.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
                pathBox.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

                var pathText = new TextBlock
                {
                    Text = iso.LocalPath,
                    FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                pathText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                pathBox.Child = pathText;
                content.Children.Add(pathBox);
            }

            var btnRow = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };

            if (exists)
            {
                var mountBtn = new Button { Content = " Montar ISO", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 8) };
                mountBtn.Click += async (_, _) => await MountIsoAsync(iso.LocalPath);
                btnRow.Children.Add(mountBtn);

                var dismountBtn = new Button { Content = "⏏ Desmontar", Margin = new Thickness(0, 0, 8, 8) };
                dismountBtn.Click += async (_, _) => await DismountIsoAsync(iso.LocalPath);
                btnRow.Children.Add(dismountBtn);

                var folderBtn = new Button { Content = " Abrir Pasta", Margin = new Thickness(0, 0, 8, 8) };
                folderBtn.Click += (_, _) =>
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{iso.LocalPath}\"") { UseShellExecute = true });
                };
                btnRow.Children.Add(folderBtn);
            }
            else
            {
                var locateBtn = new Button { Content = " Localizar no Meu PC", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 8) };
                locateBtn.Click += (_, _) =>
                {
                    var dlg = new OpenFileDialog { Filter = "Imagens ISO (*.iso)|*.iso", Title = $"Localizar arquivo para {iso.Name}" };
                    if (dlg.ShowDialog() == true)
                    {
                        iso.LocalPath = dlg.FileName;
                        _configService.Save();
                        ShowIsos();
                    }
                };
                btnRow.Children.Add(locateBtn);
            }

            if (!string.IsNullOrWhiteSpace(iso.DownloadUrl))
            {
                var dlBtn = new Button { Content = "⬇ Baixar via Google Drive", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 8) };
                dlBtn.Click += (_, _) => Process.Start(new ProcessStartInfo(iso.DownloadUrl) { UseShellExecute = true });
                btnRow.Children.Add(dlBtn);
            }

            if (!string.IsNullOrWhiteSpace(iso.LocalPath))
            {
                var copyBtn = new Button { Content = " Copiar Caminho", Margin = new Thickness(0, 0, 8, 8) };
                copyBtn.Click += (_, _) =>
                {
                    Clipboard.SetText(iso.LocalPath);
                    MessageBox.Show("Caminho copiado para a área de transferência!", "Copiado", MessageBoxButton.OK, MessageBoxImage.Information);
                };
                btnRow.Children.Add(copyBtn);
            }

            content.Children.Add(btnRow);
            card.Child = content;
            panel.Children.Add(card);
        }

        root.Children.Add(panel);
        ContentHost.Children.Clear();
        ContentHost.Children.Add(root);
    }

    private void AddIso_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecionar Imagem ISO do Windows",
            Filter = "Imagens ISO (*.iso)|*.iso|Todos os Arquivos (*.*)|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\Downloads"
        };

        if (dialog.ShowDialog() == true)
        {
            var fileName = Path.GetFileNameWithoutExtension(dialog.FileName);
            var entry = new IsoEntry
            {
                Name = fileName,
                LocalPath = dialog.FileName,
                Version = "Custom",
                Architecture = "x64",
                Description = "Imagem ISO personalizada adicionada pelo usuário."
            };
            _configService.AddIso(entry);
            MessageBox.Show($"ISO '{fileName}' adicionada com sucesso!", "ISO Adicionada", MessageBoxButton.OK, MessageBoxImage.Information);
            ShowIsos();
        }
    }

    private async Task MountIsoAsync(string isoPath)
    {
        if (!File.Exists(isoPath))
        {
            MessageBox.Show($"O arquivo ISO não foi localizado:\n{isoPath}", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
                MessageBox.Show($"A imagem ISO foi montada na unidade {path} e aberta no Explorador de Arquivos.", "ISO Montada", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("A imagem ISO foi montada com sucesso. Acesse 'Este Computador' para visualizar a unidade.", "ISO Montada", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Falha ao montar ISO: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show("A imagem ISO foi desmontada com sucesso.", "ISO Desmontada", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Falha ao desmontar ISO: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }
    #endregion
}
