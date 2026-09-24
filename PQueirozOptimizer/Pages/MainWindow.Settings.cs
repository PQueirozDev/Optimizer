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
    #region Settings Page
    private void ShowSettings()
    {
        PageTitle.Text = "Configurações";
        PageBadge.Visibility = Visibility.Collapsed;
        var root = new StackPanel { MaxWidth = 980, HorizontalAlignment = HorizontalAlignment.Left };

        // 1. Profile Manager Header
        var profileSection = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(22),
            Margin = new Thickness(0, 0, 0, 24),
            BorderThickness = new Thickness(1)
        };
        profileSection.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        profileSection.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var profileStack = new StackPanel();

        var secTitle = new TextBlock
        {
            Text = "Modos & Perfis de Visualização",
            FontSize = 17.5,
            FontWeight = FontWeights.Bold
        };
        secTitle.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

        var secDesc = new TextBlock
        {
            Text = "Personalize quais otimizações deseja visualizar no aplicativo. Você pode alternar entre os perfis prontos ou salvar seus próprios modos personalizados.",
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 18)
        };
        secDesc.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        profileStack.Children.Add(secTitle);
        profileStack.Children.Add(secDesc);

        // Profile selector row
        var selectorRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
        var selectLabel = new TextBlock
        {
            Text = "Perfil Selecionado:",
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 8)
        };
        selectLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        selectorRow.Children.Add(selectLabel);

        var profileCombo = new ComboBox
        {
            Width = 260,
            Margin = new Thickness(0, 0, 12, 8)
        };

        var allProfiles = _configService.Config.Profiles;
        foreach (var p in allProfiles)
        {
            profileCombo.Items.Add(p.Name + (p.Name == _configService.Config.ActiveProfile ? " (Ativo)" : ""));
        }

        var activeIndex = allProfiles.FindIndex(p => p.Name.Equals(_configService.Config.ActiveProfile, StringComparison.OrdinalIgnoreCase));
        profileCombo.SelectedIndex = activeIndex >= 0 ? activeIndex : 0;
        selectorRow.Children.Add(profileCombo);

        var applyBtn = new Button
        {
            Content = " Ativar Este Perfil",
            Margin = new Thickness(0, 0, 8, 8),
            FontWeight = FontWeights.SemiBold
        };
        selectorRow.Children.Add(applyBtn);

        var deleteBtn = new Button
        {
            Content = " Excluir Perfil",
            Margin = new Thickness(0, 0, 8, 8)
        };
        selectorRow.Children.Add(deleteBtn);

        profileStack.Children.Add(selectorRow);

        // Checklist of optimizations
        var checkListBorder = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 18)
        };
        checkListBorder.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        checkListBorder.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var checkListStack = new StackPanel();

        var checkHeaderDock = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var checkListTitle = new TextBlock
        {
            Text = "Otimizações exibidas neste modo:",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        checkListTitle.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        DockPanel.SetDock(checkListTitle, Dock.Left);
        checkHeaderDock.Children.Add(checkListTitle);

        var quickBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var selectAllBtn = new Button { Content = "Marcar Todas", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0), FontSize = 11.5 };
        var deselectAllBtn = new Button { Content = "Desmarcar Todas", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0), FontSize = 11.5 };
        quickBtns.Children.Add(selectAllBtn);
        quickBtns.Children.Add(deselectAllBtn);
        DockPanel.SetDock(quickBtns, Dock.Right);
        checkHeaderDock.Children.Add(quickBtns);
        checkListStack.Children.Add(checkHeaderDock);

        // Populate checkboxes
        var checkBoxes = new Dictionary<string, CheckBox>();
        var currentProfile = _configService.GetActiveProfile();
        var selectedProfile = (profileCombo.SelectedIndex >= 0 && profileCombo.SelectedIndex < allProfiles.Count)
            ? allProfiles[profileCombo.SelectedIndex]
            : currentProfile;

        var optGrid = new UniformGrid { Columns = 2 };
        foreach (var opt in ConfigService.AllOptimizations)
        {
            var cb = new CheckBox
            {
                IsChecked = selectedProfile.EnabledOptimizations.Contains(opt.Id, StringComparer.OrdinalIgnoreCase),
                Margin = new Thickness(0, 6, 12, 6)
            };

            var cbContent = new StackPanel();
            var cbTitle = new TextBlock { Text = $"{opt.Icon} {opt.Name}", FontSize = 13, FontWeight = FontWeights.Medium };
            cbTitle.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            var cbDesc = new TextBlock { Text = opt.Description, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            cbDesc.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

            cbContent.Children.Add(cbTitle);
            cbContent.Children.Add(cbDesc);
            cb.Content = cbContent;

            checkBoxes[opt.Id] = cb;
            optGrid.Children.Add(cb);
        }
        checkListStack.Children.Add(optGrid);
        checkListBorder.Child = checkListStack;
        profileStack.Children.Add(checkListBorder);

        // Save current changes or Save as new profile row
        var saveRow = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        var saveCurrentBtn = IconButton(Glyphs.Check, "Salvar alterações no perfil", primary: true);
        saveCurrentBtn.Margin = new Thickness(0, 0, 24, 8);
        saveCurrentBtn.VerticalAlignment = VerticalAlignment.Center;
        saveRow.Children.Add(saveCurrentBtn);

        var newProfileBox = new TextBox
        {
            Width = 220,
            ToolTip = "Digite o nome do novo perfil"
        };
        // Texto de exemplo enquanto a caixa está vazia
        var placeholder = new TextBlock { Text = "Nome do novo modo…", IsHitTestVisible = false, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
        placeholder.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        newProfileBox.TextChanged += (_, _) => placeholder.Visibility = string.IsNullOrEmpty(newProfileBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        var newProfileField = new Grid { Margin = new Thickness(0, 0, 8, 8), VerticalAlignment = VerticalAlignment.Center };
        newProfileField.Children.Add(newProfileBox);
        newProfileField.Children.Add(placeholder);
        saveRow.Children.Add(newProfileField);

        var saveAsNewBtn = IconButton(Glyphs.Document, "Salvar como novo modo");
        saveAsNewBtn.Margin = new Thickness(0, 0, 8, 8);
        saveAsNewBtn.VerticalAlignment = VerticalAlignment.Center;
        saveRow.Children.Add(saveAsNewBtn);

        profileStack.Children.Add(saveRow);

        // Actions wiring
        profileCombo.SelectionChanged += (_, _) =>
        {
            if (profileCombo.SelectedIndex >= 0 && profileCombo.SelectedIndex < _configService.Config.Profiles.Count)
            {
                var prof = _configService.Config.Profiles[profileCombo.SelectedIndex];
                deleteBtn.IsEnabled = !prof.IsBuiltIn;
                foreach (var kv in checkBoxes)
                {
                    kv.Value.IsChecked = prof.EnabledOptimizations.Contains(kv.Key, StringComparer.OrdinalIgnoreCase);
                }
            }
        };

        deleteBtn.IsEnabled = !selectedProfile.IsBuiltIn;

        selectAllBtn.Click += (_, _) =>
        {
            foreach (var cb in checkBoxes.Values) cb.IsChecked = true;
        };

        deselectAllBtn.Click += (_, _) =>
        {
            foreach (var cb in checkBoxes.Values) cb.IsChecked = false;
        };

        applyBtn.Click += (_, _) =>
        {
            if (profileCombo.SelectedIndex >= 0 && profileCombo.SelectedIndex < _configService.Config.Profiles.Count)
            {
                var prof = _configService.Config.Profiles[profileCombo.SelectedIndex];
                _configService.SetActiveProfile(prof.Name);
                UpdateNavBadges();
        UpdateActiveNavButton(_currentPage);
                Msg($"Perfil '{prof.Name}' ativado com sucesso!", "Perfil Ativado", MessageBoxButton.OK, MessageBoxImage.Information);
                ShowSettings();
            }
        };

        saveCurrentBtn.Click += (_, _) =>
        {
            if (profileCombo.SelectedIndex >= 0 && profileCombo.SelectedIndex < _configService.Config.Profiles.Count)
            {
                var prof = _configService.Config.Profiles[profileCombo.SelectedIndex];
                var enabled = checkBoxes.Where(c => c.Value.IsChecked == true).Select(c => c.Key).ToList();
                _configService.SaveProfile(prof.Name, prof.Description, enabled);
                UpdateNavBadges();
        UpdateActiveNavButton(_currentPage);
                Msg($"Perfil '{prof.Name}' atualizado com sucesso!", "Salvo", MessageBoxButton.OK, MessageBoxImage.Information);
                ShowSettings();
            }
        };

        saveAsNewBtn.Click += (_, _) =>
        {
            var name = newProfileBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                Msg("Por favor, digite um nome para o novo perfil.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var enabled = checkBoxes.Where(c => c.Value.IsChecked == true).Select(c => c.Key).ToList();
            _configService.SaveProfile(name, "Perfil personalizado do usuário", enabled);
            UpdateNavBadges();
        UpdateActiveNavButton(_currentPage);
            Msg($"Novo perfil '{name}' criado e ativado com sucesso!", "Perfil Criado", MessageBoxButton.OK, MessageBoxImage.Information);
            ShowSettings();
        };

        deleteBtn.Click += (_, _) =>
        {
            if (profileCombo.SelectedIndex >= 0 && profileCombo.SelectedIndex < _configService.Config.Profiles.Count)
            {
                var prof = _configService.Config.Profiles[profileCombo.SelectedIndex];
                if (prof.IsBuiltIn)
                {
                    Msg("Perfis padrão do sistema não podem ser excluídos.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var confirm = Msg($"Deseja realmente excluir o perfil '{prof.Name}'?", "Confirmar Exclusão", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm == MessageBoxResult.Yes)
                {
                    _configService.DeleteProfile(prof.Name);
                    UpdateNavBadges();
        UpdateActiveNavButton(_currentPage);
                    Msg($"Perfil '{prof.Name}' excluído.", "Excluído", MessageBoxButton.OK, MessageBoxImage.Information);
                    ShowSettings();
                }
            }
        };

        profileSection.Child = profileStack;
        root.Children.Add(profileSection);

        // 2. Appearance Section (Theme Selector)
        var themeSection = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(22),
            Margin = new Thickness(0, 0, 0, 24),
            BorderThickness = new Thickness(1)
        };
        themeSection.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        themeSection.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var themeStack = new StackPanel();
        var themeTitle = new TextBlock { Text = "Aparência & Tema", FontSize = 17.5, FontWeight = FontWeights.Bold };
        themeTitle.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

        var themeDesc = new TextBlock
        {
            Text = "Escolha o esquema de cores para o aplicativo. Todas as janelas e componentes se adaptam instantaneamente.",
            FontSize = 12.5,
            Margin = new Thickness(0, 4, 0, 16)
        };
        themeDesc.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        themeStack.Children.Add(themeTitle);
        themeStack.Children.Add(themeDesc);

        var themeBtns = new WrapPanel();
        var darkBtn = new Button
        {
            Content = "  Modo Escuro (Dark)",
            Padding = new Thickness(16, 9, 16, 9),
            FontWeight = _darkTheme ? FontWeights.Bold : FontWeights.Normal
        };
        darkBtn.Click += (_, _) => { ApplyTheme(true, saveConfig: true); ShowSettings(); };

        var lightBtn = new Button
        {
            Content = "  Modo Claro (Light)",
            Padding = new Thickness(16, 9, 16, 9),
            FontWeight = !_darkTheme ? FontWeights.Bold : FontWeights.Normal
        };
        lightBtn.Click += (_, _) => { ApplyTheme(false, saveConfig: true); ShowSettings(); };

        themeBtns.Children.Add(darkBtn);
        themeBtns.Children.Add(lightBtn);
        themeStack.Children.Add(themeBtns);

        themeSection.Child = themeStack;
        root.Children.Add(themeSection);

        // 3. Language Section (App-wide language switch)
        var langSection = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(22),
            Margin = new Thickness(0, 0, 0, 24),
            BorderThickness = new Thickness(1)
        };
        langSection.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        langSection.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var langStack = new StackPanel();
        var langTitle = new TextBlock
        {
            Text = " Language / Idioma",
            FontSize = 17.5,
            FontWeight = FontWeights.Bold
        };
        langTitle.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

        var langDesc = new TextBlock
        {
            Text = _loc.T(
                "Traduz todo o aplicativo (menus, páginas, drivers e mensagens) para o idioma selecionado.",
                "Translates the entire app (menus, pages, drivers and messages) to the selected language."),
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 16)
        };
        langDesc.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        langStack.Children.Add(langTitle);
        langStack.Children.Add(langDesc);

        var langBtns = new WrapPanel();
        var ptBtn = new Button
        {
            Content = "  Português (PT)",
            Padding = new Thickness(16, 9, 16, 9),
            FontWeight = !_loc.IsEnglish ? FontWeights.Bold : FontWeights.Normal
        };
        ptBtn.Click += (_, _) => ChangeLanguage("pt");

        var enBtn = new Button
        {
            Content = "  English (EN)",
            Padding = new Thickness(16, 9, 16, 9),
            FontWeight = _loc.IsEnglish ? FontWeights.Bold : FontWeights.Normal
        };
        enBtn.Click += (_, _) => ChangeLanguage("en");

        langBtns.Children.Add(ptBtn);
        langBtns.Children.Add(enBtn);
        langStack.Children.Add(langBtns);

        langSection.Child = langStack;
        root.Children.Add(langSection);

        ContentHost.Children.Clear();
        ContentHost.Children.Add(root);
    }
    #endregion
}
