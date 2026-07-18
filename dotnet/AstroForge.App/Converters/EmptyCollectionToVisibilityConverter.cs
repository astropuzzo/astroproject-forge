using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AstroForge.App.Converters;

public sealed class EmptyCollectionToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        int count => count == 0 ? Visibility.Visible : Visibility.Collapsed,
        ICollection collection => collection.Count == 0 ? Visibility.Visible : Visibility.Collapsed,
        _ => Visibility.Collapsed
    };

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
