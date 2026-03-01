using System.Globalization;

namespace IndoorNav.Converters;

/// <summary>
/// Конвертирует bool в один из двух цветов.
/// ConverterParameter="TrueColor|FalseColor"
/// </summary>
public class BoolToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var parts = (parameter as string ?? "Black|Gray").Split('|');
        string hex = (value is true) ? parts[0] : (parts.Length > 1 ? parts[1] : "Gray");
        try
        {
            if (Color.TryParse(hex, out var c)) return c;
        }
        catch { /* ignore */ }
        return Colors.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
