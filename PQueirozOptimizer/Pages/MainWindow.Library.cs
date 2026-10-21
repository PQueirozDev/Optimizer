using System.IO;
using System.Windows;
using System.Windows.Controls;
using PQueirozOptimizer.Services;

namespace PQueirozOptimizer;

/// <summary>Biblioteca: todos os jogos instalados (Steam, Epic, GOG, Ubisoft e adicionados à mão) num só lugar.</summary>
public partial class MainWindow
{
    private string _libraryStore = "";
    private string _libraryQuery = "";

    private void ShowLibrary()
    {
        PageTitle.Text = "Biblioteca";
        PageBadge.Visibility = Visibility.Collapsed;
        PageActions.Children.Clear();
        PageActions.Children.Add(RoundIcon(Glyphs.Refresh, "Procurar jogos de novo", () => NavigateTo("library")));
        var add = IconButton(Glyphs.Add, L("Adicionar jogo", "Add game"));
        add.Margin = new Thickness(8, 0, 0, 0);
        add.Click += (_, _) => AddLibraryGame();
        PageActions.Children.Add(add);
        var root = new StackPanel();
        ContentHost.Children.Clear(); ContentHost.Children.Add(root);
        root.Children.Add(LoadingCard(L("Procurando jogos instalados…", "Looking for installed games…")));
        _ = Task.Run(GameLibraryService.Scan).ContinueWith(t =>
        {
            root.Children.Clear();
            if (!t.IsCompletedSuccessfully) { root.Children.Add(EmptyState(Glyphs.Warning, L("Não foi possível ler a biblioteca", "Could not read the library"), t.Exception?.InnerException?.Message ?? "")); return; }
            RenderLibrary(root, t.Result);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void RenderLibrary(StackPanel root, List<LibraryGame> games)
    {
        if (games.Count == 0)
        {
            var add = IconButton(Glyphs.Add, L("Adicionar jogo", "Add game"), primary: true);
            add.Click += (_, _) => AddLibraryGame();
            root.Children.Add(EmptyState(Glyphs.Game, L("Nenhum jogo encontrado", "No games found"),
                L("Procuramos na Steam, Epic Games, GOG e Ubisoft Connect. Jogos de outras lojas podem ser adicionados pelo executável.", "We looked in Steam, Epic Games, GOG and Ubisoft Connect. Games from other stores can be added by their executable."), add));
            return;
        }
        // Filtros: busca e lojas (chips)
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, AppearanceService.Space(16)) };
        var search = new TextBox { Width = 260, Height = 34, Padding = new Thickness(12, 7, 12, 7), FontSize = 12.5, Text = _libraryQuery, ToolTip = L("Buscar jogo", "Search game") };
        DockPanel.SetDock(search, Dock.Left); bar.Children.Add(search);
        var chips = new WrapPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        bar.Children.Add(chips);
        root.Children.Add(bar);
        var grid = HomeGrid(230);
        root.Children.Add(grid);
        var stores = games.GroupBy(g => g.Store).OrderByDescending(g => g.Count()).Select(g => (Store: g.Key, Count: g.Count())).ToList();

        void Render()
        {
            chips.Children.Clear();
            foreach (var (store, count) in stores.Prepend(("", games.Count)))
            {
                var label = store.Length == 0 ? L($"Todos · {count}", $"All · {count}") : $"{(store == "Manual" ? L("Adicionados", "Added") : store)} · {count}";
                var chip = new Button { Content = label, Padding = new Thickness(13, 5, 13, 5), Margin = new Thickness(0, 0, 6, 6), Tag = Translator.SystemDataTag };
                QrzUi.SetCornerRadius(chip, new CornerRadius(14));
                chip.SetResourceReference(StyleProperty, store == _libraryStore ? "PrimaryButton" : "SecondaryButton");
                var s = store;
                chip.Click += (_, _) => { _libraryStore = s; Render(); };
                chips.Children.Add(chip);
            }
            grid.Children.Clear();
            var query = search.Text.Trim();
            foreach (var game in games.Where(g => (_libraryStore.Length == 0 || g.Store == _libraryStore) && (query.Length == 0 || g.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))))
                grid.Children.Add(LibraryCard(game));
        }
        search.TextChanged += (_, _) => { _libraryQuery = search.Text; Render(); };
        Render();
    }

    private Border LibraryCard(LibraryGame game)
    {
        var body = new StackPanel();
        Func<Task<System.Windows.Media.Imaging.BitmapSource?>> art = game.Store == "Steam" && int.TryParse(game.Id, out var appId)
            ? () => GameArtService.SteamBannerAsync(appId)
            : async () => await Task.Run(() => GameLibraryService.GuessExecutable(game)) is { } exe ? await GameArtService.ExecutableArtAsync(exe) : null;
        var banner = GameBanner(game.Name, 96, 12, art);
        banner.Margin = new Thickness(0, 0, 0, 12);
        body.Children.Add(banner);
        var name = new TextBlock { Text = game.Name, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = game.Name, Tag = Translator.SystemDataTag };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        body.Children.Add(name);
        var store = new TextBlock { Text = game.Store == "Manual" ? L("Adicionado à mão", "Added manually") : game.Store, FontSize = 12, Margin = new Thickness(0, 2, 0, 12), Tag = Translator.SystemDataTag };
        store.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        body.Children.Add(store);
        var actions = new DockPanel();
        var play = IconButton(Glyphs.Play, L("Jogar", "Play"), primary: true);
        play.Margin = new Thickness(0);
        play.Click += (_, _) =>
        {
            try
            {
                GameLibraryService.Launch(game);
                OperationStatus.Text = L("Abrindo ", "Launching ") + game.Name;
                _log.Write("INFO", "Biblioteca: abrindo " + game.Name + " (" + game.Store + ")");
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException or System.Runtime.InteropServices.COMException or IOException)
            {
                ShowToast(game.Name, ex.Message, "Danger");
            }
        };
        DockPanel.SetDock(play, Dock.Left); actions.Children.Add(play);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(RoundIcon(Glyphs.Folder, "Abrir a pasta do jogo", () =>
        {
            if (Directory.Exists(game.InstallDir))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "\"" + game.InstallDir + "\"") { UseShellExecute = false });
        }));
        if (game.IsManual)
        {
            var remove = RoundIcon(Glyphs.Delete, "Tirar da biblioteca", () => { GameLibraryService.RemoveCustom(game.Id); ShowLibrary(); });
            remove.Margin = new Thickness(6, 0, 0, 0);
            right.Children.Add(remove);
        }
        actions.Children.Add(right);
        body.Children.Add(actions);
        return HomeCard(body);
    }

    private void AddLibraryGame()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = L("Programas (*.exe)|*.exe", "Programs (*.exe)|*.exe"), Title = L("Escolha o executável do jogo", "Choose the game's executable") };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            GameLibraryService.AddCustom(dialog.FileName);
            ShowLibrary();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowToast(L("Biblioteca", "Library"), ex.Message, "Danger");
        }
    }
}
