using System.Globalization;
using System.Windows.Data;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The widest a rail row may be: the rail's width less the indent of its nesting and a margin. A row bound to it trims a long title
/// with an ellipsis, instead of running past the rail's edge where its ⏩ would be out of sight.
/// </summary>
internal sealed class RailRowWidth : IValueConverter
{
    /// <summary>
    /// What a row must leave free: the indent of its level (about 60 device-independent pixels for a history row), the scroll bar and a
    /// margin. Measured on the screen: 64 let the ⏩ clip by a few pixels.
    /// </summary>
    private const double Reserved = 80;

    public static readonly RailRowWidth Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double width ? Math.Max(0, width - Reserved) : double.PositiveInfinity;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
