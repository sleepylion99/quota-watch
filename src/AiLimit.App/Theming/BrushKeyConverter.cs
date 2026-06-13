using System.Globalization;
using System.Windows.Data;

namespace AiLimit.App.Theming;

public sealed class BrushKeyConverter : System.Windows.Data.IValueConverter
{
    private readonly Func<string, object?> _resolver;

    public BrushKeyConverter() : this(null) { }

    public BrushKeyConverter(Func<string, object?>? resourceResolver)
    {
        _resolver = resourceResolver ?? DefaultResolver;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || string.IsNullOrEmpty(key)) return System.Windows.Media.Brushes.Transparent;
        return _resolver(key) as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => System.Windows.Data.Binding.DoNothing;

    private static object? DefaultResolver(string key)
        => System.Windows.Application.Current?.TryFindResource(key);
}
