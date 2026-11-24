using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Host.Store;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Query.Phon;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Commands.Queries;

/// <summary>One natural class or phonological feature a <c>phon</c> pattern can name.</summary>
/// <param name="Name">The grammar's one label for the class or feature (a FieldWorks project's Abbreviation), or the class's id when it has none.</param>
/// <param name="Abbreviation">Unused by PanGloss's inventory, which keeps one label; null.</param>
/// <param name="Members">Not listed by PanGloss's inventory; null.</param>
/// <param name="Token">For a natural class, exactly what to type in a pattern, in brackets when it has spaces or pattern characters; null for a feature, and for a class no spelling reaches.</param>
/// <param name="Kind">For a natural class, <c>segment</c> (an explicit segment list) or <c>feature</c> (a feature structure); null for a feature.</param>
/// <param name="Values">For a feature, the names of its values in order; null for a class.</param>
public sealed record PhonValue(string Name, string? Abbreviation, int? Members = null, string? Token = null, string? Kind = null,
    IReadOnlyList<string>? Values = null);

/// <summary>The names a pattern may use: <c>motif query --values classes</c> and the MCP <c>values</c> argument of <c>motif_query_schema</c>.</summary>
/// <param name="Schema">The schema name.</param>
/// <param name="Source">Where the names were read: the project's Baseline or the grammar file.</param>
/// <param name="NaturalClasses">The natural classes, as written in the grammar's phonology.</param>
/// <param name="Features">The phonological features, for <c>[+f -g]</c> bundles (served by PanGloss once it lifts its gate).</param>
/// <param name="Note">What this list is and is not: PanGloss itself lists neither, so the names come from the grammar file.</param>
public sealed record PhonValuesResponse(string Schema, string Source, IReadOnlyList<PhonValue> NaturalClasses,
    IReadOnlyList<PhonValue> Features, string Note)
{
    /// <summary>The schema name of the JSON form.</summary>
    public const string SchemaName = "motif.query.values.v1";
}

/// <summary>Asks for the pattern names of a project's Baseline or of a grammar file; exactly one is given.</summary>
/// <param name="FwDataPath">The project whose current Baseline is read.</param>
/// <param name="GrammarPath">A grammar file read directly.</param>
public sealed record QueryValuesRequest(string? FwDataPath = null, string? GrammarPath = null);

/// <summary>Lists the natural-class and feature names of a grammar, for writing <c>phon</c> and <c>env</c> patterns.</summary>
public static class PhonValues
{
    private const string Explanation =
        "Read from PanGloss's inventory op for the grammar. PanGloss keeps one label per class, so a class is named by that label (a FieldWorks project's Abbreviation), " +
        "or by its id when the label cannot be written in brackets; a name it does not know is refused as unknown_natural_class with the nearest names.";

    /// <summary>Reads the names the request asks for.</summary>
    public static CommandOutcome<PhonValuesResponse> Read(QueryValuesRequest request, Action<string>? timing = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if ((request.FwDataPath is null) == (request.GrammarPath is null))
            return CommandOutcome<PhonValuesResponse>.Refused(new Refusal(RefusalCodes.QueryCorpusInvalid, FailureReason.InvalidArgument,
                "Name the grammar with --project or --grammar, one of the two."));
        return request.FwDataPath is not null ? ForProject(request.FwDataPath, timing) : ForGrammar(request.GrammarPath!, "grammar", timing);
    }

    /// <summary>The names of the project's current Baseline.</summary>
    public static CommandOutcome<PhonValuesResponse> ForProject(string fwDataPath, Action<string>? timing = null) =>
        ProjectStoreCommand.ReadExisting(fwDataPath,
            CommandOutcome<PhonValuesResponse>.Refused(new Refusal("baseline.missing", FailureReason.NotFound,
                "Motif has no saved copy of this project to read yet.")),
            (database, project) =>
            {
                var baseline = new SIL.Motif.Worker.Baselines.BaselineRepository(database).GetCurrentPublished(ProjectWorkspaceKey.Compute(project));
                return baseline is null
                    ? CommandOutcome<PhonValuesResponse>.Refused(new Refusal("baseline.missing", FailureReason.NotFound,
                        "The project has no Baseline yet. Run 'motif baseline capture --project <fwdata>'."))
                    : ForGrammar(baseline.FwDataPath, "baseline", timing);
            });

    /// <summary>The names in a grammar file, such as the one <c>--grammar</c> names, listed by PanGloss's <c>inventory</c> op.</summary>
    public static CommandOutcome<PhonValuesResponse> ForGrammar(string grammarPath, string source = "grammar", Action<string>? timing = null,
        string? parserPath = null)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var matcher = PhonMatchers.ForGrammar(grammarPath, parserPath);
        if (!matcher.Succeeded) return CommandOutcome<PhonValuesResponse>.Refused(matcher.Refusal!);
        PhonInventory? inventory;
        try
        {
            inventory = matcher.Value!.Inventory(CancellationToken.None);
        }
        catch (QueryRefusalException refused)
        {
            return CommandOutcome<PhonValuesResponse>.Refused(refused.Refusal);
        }
        timing?.Invoke($"timing: start PanGloss and read the inventory {stopwatch.ElapsedMilliseconds} ms");
        if (inventory is null)
            return CommandOutcome<PhonValuesResponse>.Refused(new Refusal(RefusalCodes.QueryPhonUnavailable, FailureReason.Refused,
                "The installed PanGloss could not list the grammar's natural classes."));
        var classes = inventory.Classes.Select(item => new PhonValue(item.Name ?? item.Id, null, null,
            item.Token is { } token ? PatternToken(token) : null, item.Kind)).ToList();
        var features = inventory.Features.Select(item => new PhonValue(item.Name, null, Values: item.Values)).ToList();
        var note = inventory.Complete ? Explanation : Explanation + $" PanGloss stopped at {inventory.StopReason}, so this list is partial.";
        return CommandOutcome<PhonValuesResponse>.Success(new PhonValuesResponse(PhonValuesResponse.SchemaName, source,
            [.. classes.OrderBy(value => value.Name, StringComparer.Ordinal)], [.. features.OrderBy(value => value.Name, StringComparer.Ordinal)], note));
    }

    /// <summary>The text of a class in a pattern: bare when the pattern parser reads it as one word, else in brackets.</summary>
    internal static string PatternToken(string text) =>
        text.Length > 0 && !text.Any(c => char.IsWhiteSpace(c) || c is '(' or ')' or '[' or ']' or '#' or '+' or '/' or '*') &&
        text != "_"
            ? text
            : "[" + text + "]";
}
