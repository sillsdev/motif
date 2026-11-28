using System.Text.Json.Serialization;
using SIL.Motif.Contract.Baselines;

namespace SIL.Motif.Contract.Responses;

/// <summary>The finding codes of the Guide check (ADR 0005). Every finding is a warning.</summary>
public static class GdrSyncCodes
{
    /// <summary>The list is still named "Grammar Design Records", which Motif reads until it is renamed "Grammar Guide".</summary>
    public const string ListOldName = "guide.list-old-name";

    /// <summary>The project has rules or affix processes and no Grammar Guide section at all.</summary>
    public const string NoRecords = "guide.no-records";

    /// <summary>Rules or affix processes of one kind that no Grammar Guide section links.</summary>
    public const string ObjectUncovered = "guide.object-uncovered";

    /// <summary>A link in a Grammar Guide section names an object that is no longer in the project.</summary>
    public const string CitesMissingObject = "guide.cites-missing-object";

    /// <summary>A hypothesis names a Guide section that the project does not hold.</summary>
    public const string LedgerLinkMissing = "guide.ledger-link-missing";

    /// <summary>An interlinear example names a morpheme that no lexical form matches.</summary>
    public const string InterlinearUnknownMorpheme = "guide.interlinear-unknown-morpheme";

    /// <summary>An interlinear example's lines do not pair up, token for token or morpheme for gloss.</summary>
    public const string InterlinearMisaligned = "guide.interlinear-misaligned";

    /// <summary>A word an Examples section shows as parsing has no analysis in the latest rows.</summary>
    public const string ExampleNotParsing = "guide.example-not-parsing";

    /// <summary>A word parses, but no analysis uses the morphemes its example row cites.</summary>
    public const string ExampleAnalysisChanged = "guide.example-analysis-changed";

    /// <summary>A word a section shows as not parsing now parses.</summary>
    public const string ExampleNowParses = "guide.example-now-parses";

    /// <summary>A query block's saved table was made from another Baseline, or its rows differ from a live run of the query.</summary>
    public const string SavedTableOutOfDate = "guide.saved-table-out-of-date";

    /// <summary>No stored Trial or Assessment row settles the word, so the example cannot be checked.</summary>
    public const string ExampleUnverified = "guide.example-unverified";

    /// <summary>A <c>motif-query</c> block has a problem the block parser found, or its run line was refused or failed; the block shows nothing.</summary>
    public const string QueryRefused = "guide.query-refused";

    /// <summary>A <c>motif-query</c> block needs a parse index that is not built; nothing was built, and the finding names the command that builds it.</summary>
    public const string QueryNotBuilt = "guide.query-not-built";

    /// <summary>A <c>motif-query</c> block started and did not finish: it threw, or ran past the per-block timeout.</summary>
    public const string QueryFailed = "guide.query-failed";

    /// <summary>A <c>motif-query</c> block's run line is not in NFC, the form Query matches in; Query composes it, but the block should be saved in the corpus's form.</summary>
    public const string QueryNotNormalized = "guide.query-not-normalized";

    /// <summary>A <c>motif-query</c> block's row count is outside the count its <c>expect</c> key declares.</summary>
    public const string QueryCountMismatch = "guide.query-count-mismatch";

    /// <summary>A <c>motif-query</c> or <c>motif-show</c> block is malformed (a bad line, key, id or value).</summary>
    public const string QueryBlockInvalid = "guide.query-block-invalid";
}

/// <summary>The status words of <see cref="GdrQueryBlockRun.Status"/>.</summary>
public static class GdrQueryBlockStatus
{
    /// <summary>The query ran and answered.</summary>
    public const string Ok = "ok";
    /// <summary>The query was refused, with a code, or the block's run line is not a read-only query line.</summary>
    public const string Refused = "refused";
    /// <summary>The query needs a parse index that is not built.</summary>
    public const string NotBuilt = "not-built";
    /// <summary>The query threw or timed out.</summary>
    public const string Failed = "failed";
    /// <summary>The check was asked not to run blocks.</summary>
    public const string Skipped = "skipped";
}

/// <summary>What the Guide check found for one <c>motif-query</c> block.</summary>
/// <param name="Id">The block's id.</param>
/// <param name="Gdr">The <c>GDR-n</c> of the record holding it.</param>
/// <param name="Section">The heading it sits under; null before the first heading.</param>
/// <param name="Status">A <see cref="GdrQueryBlockStatus"/> value.</param>
/// <param name="Code">The refusal code, for a refused or not-built block.</param>
/// <param name="Message">The reason or the headline, in a sentence; for a not-built block, the command that builds the index.</param>
/// <param name="Rows">The word types (or groups) the query answered; null when it gave no answer.</param>
/// <param name="Complete">False when <paramref name="Rows"/> is a lower bound ("at least"); null when there is no count.</param>
/// <param name="Milliseconds">How long the run took; 0 when it did not run.</param>
/// <param name="BuildCommand">For a not-built block, the exact shell line that builds the parse index.</param>
/// <param name="Words">For a group-by block, the word types the groups cover (<paramref name="Rows"/> then counts the groups); null otherwise.</param>
/// <param name="Tokens">The tokens of the matching words, when the run totalled them.</param>
/// <param name="IndexBuilt">True when this check built the parse index the block needed.</param>
/// <param name="Expected">The block's <c>expect</c> text, when it declares one; null otherwise.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GdrQueryBlockRun(
    string Id, string? Gdr, string? Section, string Status, string? Code, string Message, long? Rows, bool? Complete, double Milliseconds,
    string? BuildCommand = null, long? Words = null, long? Tokens = null, bool IndexBuilt = false, string? Expected = null);

/// <summary>One thing out of sync between the Grammar Guide and the grammar.</summary>
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

/// <summary>How many objects of one kind the project holds and how many a Grammar Guide section links.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GdrCoverageKind(string Kind, int Objects, int Covered);

/// <summary>What the check read, so a clean result can be told from an empty one.</summary>
/// <param name="Records">Grammar Guide read.</param>
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

/// <summary>The sync check of the Grammar Guide against the grammar in the current Baseline.</summary>
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

    /// <summary>One entry per <c>motif-query</c> block, in Guide order; null when the response did not look at blocks (the Trial advisory).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<GdrQueryBlockRun>? QueryBlocks { get; init; }
}
