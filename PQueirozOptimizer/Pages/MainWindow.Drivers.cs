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
    #region Drivers Page
    private void ShowDrivers()
    {
        PageTitle.Text = "Drivers & Utilitários";
        PageBadge.Visibility = Visibility.Collapsed;

        var allDrivers = _driverService.GetAllDrivers(_snapshot, _loc.IsEnglish);
        if (!IsAdminLicense)
        {
            allDrivers = allDrivers.Where(driver => !driver.Name.StartsWith("Driver Clean", StringComparison.OrdinalIgnoreCase)).ToList();
        }
        var root = new StackPanel();

        // Subtitle & GPU Detection Header
        var subHeader = new TextBlock
        {
            Text = !string.IsNullOrEmpty(_snapshot?.Graphics)
                ? $"Placa de vídeo detectada: {_snapshot.Graphics} • Todos os drivers com links oficiais e opção de download."
                : "Baixe e atualize os drivers essenciais de vídeo, chipset, rede e áudio para máxima taxa de quadros e menor latência.",
            FontSize = 12.5,
            Margin = new Thickness(0, 0, 0, 14)
        };
        subHeader.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        root.Children.Add(subHeader);

        // Top Filter & Search Bar
        var filterBar = new DockPanel { Margin = new Thickness(0, 0, 0, 16), LastChildFill = true };

        // Search Box (Right aligned)
        var searchPanel = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(searchPanel, Dock.Right);
        var searchBox = new TextBox
        {
            Width = 240,
            Height = 32,
            Padding = new Thickness(10, 4, 10, 4),
            FontSize = 12,
            Text = _driverSearch,
            ToolTip = "Filtrar drivers por nome, fabricante ou categoria"
        };
        searchPanel.Children.Add(new TextBlock
        {
            Text = "",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            FontSize = 13
        });
        searchPanel.Children.Add(searchBox);
        filterBar.Children.Add(searchPanel);

        // Categories Pills
        var categoriesPanel = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        var cats = new[]
        {
            ("Todos", $"Todos ({allDrivers.Count})"),
            ("GPU", $"Placas de Vídeo ({allDrivers.Count(d => d.Category == "GPU")})"),
            ("Chipset", $"Chipset & CPU ({allDrivers.Count(d => d.Category == "Chipset")})"),
            ("Áudio", $"Áudio ({allDrivers.Count(d => d.Category == "Áudio")})"),
            ("Rede", $"Rede ({allDrivers.Count(d => d.Category == "Rede")})"),
            ("Utilitários", $"Utilitários ({allDrivers.Count(d => d.Category == "Utilitários")})")
        };

        foreach (var (catKey, catLabel) in cats)
        {
            var isSelected = _driverCategory.Equals(catKey, StringComparison.OrdinalIgnoreCase);
            var catBtn = new Button
            {
                Content = catLabel,
                FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Medium,
                Margin = new Thickness(0, 0, 6, 4),
                Padding = new Thickness(10, 5, 10, 5),
                FontSize = 12
            };

            if (isSelected)
            {
                catBtn.SetResourceReference(Button.BackgroundProperty, "PanelHoverBrush");
                catBtn.SetResourceReference(Button.BorderBrushProperty, "AccentBrush");
                catBtn.SetResourceReference(Button.ForegroundProperty, "TextBrush");
            }
            else
            {
                catBtn.Background = Brushes.Transparent;
                catBtn.SetResourceReference(Button.BorderBrushProperty, "BorderBrush");
                catBtn.SetResourceReference(Button.ForegroundProperty, "MutedBrush");
            }

            catBtn.Click += (_, _) =>
            {
                _driverCategory = catKey;
                ShowDrivers();
            };
            categoriesPanel.Children.Add(catBtn);
        }
        filterBar.Children.Add(categoriesPanel);
        root.Children.Add(filterBar);

        // Host for Cards
        var cardsWrap = new WrapPanel();
        root.Children.Add(cardsWrap);

        searchBox.TextChanged += (_, _) =>
        {
            _driverSearch = searchBox.Text.Trim();
            RenderDriverCards(cardsWrap, allDrivers);
        };

        RenderDriverCards(cardsWrap, allDrivers);

        ContentHost.Children.Clear();
        ContentHost.Children.Add(root);
    }

    private void RenderDriverCards(WrapPanel cardsWrap, List<DriverInfo> allDrivers)
    {
        cardsWrap.Children.Clear();

        var filtered = allDrivers.Where(d =>
        {
            if (!_driverCategory.Equals("Todos", StringComparison.OrdinalIgnoreCase) &&
                !d.Category.Contains(_driverCategory, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(_driverSearch))
            {
                var query = _driverSearch.ToLowerInvariant();
                return d.Name.ToLowerInvariant().Contains(query) ||
                       d.Vendor.ToLowerInvariant().Contains(query) ||
                       d.Description.ToLowerInvariant().Contains(query) ||
                       d.Category.ToLowerInvariant().Contains(query);
            }

            return true;
        }).ToList();

        if (filtered.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = "Nenhum driver encontrado para os filtros selecionados.",
                FontSize = 13,
                Margin = new Thickness(10, 20, 0, 0)
            };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            cardsWrap.Children.Add(empty);
            return;
        }

        foreach (var driver in filtered)
        {
            var card = new Border
            {
                Width = 490,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(18),
                Margin = new Thickness(0, 0, 16, 16)
            };
            card.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

            var cardContent = new StackPanel();

            // Top Badges Row
            var badgesRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };

            // Vendor Badge
            var vendorColor = driver.Vendor switch
            {
                var v when v.Contains("NVIDIA") => "#10B981",
                var v when v.Contains("AMD") => "#EF4444",
                var v when v.Contains("Intel") => "#3B82F6",
                var v when v.Contains("Realtek") => "#8B5CF6",
                _ => "#648CFF"
            };

            var vendorBadge = new Border
            {
                Background = (Brush)new BrushConverter().ConvertFromString(vendorColor)!,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 6, 0)
            };
            vendorBadge.Child = new TextBlock
            {
                Text = driver.Vendor.ToUpperInvariant(),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            };
            badgesRow.Children.Add(vendorBadge);

            // Category Badge
            var catBadge = new Border
            {
                Background = (Brush)FindResource("PanelHoverBrush"),
                BorderBrush = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 6, 0)
            };
            catBadge.Child = new TextBlock
            {
                Text = driver.Category,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("MutedBrush")
            };
            badgesRow.Children.Add(catBadge);

            // Recommended Badge
            if (driver.IsRecommendedForCurrentHardware)
            {
                var recBadge = new Border
                {
                    Background = (Brush)FindResource("PanelHoverBrush"),
                    BorderBrush = (Brush)FindResource("AccentBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2, 6, 2)
                };
                recBadge.Child = new TextBlock
                {
                    Text = "⭐ Recomendado para o seu PC",
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = (Brush)FindResource("AccentBrush")
                };
                badgesRow.Children.Add(recBadge);
            }

            cardContent.Children.Add(badgesRow);

            // Title & Version
            var title = new TextBlock
            {
                Text = driver.Name,
                FontSize = 15.5,
                FontWeight = FontWeights.SemiBold
            };
            title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            cardContent.Children.Add(title);

            var ver = new TextBlock
            {
                Text = driver.Version,
                FontSize = 11.5,
                Margin = new Thickness(0, 2, 0, 6)
            };
            ver.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            cardContent.Children.Add(ver);

            // Description
            var desc = new TextBlock
            {
                Text = driver.Description,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                LineHeight = 16.5,
                Margin = new Thickness(0, 0, 0, 10)
            };
            desc.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            cardContent.Children.Add(desc);

            // Existing local file check
            var existingFile = _driverService.GetExistingInstallerPath(driver);
            bool isDownloaded = !string.IsNullOrEmpty(existingFile);

            var status = new TextBlock
            {
                Text = isDownloaded
                    ? $"● Instalador disponível: {Path.GetFileName(existingFile)}"
                    : "○ Disponível para download oficial",
                Foreground = isDownloaded ? (Brush)FindResource("SuccessBrush") : (Brush)FindResource("MutedBrush"),
                FontSize = 11.5,
                Margin = new Thickness(0, 0, 0, 10),
                TextWrapping = TextWrapping.Wrap
            };
            cardContent.Children.Add(status);

            // Progress Bar for direct download
            var pbar = new ProgressBar
            {
                Height = 4,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 0, 0, 10)
            };
            cardContent.Children.Add(pbar);

            // Action Buttons
            var btnRow = new WrapPanel();

            // Official Download Button
            var officialBtn = new Button
            {
                Content = "⬇ Baixar (Site Oficial)",
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(12, 6, 12, 6),
                FontSize = 12
            };
            officialBtn.Click += (_, _) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo(driver.OfficialDownloadUrl) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Falha ao abrir navegador: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            btnRow.Children.Add(officialBtn);

            // Secondary Download Button (ex.: Google Drive do autor)
            if (!string.IsNullOrEmpty(driver.SecondaryDownloadUrl))
            {
                var secondaryBtn = new Button
                {
                    Content = $"⬇ {driver.SecondaryDownloadLabel ?? "Download Alternativo"}",
                    Padding = new Thickness(12, 6, 12, 6),
                    FontSize = 12
                };
                secondaryBtn.Click += (_, _) =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(driver.SecondaryDownloadUrl!) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Falha ao abrir navegador: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };
                btnRow.Children.Add(secondaryBtn);
            }

            // Direct Download Button (if direct URL available)
            if (!string.IsNullOrEmpty(driver.DirectDownloadUrl))
            {
                var directBtn = new Button
                {
                    Content = " Download Direto",
                    Padding = new Thickness(12, 6, 12, 6),
                    FontSize = 12
                };
                directBtn.Click += async (_, _) =>
                {
                    directBtn.IsEnabled = false;
                    officialBtn.IsEnabled = false;
                    pbar.Visibility = Visibility.Visible;
                    pbar.IsIndeterminate = false;
                    pbar.Value = 0;
                    status.Text = "Iniciando download...";
                    status.Foreground = (Brush)FindResource("AccentBrush");

                    var progress = new Progress<(long read, long total)>(p =>
                    {
                        if (p.total > 0)
                        {
                            var pct = (double)p.read / p.total * 100.0;
                            pbar.Value = pct;
                            status.Text = $"Baixando... {p.read / 1024d / 1024d:N1} MB / {p.total / 1024d / 1024d:N1} MB ({pct:N0}%)";
                        }
                        else
                        {
                            status.Text = $"Baixando... {p.read / 1024d / 1024d:N1} MB";
                        }
                    });

                    try
                    {
                        var downloadedPath = await _driverService.DownloadDirectAsync(driver, progress);
                        pbar.Visibility = Visibility.Collapsed;
                        status.Text = $"● Concluído! Salvo em: {Path.GetFileName(downloadedPath)}";
                        status.Foreground = (Brush)FindResource("SuccessBrush");

                        var res = MessageBox.Show(
                            $"Download de '{driver.Name}' concluído com sucesso!\n\nSalvo em:\n{downloadedPath}\n\nDeseja executar o instalador agora?",
                            "Download Concluído",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Information
                        );
                        if (res == MessageBoxResult.Yes)
                        {
                            Process.Start(new ProcessStartInfo(downloadedPath) { UseShellExecute = true });
                        }
                        ShowDrivers();
                    }
                    catch (Exception ex)
                    {
                        _log.Write("ERROR", $"Download direto de {driver.Name} falhou: {ex.Message}");
                        pbar.Visibility = Visibility.Collapsed;
                        status.Text = $"Falha no download direto. Abrindo página oficial no navegador...";
                        status.Foreground = (Brush)FindResource("WarningBrush");
                        Process.Start(new ProcessStartInfo(driver.OfficialDownloadUrl) { UseShellExecute = true });
                    }
                    finally
                    {
                        directBtn.IsEnabled = true;
                        officialBtn.IsEnabled = true;
                    }
                };
                btnRow.Children.Add(directBtn);
            }

            // Run Installer Button (if file exists)
            if (isDownloaded)
            {
                var runBtn = new Button
                {
                    Content = "▶ Executar Instalador",
                    FontWeight = FontWeights.SemiBold,
                    Padding = new Thickness(12, 6, 12, 6),
                    FontSize = 12
                };
                runBtn.Click += (_, _) =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(existingFile!) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Falha ao iniciar o instalador: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };
                btnRow.Children.Add(runBtn);

                var folderBtn = new Button
                {
                    Content = " Pasta",
                    Padding = new Thickness(10, 6, 10, 6),
                    FontSize = 12
                };
                folderBtn.Click += (_, _) =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{existingFile}\"") { UseShellExecute = true });
                    }
                    catch (Exception ex) { _log.Write("WARN", "Não foi possível abrir a pasta do driver: " + ex.Message); }
                };
                btnRow.Children.Add(folderBtn);
            }

            cardContent.Children.Add(btnRow);
            card.Child = cardContent;
            cardsWrap.Children.Add(card);
        }
    }
    #endregion
}
