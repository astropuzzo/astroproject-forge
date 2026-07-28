using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace AstroForge.App.Services;

public static class WpfLocalizationAdapter
{
    public static void Apply(DependencyObject root, string language)
    {
        var visited = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        Visit(root, language, visited);
    }

    private static void Visit(DependencyObject value, string language, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(value)) return;
        Translate(value, language);

        if (value is Popup { Child: { } popupChild }) Visit(popupChild, language, visited);
        var visualChildren = value is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetChildrenCount(value) : 0;
        for (var index = 0; index < visualChildren; index++) Visit(VisualTreeHelper.GetChild(value, index), language, visited);
        foreach (var childValue in LogicalTreeHelper.GetChildren(value))
            if (childValue is DependencyObject logicalChild) Visit(logicalChild, language, visited);
    }

    private static void Translate(DependencyObject value, string language)
    {
        if (value is TextBlock text) Set(text, TextBlock.TextProperty, text.Text, language);
        if (value is ContentControl content && content.Content is string contentText)
            Set(content, ContentControl.ContentProperty, contentText, language);
        if (value is HeaderedContentControl headered && headered.Header is string header)
            Set(headered, HeaderedContentControl.HeaderProperty, header, language);
        if (value is HeaderedItemsControl headeredItems && headeredItems.Header is string itemsHeader)
            Set(headeredItems, HeaderedItemsControl.HeaderProperty, itemsHeader, language);
        if (value is FrameworkElement element && ToolTipService.GetToolTip(element) is string tip)
        {
            element.SetCurrentValue(ToolTipService.ToolTipProperty, UiLocalization.Translate(tip, language));
            if (string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(element)))
                AutomationProperties.SetHelpText(element, UiLocalization.Translate(tip, language));
        }
        if (value is DataGrid grid)
            foreach (var column in grid.Columns)
                if (column.Header is string columnHeader) column.SetCurrentValue(DataGridColumn.HeaderProperty, UiLocalization.Translate(columnHeader, language));
        if (value is FrameworkElement accessible) ApplyAccessibleName(accessible, language);
    }

    private static void ApplyAccessibleName(FrameworkElement element, string language)
    {
        var current = AutomationProperties.GetName(element);
        if (!string.IsNullOrWhiteSpace(current))
        {
            AutomationProperties.SetName(element, UiLocalization.Translate(current, language));
            return;
        }

        var candidate = element switch
        {
            TextBox textBox when !string.IsNullOrWhiteSpace(textBox.Tag?.ToString()) => textBox.Tag!.ToString(),
            ContentControl content when content.Content is string contentText && IsMeaningful(contentText) => contentText,
            HeaderedContentControl headered when headered.Header is string header && IsMeaningful(header) => header,
            _ when ToolTipService.GetToolTip(element) is string tip && IsMeaningful(tip) => tip,
            _ => null
        };
        if (!string.IsNullOrWhiteSpace(candidate))
            AutomationProperties.SetName(element, UiLocalization.Translate(candidate, language));
    }

    private static bool IsMeaningful(string value) =>
        value.Count(char.IsLetterOrDigit) >= 2;

    private static void Set(DependencyObject target, DependencyProperty property, string value, string language)
    {
        var translated = UiLocalization.Translate(value, language);
        if (!string.Equals(value, translated, StringComparison.Ordinal)) target.SetCurrentValue(property, translated);
    }
}
