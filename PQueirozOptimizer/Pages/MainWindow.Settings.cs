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
        if ((Application.Current as App)?.ActiveLicense is { } license) root.Children.Add(LicenseSection(license));

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
            FontSize = 18,
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
            FontSize = 12.5,
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

        var applyBtn = IconButton(Glyphs.Check, "Ativar Este Perfil");
        applyBtn.Margin = new Thickness(0, 0, 8, 8);
        selectorRow.Children.Add(applyBtn);

        var deleteBtn = IconButton(Glyphs.Delete, "Excluir Perfil");
        deleteBtn.Margin = new Thickness(0, 0, 8, 8);
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
        var selectAllBtn = new Button { Content = "Marcar Todas", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0), FontSize = 11 };
        var deselectAllBtn = new Button { Content = "Desmarcar Todas", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0), FontSize = 11 };
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
            var cbTitle = new TextBlock { Text = $"{opt.Icon} {opt.Name}", FontSize = 12.5, FontWeight = FontWeights.Medium };
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
        var placeholder = new TextBlock { Text = "Nome do novo modo…", IsHitTestVisible = false, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5 };
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
        // "Padrão" e "Modo Completo" sempre mostram tudo: alterar a lista deles não sobreviveria à próxima abertura
        var fixedHint = Label("Este modo sempre exibe todas as otimizações. Para personalizar, marque o que quiser e salve como novo modo.", 12, true);
        fixedHint.Margin = new Thickness(0, 4, 0, 0);
        profileStack.Children.Add(fixedHint);
        void RefreshProfileActions(OptimizationProfile prof)
        {
            deleteBtn.IsEnabled = !prof.IsBuiltIn;
            saveCurrentBtn.IsEnabled = !ConfigService.ShowsEverything(prof.Name);
            fixedHint.Visibility = saveCurrentBtn.IsEnabled ? Visibility.Collapsed : Visibility.Visible;
        }

        // Actions wiring
        profileCombo.SelectionChanged += (_, _) =>
        {
            if (profileCombo.SelectedIndex >= 0 && profileCombo.SelectedIndex < _configService.Config.Profiles.Count)
            {
                var prof = _configService.Config.Profiles[profileCombo.SelectedIndex];
                RefreshProfileActions(prof);
                foreach (var kv in checkBoxes)
                {
                    kv.Value.IsChecked = prof.EnabledOptimizations.Contains(kv.Key, StringComparer.OrdinalIgnoreCase);
                }
            }
        };

        RefreshProfileActions(selectedProfile);

        selectAllBtn.Click += (_, _) =>
        {
            foreach (var cb in checkBoxes.Values) cb.IsChecked = true;
        };

        deselectAllBtn.Click += (_, _) =>
        {
            foreach (var cb in checkBoxes.Values) cb.IsChecked = false;
        };

        OptimizationProfile? SelectedProfile() =>
            profileCombo.SelectedIndex >= 0 && profileCombo.SelectedIndex < _configService.Config.Profiles.Count ? _configService.Config.Profiles[profileCombo.SelectedIndex] : null;
        List<string> CheckedIds() => checkBoxes.Where(c => c.Value.IsChecked == true).Select(c => c.Key).ToList();
        // Grava e mostra a confirmação; se o arquivo estiver bloqueado, avisa em vez de fechar o app
        void SaveAndConfirm(Action save, string message, string caption)
        {
            try { save(); }
            catch (IOException ex)
            {
                Msg("Não foi possível salvar suas preferências: " + (ex.InnerException?.Message ?? ex.Message), "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            UpdateNavBadges();
            Msg(message, caption, MessageBoxButton.OK, MessageBoxImage.Information);
            ShowSettings();
        }

        applyBtn.Click += (_, _) =>
        {
            if (SelectedProfile() is not { } prof) return;
            SaveAndConfirm(() => _configService.SetActiveProfile(prof.Name), $"Perfil '{prof.Name}' ativado com sucesso!", "Perfil Ativado");
        };

        saveCurrentBtn.Click += (_, _) =>
        {
            if (SelectedProfile() is not { } prof) return;
            SaveAndConfirm(() => _configService.SaveProfile(prof.Name, prof.Description, CheckedIds()), $"Perfil '{prof.Name}' atualizado com sucesso!", "Salvo");
        };

        saveAsNewBtn.Click += (_, _) =>
        {
            var name = newProfileBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                Msg("Por favor, digite um nome para o novo perfil.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (_configService.Config.Profiles.Any(p => p.IsBuiltIn && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                Msg("Já existe um modo pronto com esse nome. Escolha outro nome.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            SaveAndConfirm(() => _configService.SaveProfile(name, "Perfil personalizado do usuário", CheckedIds()), $"Novo perfil '{name}' criado e ativado com sucesso!", "Perfil Criado");
        };

        deleteBtn.Click += (_, _) =>
        {
            if (SelectedProfile() is not { } prof) return;
            if (prof.IsBuiltIn)
            {
                Msg("Perfis padrão do sistema não podem ser excluídos.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (Msg($"Deseja realmente excluir o perfil '{prof.Name}'?", "Confirmar Exclusão", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                SaveAndConfirm(() => _configService.DeleteProfile(prof.Name), $"Perfil '{prof.Name}' excluído.", "Excluído");
        };

        profileSection.Child = profileStack;
        root.Children.Add(profileSection);

        // 2. Aparência (tema, roxo, densidade, cards, animações) e tutoriais
        root.Children.Insert(0, AppearanceSection());
        root.Children.Add(TutorialsSection());

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
            Text = "Language / Idioma",
            FontSize = 18,
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
        foreach (var (code, name) in new[] { ("pt", "Português (PT)"), ("en", "English (EN)") })
        {
            var button = IconButton(Glyphs.Check, name, primary: _loc.CurrentLanguage == code);
            // Nome do idioma sempre na própria língua, nunca traduzido
            if (button.Content is StackPanel { Children.Count: > 1 } content && content.Children[1] is TextBlock label) label.Tag = Translator.SystemDataTag;
            button.Click += (_, _) => ChangeLanguage(code);
            langBtns.Children.Add(button);
        }
        langStack.Children.Add(langBtns);

        langSection.Child = langStack;
        root.Children.Add(langSection);
        root.Children.Add(UpdatesSection());

        ContentHost.Children.Clear();
        ContentHost.Children.Add(root);
    }

    /// <summary>Versão instalada e verificação manual de atualizações (a automática roda ao abrir o app).</summary>
    private Border UpdatesSection()
    {
        var stack = new StackPanel();
        var title = Label(_loc.T("Atualizações", "Updates"), 17.5);
        title.FontWeight = FontWeights.Bold; title.Margin = new Thickness(0);
        stack.Children.Add(title);
        var desc = Label(_loc.T(
            $"Versão instalada: {AppVersion}. O aplicativo procura novas versões ao abrir; use o botão para verificar agora.",
            $"Installed version: {AppVersion}. The app looks for new versions on startup; use the button to check now."), 12.5, true);
        desc.TextWrapping = TextWrapping.Wrap; desc.Margin = new Thickness(0, 4, 0, 16);
        stack.Children.Add(desc);

        var check = IconButton(Glyphs.Refresh, _loc.T("Verificar atualizações", "Check for updates"), primary: true);
        var status = Label("", 12.5, true);
        status.TextWrapping = TextWrapping.Wrap; status.VerticalAlignment = VerticalAlignment.Center; status.Margin = new Thickness(14, 0, 0, 0);
        check.Click += async (_, _) => await CheckForUpdateManuallyAsync(check, status);
        stack.Children.Add(new DockPanel { Children = { check, status } });
        DockPanel.SetDock(check, Dock.Left);

        var section = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(22), Margin = new Thickness(0, 0, 0, 24), BorderThickness = new Thickness(1), Child = stack };
        section.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        section.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        return section;
    }

    /// <summary>
    /// Minha licença: plano, validade e o que o cliente pode fazer a partir dele (renovar o Mensal,
    /// pedir o upgrade para o Vitalício, ativar outra chave ou chamar o suporte).
    /// </summary>
    private Border LicenseSection(LicenseInfo license)
    {
        var tone = license.IsLifetime ? "Success" : license.IsExpiringSoon ? "Warning" : "Info";
        var stack = new StackPanel();

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var plan = Pill(license.IsAdmin ? $"{license.PlanName} · Admin" : license.PlanName, tone);
        plan.Margin = new Thickness(12, 0, 0, 0);
        DockPanel.SetDock(plan, Dock.Right); header.Children.Add(plan);
        var chip = IconChip(Glyphs.Key, "Accent", 40); DockPanel.SetDock(chip, Dock.Left); header.Children.Add(chip);
        var titles = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var title = Label("Minha licença", 17.5); title.FontWeight = FontWeights.Bold; title.Margin = new Thickness(0);
        titles.Children.Add(title);
        var subtitle = Label(license.IsLifetime
            ? "Plano Vitalício: sem data de vencimento, com reemissão da chave após formatar e suporte prioritário."
            : "Plano com validade: quando estiver perto de vencer, abra um ticket de renovação no Discord e pague via Pix.", 12.5, true);
        subtitle.Margin = new Thickness(0, 3, 0, 0);
        titles.Children.Add(subtitle);
        header.Children.Add(titles);
        stack.Children.Add(header);

        var info = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        info.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        info.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var validity = license.ExpiresAtUtc is { } expires
            ? license.DaysLeft switch { 0 => $"Até {expires.ToLocalTime():dd/MM/yyyy} (vence hoje)", 1 => $"Até {expires.ToLocalTime():dd/MM/yyyy} (vence amanhã)", var d => $"Até {expires.ToLocalTime():dd/MM/yyyy} ({d} dias)" }
            : "Vitalícia";
        var rows = new (string Label, string Value, bool Mono)[]
        {
            ("Titular", license.Licensee, false),
            ("Plano", license.PlanName, false),
            ("Validade", validity, false),
            ("Tipo", license.IsAdmin ? "Admin" : "Standard", false),
            ("ID do computador", new LicenseService().DisplayMachineId, true),
        };
        for (var i = 0; i < rows.Length; i++)
        {
            info.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var name = Label(rows[i].Label, 12.5, true); name.Margin = new Thickness(0, 3, 24, 3);
            var value = Label(rows[i].Value, 12.5); value.FontWeight = FontWeights.SemiBold; value.Margin = new Thickness(0, 3, 0, 3);
            // Nome e ID vêm da chave, não do app: não passam pelo tradutor
            if (i is 0 or 4) value.Tag = Translator.SystemDataTag;
            if (rows[i].Mono) value.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            Grid.SetRow(name, i); Grid.SetRow(value, i); Grid.SetColumn(value, 1);
            info.Children.Add(name); info.Children.Add(value);
        }
        stack.Children.Add(info);

        var actions = new WrapPanel();
        void CopyRequest(string request, string done)
        {
            try { Clipboard.SetText(request); OperationStatus.Text = done; }
            catch (System.Runtime.InteropServices.COMException) { OperationStatus.Text = "A área de transferência está ocupada. Tente novamente."; }
        }
        if (!license.IsLifetime)
        {
            var renew = IconButton(Glyphs.OpenInNew, "Renovar pelo Discord", primary: true);
            renew.ToolTip = "Copia o pedido de renovação e abre o Discord: abra um ticket de renovação, cole o pedido e pague via Pix";
            renew.Click += (_, _) => RenewViaDiscord(license);
            var upgrade = IconButton(Glyphs.Lightning, "Quero o Vitalício");
            upgrade.ToolTip = "Copia um pedido de upgrade: pague uma vez e não precisa mais renovar";
            upgrade.Click += (_, _) => CopyRequest(new LicenseService().BuildActivationRequest(license.Licensee, LicensePlans.Lifetime), "Pedido de upgrade para o Vitalício copiado. No Discord, abra um ticket, cole o pedido e pague via Pix.");
            actions.Children.Add(renew); actions.Children.Add(upgrade);
        }
        var activate = IconButton(Glyphs.Key, "Ativar outra chave");
        activate.Click += (_, _) => ActivateAdminLicense();
        var support = IconButton(Glyphs.OpenInNew, license.IsLifetime ? "Suporte prioritário (Discord)" : "Suporte (Discord)");
        support.Click += (_, _) => OpenUrl(LicensePlans.DiscordUrl);
        actions.Children.Add(activate); actions.Children.Add(support);
        stack.Children.Add(actions);

        var card = Surface(stack);
        card.Margin = new Thickness(0, 0, 0, 24);
        card.SetResourceReference(Border.BorderBrushProperty, tone + "SoftBrush");
        return card;
    }
    #endregion
}
