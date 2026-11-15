using System.Globalization;
using Avalonia.Data.Converters;

namespace SIL.Motif.App.ViewModels;

/// <summary>Divides recorded rule work by the exact measured-word denominator.</summary>
public sealed class DiagnosticStepsConverter : IMultiValueConverter
{
    public static DiagnosticStepsConverter Instance { get; } = new();

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Count == 2 && values[0] is long work && values[1] is int count && count > 0
            ? ((decimal)work / count).ToString("N2", culture) : MotifAppStrings.TimingNotRecorded;
}
