using Microsoft.Data.Sqlite;

namespace SIL.Motif.Host.Store;

/// <summary>
/// One declared change to the project store, keyed on the <c>user_version</c> it produced. The upgrade copies authored
/// data into a new store; a step says which tables it added as authored or derived, and what a store older than
/// <see cref="To"/> needs done while it is carried.
/// </summary>
/// <param name="To">The schema version this step produced.</param>
/// <param name="Summary">What the step is, in one line, for the receipt and for a reader of the table.</param>
/// <param name="Authored">Tables this step added that hold authored work; carried, and never dropped.</param>
/// <param name="Derived">Tables this step added that hold recomputable results; dropped on upgrade (ADR 0023).</param>
/// <param name="ReadableFrom">Authored tables whose older rows cannot be read now; a store before <see cref="To"/> drops them.</param>
/// <param name="RewriteColumns">Adds or changes the columns copied for a table when the store is older than <see cref="To"/>.</param>
/// <param name="AfterCopy">Runs once after the copy when the store is older than <see cref="To"/>.</param>
internal sealed record StoreUpgradeStep(int To, string Summary, string[] Authored, string[] Derived,
    string[]? ReadableFrom = null,
    StoreUpgradeStep.ColumnRewrite? RewriteColumns = null,
    Action<StoreUpgradeStep.Context>? AfterCopy = null)
{
    /// <summary>Adjusts the names and values copied for one table.</summary>
    internal delegate void ColumnRewrite(string table, HashSet<string> oldColumns, HashSet<string> newColumns,
        List<string> names, List<string> values);

    /// <summary>The open build transaction a step works in.</summary>
    internal sealed record Context(SqliteConnection Destination, SqliteTransaction Transaction,
        HashSet<string> OldTables, Dictionary<string, long> Carried);
}

/// <summary>What one upgrade does, worked out from the steps and the schema the store was at.</summary>
internal sealed class StoreUpgradePlan(int sourceSchema, IReadOnlyList<StoreUpgradeStep> crossed,
    string[] kept, HashSet<string> derived, bool carriesSummaries)
{
    /// <summary>The schema the store was at.</summary>
    public int SourceSchema { get; } = sourceSchema;
    /// <summary>The steps newer than the store, oldest first.</summary>
    public IReadOnlyList<StoreUpgradeStep> Crossed { get; } = crossed;
    /// <summary>The tables carried.</summary>
    public string[] Kept { get; } = kept;
    /// <summary>Whether Difference summaries are in a shape current code reads.</summary>
    public bool CarriesSummaries { get; } = carriesSummaries;

    /// <summary>Whether an old table is dropped.</summary>
    public bool IsDerived(string table) => !Kept.Contains(table, StringComparer.Ordinal) && derived.Contains(table);
}

/// <summary>
/// The ordered table of store upgrade steps. Every change to <see cref="MotifSchema.CurrentSchema"/> adds a step here,
/// and every table in the current schema is named by exactly one step as authored or derived; tests fail when either is missed.
/// </summary>
internal static class StoreUpgradeSteps
{
    /// <summary>The oldest schema the steps know; tables listed here predate step tracking.</summary>
    internal const int BaseSchema = 1;

    internal static readonly IReadOnlyList<StoreUpgradeStep> All =
    [
        new(BaseSchema, "Tables present before steps were recorded.",
            Authored: ["Corpora", "CorpusDocuments", "Proposals", "ProposalRevisions",
                "ProposalRationaleRevisions", "ProposalPictures",
                "Receipts", "AppliedIndex", "NamedSelections", "DefaultSelection", "ParsimonySelection", "ReviewMarks", "ReadOccurrences",
                "PhonologyExampleSets", "PhonologyExampleSetRevisions", "PhonologyExamples", "ReviewReports", "Requests",
                "Hypotheses", "HypothesisProposals", "HypothesisTests", "StoreRelocations"],
            Derived: ["ActiveParse", "AssessedWords", "AssessmentInvocations", "AssessmentObjectTimings", "AssessmentPins",
                "AssessmentWordPayloads", "AssessmentWordSetBindings", "Assessments", "BeforeAssessmentBinding", "BaselineSummaries",
                "BaselineTextReadIndex", "BaselineTextWordforms", "BaselineTextWords", "Baselines", "CurrentParsimonyBundles",
                "GrammarChecks", "Jobs", "ParsedAnalyses", "ParserRefusals", "ParsimonyBuildClaims", "ParsimonyBundleReferences",
                "ParsimonyBundles", "ParsimonyFrozenViews", "ParsimonyReaderLeases",
                "SourceIndexState", "SourcePassages", "SourceWordforms", "SourceWordPlaces",
                "IndexedCorpusHeads", "IndexedCorpusGenerations", "IndexedCorpusDocuments", "IndexedCorpusShards",
                "IndexedCorpusForms", "IndexedCorpusOccurrences", "IndexedCorpusWeights", "IndexedCorpusShardWeights",
                "PendingChangeFits", "ProgressState", "ProgressTexts",
                "PhonologyAssessmentBindings", "PhonologyAssessmentDependencies", "PhonologyAssessmentRows",
                "ProposalDifferenceBuckets", "ProposalDifferenceCases", "ProposalDifferenceOccurrences",
                "Reports", "RetainedInvocationMembers", "RetainedInvocations",
                "query_snapshot", "query_forms", "query_stems", "query_uses", "query_objects",
                "ReviewReportCases", "ReviewReportMemberships", "ReviewReportEvents"]),
        new(56, "Difference summaries take their standalone shape (ADR 0021); older summaries are dropped.",
            Authored: ["ProposalDifferences"], Derived: [], ReadableFrom: ["ProposalDifferences"]),
        new(60, "A Named Selection's per-word limit counts parser work, not analysis attempts (x100).",
            Authored: [], Derived: [], RewriteColumns: RescalePerWordLimit),
        new(62, "A Proposal's Note becomes a Comment by a person with no recorded name; blank Notes are dropped.",
            Authored: ["ProposalComments"], Derived: [], AfterCopy: FoldNotesIntoComments),
        new(76, "Pinned Baselines and Assessments are derived and dropped, as for every other schema.",
            Authored: [], Derived: []),
        new(77, "Coverage publication tables are derived and dropped; they are recomputed from a new Trial.",
            Authored: [], Derived: ["GrammarCoverageSegmentations", "GrammarCoverageMeasurements", "GrammarCoverageNoAnalysisRank"]),
        new(78, "The Corpus Source index is read in place and is derived.", Authored: [], Derived: []),
        new(79, "Word type dispositions are authored judgements and are carried.",
            Authored: ["WordTypeDispositions"], Derived: []),
    ];

    /// <summary>Every table the upgrade carries.</summary>
    internal static IEnumerable<string> AuthoredTables => All.SelectMany(step => step.Authored);

    /// <summary>Every table the upgrade drops.</summary>
    internal static IEnumerable<string> DerivedTables => All.SelectMany(step => step.Derived);

    /// <summary>Works out what an upgrade of a store at <paramref name="sourceSchema"/> does.</summary>
    /// <param name="sourceSchema">The schema the store was at.</param>
    /// <param name="firstSummarySchema">Overrides the first schema whose Difference summaries are readable, for tests.</param>
    internal static StoreUpgradePlan PlanFor(int sourceSchema, int? firstSummarySchema = null)
    {
        var unreadable = All.Where(step => step.ReadableFrom is not null)
            .Where(step => sourceSchema < (firstSummarySchema is { } first && step.To == 56 ? first : step.To))
            .SelectMany(step => step.ReadableFrom!).ToHashSet(StringComparer.Ordinal);
        var kept = AuthoredTables.Where(table => !unreadable.Contains(table)).ToArray();
        var derived = DerivedTables.Concat(unreadable).ToHashSet(StringComparer.Ordinal);
        var crossed = All.Where(step => step.To > sourceSchema).OrderBy(step => step.To).ToArray();
        return new StoreUpgradePlan(sourceSchema, crossed, kept, derived, !unreadable.Contains("ProposalDifferences"));
    }

    private static void RescalePerWordLimit(string table, HashSet<string> oldColumns, HashSet<string> newColumns,
        List<string> names, List<string> values)
    {
        // Before schema 60 a Named Selection's limit counted analysis attempts; it now counts parser work.
        if (table != "NamedSelections" || !oldColumns.Contains("PerWordStepLimit") ||
            !newColumns.Contains("PerWordParserWorkLimit") || oldColumns.Contains("PerWordParserWorkLimit")) return;
        names.Add(MotifStoreUpgrade.Quote("PerWordParserWorkLimit"));
        values.Add("CASE WHEN PerWordStepLimit IS NULL THEN NULL WHEN PerWordStepLimit <= 92233720368547758 THEN PerWordStepLimit * 100 " +
            "ELSE 9223372036854775807 END");
    }

    // A schema 61 Note becomes a Comment by a person with no recorded name; blank Notes are dropped.
    private static void FoldNotesIntoComments(StoreUpgradeStep.Context context)
    {
        var (destination, transaction, tables, carried) = context;
        if (!tables.Contains("Proposals") || tables.Contains("ProposalComments") ||
            !MotifStoreUpgrade.Columns(destination, "old", "Proposals", transaction).Contains("Note")) return;
        var notes = MotifStoreUpgrade.Scalar(destination, "SELECT COUNT(*) FROM old.Proposals WHERE Note IS NOT NULL AND trim(Note) <> '';", transaction);
        MotifStoreUpgrade.Execute(destination, "INSERT INTO main.ProposalComments (ProposalId, Kind, AuthorLabel, PersonName, Text, IntentDigest, CreatedUtc, EditedUtc) " +
            "SELECT ProposalId, 'standard', NULL, '', Note, NoteIntentDigest, COALESCE(NoteUpdatedUtc, strftime('%Y-%m-%dT%H:%M:%fZ','now')), NULL " +
            "FROM old.Proposals WHERE Note IS NOT NULL AND trim(Note) <> '' ORDER BY rowid;", transaction);
        carried["ProposalComments"] = MotifStoreUpgrade.Scalar(destination, "SELECT COUNT(*) FROM main.ProposalComments;", transaction);
        if (carried["ProposalComments"] != notes) throw new InvalidDataException(StoreUpgradeMessages.Format("CountChanged", "ProposalComments"));
    }
}
