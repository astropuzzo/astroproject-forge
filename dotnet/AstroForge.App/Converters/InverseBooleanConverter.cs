using System.Globalization;
using System.Windows.Data;

namespace AstroForge.App.Converters;

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is bool boolean && !boolean;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is bool boolean && !boolean;
}

