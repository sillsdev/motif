using System.Text.Json;
using SIL.Motif.Worker.Corpus.Ledger;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Worker.Corpus;

/// <summary>Opt-in joins over the pinned lane ledgers; outputs are counts only, never words or ledger text.</summary>
public sealed class LedgerRealCorporaTests(ITestOutputHelper output)
{
    public const string FixturesVariable = "MOTIF_CORPUS_LEDGER_FIXTURES";
    public const string TamFrequencyVariable = "MOTIF_CORPUS_TAM_FREQUENCY";
    public const string SmeDirectoryVariable = "MOTIF_CORPUS_SME_DIR";
    public const string MetricsVariable = "MOTIF_CORPUS_LEDGER_METRICS";
    public const string StoreVariablePrefix = "MOTIF_CORPUS_STORE_";

    [LedgerFixtureFact]
    public void CebJoinsItsFrozenFrequencyListWithNoRefusal() =>
        Measure("ceb", "ceb.ledger.tsv", "ceb.ledger.meta.json", FrequencyKind.Tsv, hasHeader: true, absoluteFrequency: true);

    [LedgerFixtureFact]
    public void SwahiliJoinsItsFrozenFrequencyListWithNoRefusal() =>
        Measure("swh", "swh.ledger.tsv", "swh.ledger.meta.json", FrequencyKind.Tsv, hasHeader: false, absoluteFrequency: true);

    [TamFixtureFact]
    public void TamilJoinsItsFrozenWeightsWithNoRefusal() =>
        Measure("tam", "tam.ledger.tsv", "tam.ledger.meta.json", FrequencyKind.Json, hasHeader: false, absoluteFrequency: false);

    [SmeFixtureFact]
    public void NorthSamiLedgerJoinsItsFrequencyListAndReportsCountsOnly()
    {
        var directory = Environment.GetEnvironmentVariable(SmeDirectoryVariable)!;
        var ledger = Path.Combine(directory, "ledger", "ledger.tsv");
        var sidecar = Path.Combine(directory, "ledger", "ledger.meta.json");
        var frequency = Path.Combine(directory, "eval", "accept-dev.tsv");
        Measure("sme", ledger, sidecar, FrequencyKind.Tsv, hasHeader: false, absoluteFrequency: false, frequency);
    }

    private enum FrequencyKind { Tsv, Json }

    private void Measure(string lane, string ledgerName, string sidecarName, FrequencyKind kind, bool hasHeader,
        bool absoluteFrequency, string? frequencyPath = null)
    {
        var root = Environment.GetEnvironmentVariable(FixturesVariable);
        var ledgerPath = Path.IsPathRooted(ledgerName) ? ledgerName : Path.Combine(root!, ledgerName);
        var sidecarPath = Path.IsPathRooted(sidecarName) ? sidecarName : Path.Combine(root!, sidecarName);
        var ledgerBytes = File.ReadAllBytes(ledgerPath);
        var sidecarBytes = File.ReadAllBytes(sidecarPath);
        var ledger = LedgerTsvReader.Read(ledgerBytes);
        var sidecar = LedgerSidecarReader.Read(sidecarBytes);
        using var raw = JsonDocument.Parse(sidecarBytes);
        var declaredLedger = raw.RootElement.TryGetProperty("ledgerSha256", out var ledgerSha) ? ledgerSha.GetString() : null;
        var declaredFrequency = raw.RootElement.TryGetProperty("frequencySha256", out var frequencySha) ? frequencySha.GetString() : null;
        LedgerSidecarReader.Validate(sidecar, ledger, declaredLedger);

        frequencyPath ??= absoluteFrequency ? sidecar.FrequencySource : Environment.GetEnvironmentVariable(TamFrequencyVariable)!;
        var frequencyBytes = File.ReadAllBytes(frequencyPath);
        var frequency = kind == FrequencyKind.Json
            ? FrequencyListReader.ReadWordCountJson(frequencyBytes, sidecar)
            : FrequencyListReader.ReadTsv(frequencyBytes, sidecar, hasHeader);
        IReadOnlyDictionary<string, TypeCheck>? checks = null;
        if (Environment.GetEnvironmentVariable(StoreVariablePrefix + lane.ToUpperInvariant()) is { Length: > 0 } storePath)
        {
            // A disposable copy of a project database, never the original; the registered path only names the project.
            var registered = Environment.GetEnvironmentVariable(StoreVariablePrefix + lane.ToUpperInvariant() + "_FWDATA")
                ?? Path.ChangeExtension(storePath, ".fwdata");
            var project = new SIL.Motif.Contract.Projects.ProjectLocator(registered, Path.GetFileNameWithoutExtension(registered));
            using var database = SIL.Motif.Host.Store.MotifDatabase.OpenOwned(storePath, project,
                SIL.Motif.Host.Store.MotifSchema.CurrentSchema, new Version(1, 0));
            checks = TypeCheckProducer.FromStore(new SIL.Motif.Worker.Store.AssessmentRepository(database), sidecar.GrammarRevision);
        }
        var report = LedgerCoverage.Build(new(ledger, sidecar, frequency, checks, declaredFrequency));
        var join = report.Join;

        var metrics = new
        {
            lane, checks = checks is null ? null : new
            {
                produced = checks.Count, comparable = checks.Values.Count(item => item.Comparable),
                complete = checks.Values.Count(item => item.Completeness == CheckCompleteness.Complete),
                partial = checks.Values.Count(item => item.Completeness == CheckCompleteness.Partial),
                provedPresent = checks.Values.Count(item => item.ProvedPresent),
                usedByJoin = report.Rows.Count(row => row.Check is not null),
                usedComparable = report.Rows.Count(row => row.Check is { Comparable: true }),
            },
            accountedCounts = report.Done.Accounted.Counts,
            ledgerRows = ledger.Rows.Count, ledgerTokens = ledger.TokenSum, frequencyTypes = frequency.Entries.Count,
            frequencyDigestDeclared = declaredFrequency is not null, frequencyDigestMatches = declaredFrequency is null || declaredFrequency == frequency.Digest,
            join.EligibleTypes, join.EligibleTokens, join.Blocks,
            missing = new { join.MissingTypes, join.MissingWeight }, extra = new { rows = join.Extra.Count, tokens = join.ExtraDeclaredTokens },
            duplicates = new { join.DuplicateTypes, join.DuplicateWeight }, revision = new { join.RevisionTypes, join.RevisionWeight },
            conflicts = new { join.ConflictTypes, join.ConflictWeight, signedDifference = join.Conflicts.Sum(item => item.SignedDifference) },
            join.DuplicateFrequencyKeys, residual = new { join.ResidualTypes, join.ResidualWeight },
            states = report.ByState.ToDictionary(item => item.Key.ToString(), item => new { stale = item.Value.Stale, current = item.Value.Current }),
            noState = report.NoState, dispositionKinds = report.DispositionByKind.ToDictionary(item => item.Key, item => item.Value.Total),
            openMechanisms = report.OpenByMechanism.ToDictionary(item => item.Key, item => item.Value.Total),
            recurringOpen = new { report.RecurringOpenCurrent, report.RecurringOpenStale },
            singletons = new { report.OpenSingletons, share = report.OpenSingletonTokenShare },
            done = new { verdict = report.Done.Verdict.ToString(), accounted = report.Done.Accounted.Verdict.ToString(),
                accountedReasons = report.Done.Accounted.Reasons, correct = report.Done.Correct.Verdict.ToString(),
                correctReasons = report.Done.Correct.Reasons, stable = report.Done.Stable.Verdict.ToString(),
                stableReasons = report.Done.Stable.Reasons },
        };
        var json = JsonSerializer.Serialize(metrics);
        output.WriteLine(json);
        if (Environment.GetEnvironmentVariable(MetricsVariable) is { Length: > 0 } sink) File.AppendAllText(sink, json + "\n");

        Assert.False(join.Blocked);
        if (raw.RootElement.TryGetProperty("stateRows", out var stateRows))
            foreach (var state in Enum.GetValues<ReportState>())
                if (stateRows.TryGetProperty(state.ToString(), out var expected))
                    Assert.Equal(expected.GetInt64(), ledger.Rows.Count(row => row.State.ToString() == state.ToString()));
    }
}

public sealed class LedgerFixtureFactAttribute : FactAttribute
{
    public LedgerFixtureFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(LedgerRealCorporaTests.FixturesVariable)))
            Skip = "Set MOTIF_CORPUS_LEDGER_FIXTURES to the pinned ledger fixture folder.";
    }
}

public sealed class TamFixtureFactAttribute : FactAttribute
{
    public TamFixtureFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(LedgerRealCorporaTests.FixturesVariable)) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(LedgerRealCorporaTests.TamFrequencyVariable)))
            Skip = "Set MOTIF_CORPUS_LEDGER_FIXTURES and MOTIF_CORPUS_TAM_FREQUENCY to the pinned Tamil inputs.";
    }
}

public sealed class SmeFixtureFactAttribute : FactAttribute
{
    public SmeFixtureFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(LedgerRealCorporaTests.SmeDirectoryVariable)))
            Skip = "Set MOTIF_CORPUS_SME_DIR to read the private North Sami ledger in place.";
    }
}
