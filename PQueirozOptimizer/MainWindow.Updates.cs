using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using PQueirozOptimizer.Services;
using PQueirozOptimizer.Models;

namespace PQueirozOptimizer;

/// <summary>Atualizações: verificação, aviso no painel e instalação.</summary>
public partial class MainWindow
{
    private readonly UpdateService _updates = new();
    private StackPanel? _updateSlot;
    private UpdateInfo? _pendingUpdate;

    private async Task CheckForUpdateAsync(bool showPrompt = false)
    {
        try
        {
            var info = await _updates.CheckAsync(AppVersion);
            if (!info.IsAvailable || (string.IsNullOrWhiteSpace(info.AssetUrl) && string.IsNullOrWhiteSpace(info.DownloadUrl))) return;
            _pendingUpdate = info;
            ShowUpdateBanner();
            if (!_operationRunning) OperationStatus.Text = $"Atualização disponível: {info.LatestVersion}";
            // Ao abrir o app: janela com as novidades (depois do tutorial, se ele estiver na tela)
            if (showPrompt)
            {
                while (_tutorialSteps != null) await Task.Delay(500);
                ShowUpdateDialog(info);
            }
        }
        catch (Exception ex)
        {
            // Atualização é opcional e não deve impedir o dashboard
            _log.Write("WARN", "Não foi possível verificar atualizações: " + ex.Message);
        }
    }

    /// <summary>Verificação pedida pelo usuário: sempre dá uma resposta (nova versão, já atualizado ou falha).</summary>
    private async Task CheckForUpdateManuallyAsync(Button button, TextBlock status)
    {
        button.IsEnabled = false;
        status.Text = _loc.T("Verificando...", "Checking...");
        try
        {
            var info = await _updates.CheckAsync(AppVersion);
            if (!info.IsAvailable || (string.IsNullOrWhiteSpace(info.AssetUrl) && string.IsNullOrWhiteSpace(info.DownloadUrl)))
            {
                status.Text = _loc.T($"Você já está na versão mais recente ({AppVersion}).", $"You are on the latest version ({AppVersion}).");
                return;
            }
            _pendingUpdate = info;
            status.Text = _loc.T($"Nova versão disponível: {info.LatestVersion}", $"New version available: {info.LatestVersion}");
            if (!_operationRunning) OperationStatus.Text = $"Atualização disponível: {info.LatestVersion}";
            if (Msg($"A versão {info.LatestVersion} está disponível. Deseja atualizar agora?", "Atualização disponível", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                await InstallUpdateAsync(info, button);
        }
        catch (Exception ex)
        {
            status.Text = _loc.T("Não foi possível verificar agora. Confira a conexão com a internet.", "Could not check right now. Check your internet connection.");
            _log.Write("WARN", "Não foi possível verificar atualizações: " + ex.Message);
        }
        finally { button.IsEnabled = true; }
    }

    /// <summary>Mostra o aviso de nova versão no painel (é recriado sempre que o painel é redesenhado).</summary>
    private void ShowUpdateBanner()
    {
        if (_pendingUpdate is not { } info || _currentPage != "dashboard" || _updateSlot is null) return;
        var update = IconButton(Glyphs.Download, UpdateService.CanAutoInstall(info) ? $"Atualizar para {info.LatestVersion}" : $"Baixar {info.LatestVersion}", primary: true);
        update.Margin = new Thickness(12, 0, 0, 0); update.VerticalAlignment = VerticalAlignment.Center;
        update.Click += async (_, _) => await InstallUpdateAsync(info, update);

        var banner = new DockPanel();
        DockPanel.SetDock(update, Dock.Right); banner.Children.Add(update);
        var details = IconButton(Glyphs.Info, "Ver novidades");
        details.Margin = new Thickness(12, 0, 0, 0); details.VerticalAlignment = VerticalAlignment.Center;
        details.Click += (_, _) => ShowUpdateDialog(info);
        DockPanel.SetDock(details, Dock.Right); banner.Children.Add(details);
        var chip = IconChip(Glyphs.Download, "Accent", 46); DockPanel.SetDock(chip, Dock.Left); banner.Children.Add(chip);
        var bannerText = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var bt = Label($"Nova versão disponível · {info.LatestVersion}", 16); bt.FontWeight = FontWeights.Bold; bt.Margin = new Thickness(0);
        bannerText.Children.Add(bt);
        var bs = Label(UpdateService.CanAutoInstall(info) ? "Instalador seguro, verificado por SHA256 e pronto para atualizar." : "Baixe a versão pela página oficial do GitHub.", 12, true); bs.Margin = new Thickness(0, 4, 0, 0);
        bannerText.Children.Add(bs);
        banner.Children.Add(bannerText);
        var bannerCard = Surface(banner); bannerCard.Padding = new Thickness(20, 16, 20, 16);
        bannerCard.SetResourceReference(Border.BackgroundProperty, "HeroBrush");
        bannerCard.SetResourceReference(Border.BorderBrushProperty, "InfoBrush");
        bannerCard.BorderThickness = new Thickness(1);
        _updateSlot.Children.Clear();
        _updateSlot.Children.Add(bannerCard);
    }

    private bool _installingUpdate;

    private async Task InstallUpdateAsync(UpdateInfo info, Button? button)
    {
        if (_operationRunning) { OperationStatus.Text = "Aguarde a operação em andamento terminar antes de atualizar."; return; }
        if (!UpdateService.CanAutoInstall(info)) { OpenUrl(info.DownloadUrl); return; }
        // O aviso ao abrir e o botão do painel podem pedir a mesma atualização
        if (_installingUpdate) return;
        _installingUpdate = true;
        if (button != null) button.IsEnabled = false;
        OperationProgress.Visibility = Visibility.Visible;
        ReportUpdateProgress("Baixando instalador...", null);
        try
        {
            using var installer = await _updates.DownloadAsync(info, new Progress<(long read, long total)>(p => ReportUpdateProgress(
                p.total > 0 ? $"Baixando instalador... {p.read * 100d / p.total:N0}% ({p.read / 1048576d:N1} de {p.total / 1048576d:N1} MB)" : $"Baixando instalador... {p.read / 1048576d:N1} MB",
                p.total > 0 ? p.read * 100d / p.total : null)));
            ReportUpdateProgress("Instalador verificado por SHA256. Instalando — o app reabre sozinho em instantes...", 100);
            _log.Write("INFO", $"Atualização {info.LatestVersion} verificada por SHA256; iniciando instalação silenciosa.");
            installer.LaunchSilent();
            await Task.Delay(500);
            Application.Current.Shutdown();
        }
        catch (Exception ex) { ReportUpdateProgress("Falha na atualização: " + ex.Message, 0); _log.Write("ERROR", "Atualização: " + ex.Message); }
        finally { _installingUpdate = false; OperationProgress.Visibility = Visibility.Collapsed; if (button != null) button.IsEnabled = true; if (_updateLater != null) _updateLater.IsEnabled = true; }
    }
}
