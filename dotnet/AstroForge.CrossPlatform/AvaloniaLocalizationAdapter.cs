using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using AstroForge.App.Services;

namespace AstroForge.CrossPlatform;

internal static class AvaloniaLocalizationAdapter
{
    public static void Apply(Visual root, string language)
    {
        foreach (var value in new[] { root }.Concat(root.GetVisualDescendants())) Translate(value, language);
    }

    private static void Translate(Visual value, string language)
    {
        if (value is TextBlock text && text.Text is { } textValue)
        {
            var translated = UiLocalization.Translate(textValue, language);
            if (!string.Equals(textValue, translated, StringComparison.Ordinal)) text.SetCurrentValue(TextBlock.TextProperty, translated);
        }
        if (value is ContentControl content && content.Content is string contentText)
            content.SetCurrentValue(ContentControl.ContentProperty, UiLocalization.Translate(contentText, language));
        if (value is HeaderedContentControl headered && headered.Header is string header)
            headered.SetCurrentValue(HeaderedContentControl.HeaderProperty, UiLocalization.Translate(header, language));
        if (value is Control control && ToolTip.GetTip(control) is string tip)
            ToolTip.SetTip(control, UiLocalization.Translate(tip, language));
        if (value is DataGrid grid)
            foreach (var column in grid.Columns)
                if (column.Header is string columnHeader) column.Header = UiLocalization.Translate(columnHeader, language);
    }

}
