using System.Globalization;
using Avalonia.Data.Converters;

namespace SIL.Motif.App.Views;

/// <summary>Shows parser rule kinds as readable labels without changing the stored timing response.</summary>
public sealed class TimingKindLabelConverter : IValueConverter
{
    public static string Display(string kind) => kind switch
    {
        "morph_rule" => "Morphological rules",
        "phon_rule" => "Phonological rules",
        "root_index" => "Root lookup",
        "lex_entry" => "Lexical entries",
        _ => kind.Replace('_', ' '),
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string kind ? Display(kind) : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
