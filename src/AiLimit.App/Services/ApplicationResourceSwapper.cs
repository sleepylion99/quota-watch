namespace AiLimit.App.Services;

/// <summary>
/// Production <see cref="IResourceDictionarySwapper"/> that replaces the theme colors
/// and reloads the shared brushes in <see cref="System.Windows.Application.Current"/>.
/// </summary>
public sealed class ApplicationResourceSwapper : IResourceDictionarySwapper
{
    private const string ThemeResourcePrefix =
        "pack://application:,,,/QuotaWatch;component/Theming/Themes/";

    public void Swap(ResolvedTheme theme)
    {
        var app = System.Windows.Application.Current;
        if (app is null) return;

        var dicts = app.Resources.MergedDictionaries;
        var source = theme == ResolvedTheme.Light
            ? $"{ThemeResourcePrefix}Colors.Light.xaml"
            : $"{ThemeResourcePrefix}Colors.Dark.xaml";

        var colorIndex = -1;
        var brushIndex = -1;
        for (int i = 0; i < dicts.Count; i++)
        {
            var s = dicts[i].Source?.OriginalString ?? "";
            if (s.Contains("Colors.Dark.xaml") || s.Contains("Colors.Light.xaml"))
            {
                colorIndex = i;
            }
            else if (s.Contains("Brushes.xaml"))
            {
                brushIndex = i;
            }
        }

        var colorDictionary = LoadDictionary(source);
        if (colorIndex >= 0)
        {
            dicts[colorIndex] = colorDictionary;
        }
        else
        {
            colorIndex = 0;
            dicts.Insert(colorIndex, colorDictionary);
            if (brushIndex >= colorIndex)
            {
                brushIndex++;
            }
        }

        // SolidColorBrush resources keep their resolved Color value, so recreate them
        // after the palette changes.
        var brushDictionary = LoadDictionary($"{ThemeResourcePrefix}Brushes.xaml");
        if (brushIndex >= 0)
        {
            dicts[brushIndex] = brushDictionary;
        }
        else
        {
            dicts.Insert(colorIndex + 1, brushDictionary);
        }
    }

    private static System.Windows.ResourceDictionary LoadDictionary(string source)
    {
        return new System.Windows.ResourceDictionary
        {
            Source = new Uri(source, UriKind.Absolute)
        };
    }
}
