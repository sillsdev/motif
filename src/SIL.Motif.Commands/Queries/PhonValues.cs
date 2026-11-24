using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Core.WritingSystems;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Commands.Queries;

/// <summary>One natural class or phonological feature a <c>phon</c> pattern can name.</summary>
/// <param name="Name">The LibLCM name.</param>
/// <param name="Abbreviation">The LibLCM abbreviation; PanGloss may know the class by either, so try both.</param>
/// <param name="Members">For a segment class, how many phonemes it holds; null for a feature class and a feature.</param>
public sealed record PhonValue(string Name, string? Abbreviation, int? Members = null);

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
        "Read from the grammar's phonology (LibLCM). PanGloss's match op does not list classes; a name it does not know is refused as unknown_natural_class with the nearest names.";

    /// <summary>Reads the names the request asks for.</summary>
    public static CommandOutcome<PhonValuesResponse> Read(QueryValuesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if ((request.FwDataPath is null) == (request.GrammarPath is null))
            return CommandOutcome<PhonValuesResponse>.Refused(new Refusal(RefusalCodes.QueryCorpusInvalid, FailureReason.InvalidArgument,
                "Name the grammar with --project or --grammar, one of the two."));
        return request.FwDataPath is not null ? ForProject(request.FwDataPath) : ForGrammar(request.GrammarPath!);
    }

    /// <summary>The names of the project's current Baseline.</summary>
    public static CommandOutcome<PhonValuesResponse> ForProject(string fwDataPath) =>
        ProjectStoreCommand.ReadExisting(fwDataPath,
            CommandOutcome<PhonValuesResponse>.Refused(new Refusal("baseline.missing", FailureReason.NotFound,
                "Motif has no saved copy of this project to read yet.")),
            (database, project) =>
            {
                var baseline = new SIL.Motif.Worker.Baselines.BaselineRepository(database).GetCurrentPublished(ProjectWorkspaceKey.Compute(project));
                return baseline is null
                    ? CommandOutcome<PhonValuesResponse>.Refused(new Refusal("baseline.missing", FailureReason.NotFound,
                        "The project has no Baseline yet. Run 'motif baseline capture --project <fwdata>'."))
                    : ForGrammar(baseline.FwDataPath, "baseline");
            });

    /// <summary>The names in a grammar file, such as the one <c>--grammar</c> names.</summary>
    public static CommandOutcome<PhonValuesResponse> ForGrammar(string grammarPath, string source = "grammar")
    {
        if (!File.Exists(grammarPath))
            return CommandOutcome<PhonValuesResponse>.Refused(new Refusal(RefusalCodes.QueryCorpusUnreadable, FailureReason.InvalidArgument,
                $"The grammar '{grammarPath}' is not a file."));
        using var reader = BaselineReadCache.Open(grammarPath);
        var cache = reader.Cache;
        var classes = cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Select(natural =>
            new PhonValue(Text(natural.Name), Text(natural.Abbreviation) is { Length: > 0 } abbreviation ? abbreviation : null,
                natural is IPhNCSegments segments ? segments.SegmentsRC.Count : null)).OrderBy(value => value.Name, StringComparer.Ordinal).ToList();
        var features = cache.LangProject.PhFeatureSystemOA.FeaturesOC.Select(feature =>
            new PhonValue(Text(feature.Name), Text(feature.Abbreviation) is { Length: > 0 } abbreviation ? abbreviation : null))
            .OrderBy(value => value.Name, StringComparer.Ordinal).ToList();
        return CommandOutcome<PhonValuesResponse>.Success(new PhonValuesResponse(PhonValuesResponse.SchemaName, source, classes, features, Explanation));
    }

    private static string Text(IMultiAccessorBase text) => text.BestAnalysisAlternative.Text ?? "";
}
