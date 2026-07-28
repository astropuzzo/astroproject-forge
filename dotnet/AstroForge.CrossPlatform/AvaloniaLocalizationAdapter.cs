using Avalonia;
using Avalonia.Automation;
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
        {
            var translatedTip = UiLocalization.Translate(tip, language);
            ToolTip.SetTip(control, translatedTip);
            if (string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(control)))
                AutomationProperties.SetHelpText(control, translatedTip);
        }
        if (value is Control accessibleControl) ApplyAccessibleName(accessibleControl, language);
        if (value is DataGrid grid)
            foreach (var column in grid.Columns)
                if (column.Header is string columnHeader) column.Header = UiLocalization.Translate(columnHeader, language);
    }

    private static void ApplyAccessibleName(Control control, string language)
    {
        var existing = AutomationProperties.GetName(control);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            AutomationProperties.SetName(control, UiLocalization.Translate(existing, language));
            return;
        }

        var candidate = control switch
        {
            TextBox textBox when !string.IsNullOrWhiteSpace(textBox.PlaceholderText) => textBox.PlaceholderText,
            ContentControl content when content.Content is string text => text,
            HeaderedContentControl headered when headered.Header is string header => header,
            _ when ToolTip.GetTip(control) is string tip => tip,
            _ => null
        };
        if (IsMeaningful(candidate))
            AutomationProperties.SetName(control, UiLocalization.Translate(candidate!, language));
    }

    private static bool IsMeaningful(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Count(char.IsLetterOrDigit) >= 2;

}
