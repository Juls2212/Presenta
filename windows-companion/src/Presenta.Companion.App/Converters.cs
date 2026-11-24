using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Presenta.Companion.ViewModels;
using Presenta.Server;

namespace Presenta.Companion.App;

/// <summary>Finds the stroke icon of a page by its section (<c>Icon_Home</c>, <c>Icon_Pair</c> and so on in Controls.xaml).</summary>
public sealed class SectionIconConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Section section ? Application.Current?.TryFindResource("Icon_" + section) : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// Maps every kind of status of the window to one of four marks: <c>Ok</c>, <c>Warn</c>, <c>Off</c> and <c>Bad</c>. The marks differ in
/// shape as well as color, so a status never depends on color alone.
/// </summary>
public sealed class ToneConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        NavStatus.Ok or CheckState.Ok or ActivityTone.Good or PaceTone.Good or TargetSeverity.Good => "Ok",
        NavStatus.Warning or CheckState.Warning or ActivityTone.Warning or PaceTone.Warning or TargetSeverity.Warning => "Warn",
        true => "Ok",
        ActivityTone.Bad => "Bad",
        AiStatus.Ready or AiStatus.Working => "Ok",
        AiStatus.Paused or AiStatus.InvalidKey or AiStatus.NoText or AiStatus.InvalidAddress or AiStatus.InsecureAddress => "Warn",
        _ => "Off",
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>A share (0 to 1) of a width: <c>values[0]</c> is the fraction, <c>values[1]</c> the width.</summary>
public sealed class FractionOfWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double fraction || values[1] is not double width || double.IsNaN(width))
        {
            return 0.0;
        }

        return Math.Max(0, fraction * width);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// Places a marker label on the timeline: <c>values[0]</c> is the position (0 to 1), <c>values[1]</c> the width of the bar,
/// <c>values[2]</c> the row (0, 1 or 2). The label is 140 wide, centered on the position and kept inside the bar.
/// </summary>
public sealed class MarkerMarginConverter : IMultiValueConverter
{
    public const double LabelWidth = 140;
    public const double RowHeight = 46;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 3 || values[0] is not double position || values[1] is not double width || values[2] is not int row || double.IsNaN(width))
        {
            return new Thickness(0);
        }

        var left = Math.Min(Math.Max(0, position * width - LabelWidth / 2), Math.Max(0, width - LabelWidth));
        return new Thickness(left, row * RowHeight, 0, 0);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// The arc of the progress ring for a percentage. The parameter is <c>center;radius</c> (for example <c>88;81</c>); the arc starts at
/// the top and runs clockwise.
/// </summary>
public sealed class PercentToArcConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var percent = value is double d ? d : 0;
        var parts = ((parameter as string) ?? "88;81").Split(';');
        var center = double.Parse(parts[0], CultureInfo.InvariantCulture);
        var radius = double.Parse(parts[1], CultureInfo.InvariantCulture);
        if (percent <= 0.05)
        {
            return Geometry.Empty;
        }

        var angle = Math.Min(percent, 99.99) / 100.0 * 2 * Math.PI;
        var end = new Point(center + radius * Math.Sin(angle), center - radius * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = new Point(center, center - radius), IsClosed = false };
        figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, angle > Math.PI, SweepDirection.Clockwise, true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Half of a number, negated. Kept for centering with a transform.</summary>
public sealed class NegativeHalfConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double d ? -d / 2 : 0.0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>True hides, false shows.</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
