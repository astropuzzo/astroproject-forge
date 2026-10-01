using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Threading;

namespace AstroForge.CrossPlatform.Controls;

/// <summary>
/// "Browse the catalogue": the whole list of cameras or telescopes in a searchable, scrolling menu. The box in the profile
/// only offers what matches the text already in it, so for the person who wants to look around it has to be something else.
/// </summary>
public static class CatalogBrowser
{
    public sealed record Handle(Flyout Flyout, ListBox List, TextBox Search, TextBlock Count);

    public static Handle Show<T>(Control anchor, IReadOnlyList<T> all, Func<T, string> name, Func<T, string> detail, T? current, Action<T> pick,
        string placeholder, Func<int, int, string> count) where T : class
    {
        var search = new TextBox { PlaceholderText = placeholder };
        Avalonia.Automation.AutomationProperties.SetName(search, placeholder);
        var counter = new TextBlock { Classes = { "muted" }, FontSize = 11 };
        var list = new ListBox
        {
            Height = 330,
            ItemsSource = all,
            ItemTemplate = new FuncDataTemplate<T>((item, _) =>
            {
                // the list asks for a row with nothing in it while its content is being swapped
                if (item is null) return new Panel();
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                row.Children.Add(new TextBlock { Text = name(item), TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
                var info = new TextBlock { Text = detail(item), Classes = { "mono", "muted" }, FontSize = 10.5, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(info, 1);
                row.Children.Add(info);
                return row;
            })
        };
        counter.Text = count(all.Count, all.Count);
        var flyout = new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            Content = new StackPanel { Width = 470, Spacing = 8, Children = { search, counter, list } }
        };

        // Every word typed must be in the name, in any order: "skywatcher 130" finds the Skywatcher 130PDS.
        search.TextChanged += (_, _) =>
        {
            var words = (search.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var shown = words.Length == 0 ? all : all.Where(item => words.All(word => name(item).Contains(word, StringComparison.OrdinalIgnoreCase))).ToList();
            list.ItemsSource = shown;
            counter.Text = count(shown.Count, all.Count);
        };
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not T item) return;
            pick(item);
            flyout.Hide();
        };
        flyout.Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            search.Focus();
            if (current is not null) list.ScrollIntoView(current);
        }, DispatcherPriority.Background);

        flyout.ShowAt(anchor);
        return new Handle(flyout, list, search, counter);
    }
}
