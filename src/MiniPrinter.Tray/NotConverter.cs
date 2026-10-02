using System.Globalization;
using System.Windows.Data;

namespace MiniPrinter.Tray;

/// <summary>Negates a boolean binding (e.g. disable a field while a checkbox is checked).</summary>
public sealed class NotConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}
