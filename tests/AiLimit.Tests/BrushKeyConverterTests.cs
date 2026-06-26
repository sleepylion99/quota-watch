using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using AiLimit.App.Theming;

namespace AiLimit.Tests;

public sealed class BrushKeyConverterTests
{
    [Fact]
    public void Convert_KnownKey_ReturnsResolvedBrush()
    {
        var expected = Brushes.Red;
        var converter = new BrushKeyConverter(key => key == "Brush.Test.Known" ? expected : null);

        var result = converter.Convert("Brush.Test.Known", typeof(Brush), null, CultureInfo.InvariantCulture);

        Assert.Same(expected, result);
    }

    [Fact]
    public void Convert_UnknownKey_ReturnsTransparent()
    {
        var converter = new BrushKeyConverter(_ => null);

        var result = converter.Convert("Brush.DoesNotExist", typeof(Brush), null, CultureInfo.InvariantCulture);

        Assert.Equal(Brushes.Transparent, result);
    }

    [Fact]
    public void Convert_NullInput_ReturnsTransparent()
    {
        var converter = new BrushKeyConverter(_ => null);

        var result = converter.Convert(null, typeof(Brush), null, CultureInfo.InvariantCulture);

        Assert.Equal(Brushes.Transparent, result);
    }

    [Fact]
    public void Convert_EmptyStringInput_ReturnsTransparent()
    {
        var converter = new BrushKeyConverter(_ => null);

        var result = converter.Convert(string.Empty, typeof(Brush), null, CultureInfo.InvariantCulture);

        Assert.Equal(Brushes.Transparent, result);
    }

    [Fact]
    public void Convert_DefaultResolverUnresolvableKey_ReturnsTransparent()
    {
        // The default resolver calls Application.Current?.TryFindResource. Use a key that no resource
        // dictionary defines so the result is deterministic regardless of whether another test in the
        // suite has created a global Application.Current — WPF cannot reset it, so this test must not
        // assume it is null. The converter must fall back to Transparent without throwing.
        var converter = new BrushKeyConverter();

        var result = converter.Convert(
            "Brush.UnitTest.Unresolvable.DoesNotExist", typeof(Brush), null, CultureInfo.InvariantCulture);

        Assert.Equal(Brushes.Transparent, result);
    }

    [Fact]
    public void ConvertBack_ReturnsDonthing()
    {
        var converter = new BrushKeyConverter();

        var result = converter.ConvertBack(Brushes.Red, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal(Binding.DoNothing, result);
    }
}
