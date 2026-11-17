namespace GrayCat.UI.Views;

using GrayCat.UI.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GrayCat.Core.Generation;
using GrayCat.Core.Models;
using GrayCat.UI.ViewModels;

/// <summary>Add, rename, reorder and delete the pages of the site.</summary>
public class PagesWindow : Controls.ThemedWindow
{
    private readonly MainViewModel _viewModel;
    private readonly ListBox _list;
    private readonly TextBox _title;
    private readonly TextBox _slug;
    private readonly CheckBox _inMenu;
    private readonly TextBlock _error;
    private readonly Button _delete;
    private bool _slugTouched;

    public PagesWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        Title = Loc.T("Pages");
        Width = 620;
        Height = 440;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(16) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        root.ColumnDefinitions.Add(new ColumnDefinition());

        // Left: the page list.
        var left = new DockPanel();
        var listButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(listButtons, Dock.Bottom);
        var add = SmallButton(Loc.T("+ New page"));
        var up = SmallButton("▲");
        var down = SmallButton("▼");
        listButtons.Children.Add(add);
        listButtons.Children.Add(up);
        listButtons.Children.Add(down);
        _list = new ListBox { DisplayMemberPath = nameof(PageInfo.Title), ItemsSource = viewModel.Pages };
        left.Children.Add(listButtons);
        left.Children.Add(_list);
        Grid.SetColumn(left, 0);
        root.Children.Add(left);

        // Right: details of the selected page.
        var right = new StackPanel();
        right.Children.Add(Label(Loc.T("Page title (shown in the menu)")));
        _title = new TextBox { Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 12) };
        right.Children.Add(_title);
        right.Children.Add(Label(Loc.T("Address")));
        _slug = new TextBox { Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 4) };
        right.Children.Add(_slug);
        right.Children.Add(new TextBlock
        {
            Text = Loc.T("Lowercase letters, digits and dashes. The page is available at /address (about.html in static sites)."),
            Foreground = new SolidColorBrush(Color.FromRgb(108, 117, 125)),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });
        _inMenu = new CheckBox { Content = Loc.T("Show in navigation menus"), Margin = new Thickness(0, 0, 0, 12) };
        right.Children.Add(_inMenu);
        _error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        right.Children.Add(_error);

        var detailButtons = new StackPanel { Orientation = Orientation.Horizontal };
        var apply = SmallButton(Loc.T("Apply"));
        var open = SmallButton(Loc.T("Edit this page"));
        _delete = SmallButton(Loc.T("Delete page"));
        _delete.Background = new SolidColorBrush(Color.FromRgb(179, 54, 43));
        detailButtons.Children.Add(apply);
        detailButtons.Children.Add(open);
        detailButtons.Children.Add(_delete);
        right.Children.Add(detailButtons);
        Grid.SetColumn(right, 2);
        root.Children.Add(right);

        Content = root;

        _list.SelectionChanged += (_, _) => ShowSelected();
        _title.TextChanged += (_, _) =>
        {
            // Suggest an address from the title for new pages until the user edits it.
            if (!_slugTouched && Selected is { } page && !string.IsNullOrEmpty(page.Slug) && _slug.IsEnabled)
                _slug.Text = ProjectPages.SlugFrom(_title.Text);
        };
        _slug.PreviewTextInput += (_, _) => _slugTouched = true;

        add.Click += (_, _) => AddPage();
        up.Click += (_, _) => { if (Selected is { } p) { _viewModel.MovePage(p, -1); _list.SelectedItem = p; } };
        down.Click += (_, _) => { if (Selected is { } p) { _viewModel.MovePage(p, +1); _list.SelectedItem = p; } };
        apply.Click += (_, _) => Apply();
        open.Click += (_, _) =>
        {
            if (Selected is { } p)
            {
                _viewModel.CurrentPage = p;
                Close();
            }
        };
        _delete.Click += (_, _) => DeleteSelected();

        _list.SelectedItem = viewModel.CurrentPage;
        ShowSelected();
    }

    private PageInfo? Selected => _list.SelectedItem as PageInfo;

    private void ShowSelected()
    {
        _error.Text = string.Empty;
        _slugTouched = false;
        if (Selected is not { } page)
            return;
        var isHome = string.IsNullOrEmpty(page.Slug);
        _title.Text = page.Title;
        _slug.Text = isHome ? Loc.T("(home page)") : page.Slug;
        _slug.IsEnabled = !isHome;
        _inMenu.IsChecked = page.ShowInMenu;
        _delete.IsEnabled = !isHome;
        _slugTouched = true; // existing pages keep their address unless the user changes it
    }

    private void AddPage()
    {
        var number = _viewModel.Pages.Count + 1;
        var slug = $"page-{number}";
        while (_viewModel.Pages.Any(p => p.Slug == slug))
            slug = $"page-{++number}";

        if (_viewModel.AddPage(Loc.F("Page {0}", number), slug) is { } error)
        {
            _error.Text = Loc.T(error);
            return;
        }
        _list.SelectedItem = _viewModel.CurrentPage;
        _slugTouched = false;
        _title.Focus();
        _title.SelectAll();
    }

    private void Apply()
    {
        if (Selected is not { } page)
            return;
        var slug = string.IsNullOrEmpty(page.Slug) ? string.Empty : _slug.Text;
        var error = _viewModel.UpdatePage(page, _title.Text, slug, _inMenu.IsChecked == true);
        _error.Text = error == null ? string.Empty : Loc.T(error);
        _list.Items.Refresh();
        _list.SelectedItem = page;
    }

    private void DeleteSelected()
    {
        if (Selected is not { } page || string.IsNullOrEmpty(page.Slug))
            return;
        if (ThemedMessageBox.Show(this, Loc.F("Delete the page \"{0}\" and all its blocks?\n\nYou can undo this with Ctrl+Z.", page.Title),
                Loc.T("Delete page"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        _viewModel.DeletePage(page);
        _list.SelectedItem = _viewModel.CurrentPage;
    }

    private static TextBlock Label(string text) =>
        new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };

    private static Button SmallButton(string text) =>
        new() { Content = text, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 6, 0) };
}
