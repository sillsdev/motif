using SIL.Motif.Host.PanGloss;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

internal sealed record PanGlossWarningText(
    string Title,
    string Description,
    string Explanation,
    string Guidance,
    string FieldWorksPlaces,
    string HelpBody,
    Uri? HelpUrl)
{
    public static PanGlossWarningText From(GrammarWarning warning)
    {
        var places = warning.FieldWorksPlaces
            .Select(place => $"{FieldWorksLinks.ToolName(place.Tool)} > {place.Field}")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var helpUrl = warning.HelpUrl is { } address ? new Uri(address) : null;
        return new(warning.Title ?? warning.Group ?? warning.CodeLabel, warning.Description,
            warning.Explanation ?? string.Empty, warning.Guidance ?? string.Empty,
            string.Join("; ", places), warning.HelpBody ?? string.Empty, helpUrl);
    }
}
