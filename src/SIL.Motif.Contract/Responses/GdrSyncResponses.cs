using System.Text.Json.Serialization;
using SIL.Motif.Contract.Baselines;

namespace SIL.Motif.Contract.Responses;

/// <summary>The finding codes of the GDR sync check (ADR 0119). Every finding is a warning.</summary>
public static class GdrSyncCodes
{
    /// <summary>The project has rules or affix processes and no Grammar Design Record at all.</summary>
    public const string NoRecords = "gdr.no-records";

    /// <summary>Rules or affix processes of one kind that no Grammar Design Record links.</summary>
    public const string ObjectUncovered = "gdr.object-uncovered";

    /// <summary>A link in a Grammar Design Record names an object that is no longer in the project.</summary>
    public const string CitesMissingObject = "gdr.cites-missing-object";

    /// <summary>A hypothesis names a GDR or a section that the project does not hold.</summary>
    public const string LedgerLinkMissing = "gdr.ledger-link-missing";

    /// <summary>A word an Examples section shows as parsing has no analysis in the latest rows.</summary>
    public const string ExampleNotParsing = "gdr.example-not-parsing";

    /// <summary>A word parses, but no analysis uses the morphemes its example row cites.</summary>
    public const string ExampleAnalysisChanged = "gdr.example-analysis-changed";

    /// <summary>A word a section shows as not parsing now parses.</summary>
    public const string ExampleNowParses = "gdr.example-now-parses";

    /// <summary>No stored Trial or Assessment row settles the word, so the example cannot be checked.</summary>
    public const string ExampleUnverified = "gdr.example-unverified";
}

/// <summary>One thing out of sync between the Grammar Design Records and the grammar.</summary>
/// <param name="Code">A <see cref="GdrSyncCodes"/> value.</param>
/// <param name="Message">What is wrong and what to do, in a sentence.</param>
/// <param name="Gdr">The <c>GDR-n</c> (or title) of the record concerned; null for a rule no record names.</param>
/// <param name="Section">The section heading concerned; null when the finding is about the whole record or none.</param>
/// <param name="Kind">The kind of object concerned, in a person's words; null for an example.</param>
/// <param name="Count">How many objects or examples the finding stands for.</param>
/// <param name="Subjects">The names of the objects, words or hypotheses concerned, at most ten.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GdrSyncFinding(
    string Code, string Message, string? Gdr, string? Section, string? Kind, int Count, IReadOnlyList<string> Subjects);

/// <summary>How many objects of one kind the project holds and how many a Grammar Design Record links.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GdrCoverageKind(string Kind, int Objects, int Covered);

/// <summary>What the check read, so a clean result can be told from an empty one.</summary>
/// <param name="Records">Grammar Design Records read.</param>
/// <param name="Sections">Sections (headings) read across them.</param>
/// <param name="Links">Links to project objects read across them.</param>
/// <param name="Examples">Distinct example words read from Examples and false-parse sections.</param>
/// <param name="ExamplesChecked">Examples settled by a stored Trial or Assessment row.</param>
/// <param name="EvidenceAssessments">The stored Assessments the example rows came from, newest first.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GdrSyncCounts(
    int Records, int Sections, int Links, int Examples, int ExamplesChecked, IReadOnlyList<string> EvidenceAssessments);

/// <summary>Time spent: opening the Baseline copy, then the check itself.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GdrSyncTiming(long LoadMilliseconds, long CheckMilliseconds);

/// <summary>The sync check of the Grammar Design Records against the grammar in the current Baseline.</summary>
/// <param name="HasBaseline">Whether the project has a Baseline to check.</param>
/// <param name="InSync">True when there is no finding; the Finalize warning and the loop's completion test read this.</param>
/// <param name="Coverage">Each kind that needs a section, with the counts.</param>
/// <param name="Findings">The warnings.</param>
/// <param name="Counts">What was read.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GdrCheckResponse(
    bool HasBaseline, bool InSync, IReadOnlyList<GdrCoverageKind> Coverage, IReadOnlyList<GdrSyncFinding> Findings,
    GdrSyncCounts Counts)
{
    /// <summary>The Baseline the check read.</summary>
    public BaselineToken? Baseline { get; init; }

    /// <summary>Whether the live file has been saved since that Baseline.</summary>
    public bool IsStale { get; init; }

    /// <summary>Load and check time; null when the check did not run.</summary>
    public GdrSyncTiming? Timing { get; init; }
}
