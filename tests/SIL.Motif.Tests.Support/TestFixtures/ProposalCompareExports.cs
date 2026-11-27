using System.Collections.Immutable;
using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>Builds completed Trial jobs with retained evidence exports, so comparisons run over exact, known readings.</summary>
public static class ProposalCompareExports
{
    /// <summary>One expanded form's observation: the reading numbers before and after, and whether each side finished.</summary>
    public sealed record FormSpec(string Form, int[] Before, int[] After, string? WritingSystemId = "ws",
        bool Dispatched = true, bool Complete = true)
    {
        /// <summary>Real Reading signatures for the before side, replacing the numbered ones when set.</summary>
        public IReadOnlyList<ReadingSignature>? BeforeSignatures { get; init; }

        /// <summary>Real Reading signatures for the after side, replacing the numbered ones when set.</summary>
        public IReadOnlyList<ReadingSignature>? AfterSignatures { get; init; }
    }

    /// <summary>Records the project's current Baseline as the one <see cref="AddTrial"/> measures with the same fill.</summary>
    public static void RecordCurrentBaseline(MotifDatabase database, ProjectLocator project, char fill = 'a')
    {
        var digest = BaselineDigest(fill);
        new SIL.Motif.Worker.Baselines.BaselineRepository(database).Record(ProjectWorkspaceKey.Compute(project),
            new SIL.Motif.Worker.Baselines.BaselinePublication(Path.GetTempPath(), Path.Combine(Path.GetTempPath(), "baseline.zip"),
                new SIL.Motif.Contract.Baselines.BaselineToken("project", digest, "test", "2026-01-01T00:00:00Z", digest)),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, TestTextWords.Empty);
    }

    public static string BaselineDigest(char fill) => "sha256:" + new string(fill, 64);

    public static string ReadingId(int number) => Signature(number).Id;

    /// <summary>Publishes one completed Trial for a new Proposal and returns the Proposal ID.</summary>
    public static string AddTrial(MotifDatabase database, ProjectLocator project, IEnumerable<FormSpec> forms,
        string? proposalId = null, char baseline = 'a', int minute = 0, bool completed = true,
        string? sample = null, string? wordListDigest = null, int second = 0, string? jobId = null,
        string? parserBefore = null, string? parserAfter = null, IReadOnlyList<object>? operations = null,
        IReadOnlyList<string>? siblings = null, string siblingPattern = "per-form")
    {
        proposalId ??= CanonicalId.Mint("proposal/").Value;
        jobId ??= "job/" + Guid.NewGuid().ToString("D");
        var key = ProjectWorkspaceKey.Compute(project);
        var proposalJson = JsonSerializer.Serialize(new
        {
            contractVersions = operations is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["lexical"] = "1.0", ["lists"] = "1.0" },
            proposalId, operations = operations ?? (IReadOnlyList<object>)Array.Empty<object>(),
        });
        var input = new TrialJobInput(proposalJson, null) {
            Sample = sample, WordListDigest = wordListDigest,
            Siblings = siblings is null ? null : new TrialSiblingsInput(JobWordLists.Store(database, siblings), siblingPattern, 42, 1000,
                ["ish"], "abc", siblings.Count + 3, 2),
        };
        var created = new DateTimeOffset(2026, 1, 1, 0, minute, second, TimeSpan.Zero).ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");
        new JobRepository(database).RecordInterrupted(jobId, key, "trial", JsonSerializer.Serialize(input, MotifJson.CreateOptions()),
            "{}", created, JobStatus.Failed);
        if (completed)
        {
            using var connection = database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Jobs SET Status='completed', FailureCategory='none' WHERE JobId=$id;";
            command.Parameters.AddWithValue("$id", jobId);
            command.ExecuteNonQuery();
        }
        SaveProposal(database, proposalId, proposalJson);
        var list = forms.ToList();
        var words = list.Select((spec, index) => new TrialExportWord("case/" + index, spec.WritingSystemId, spec.Form, [],
            [new TrialExportForm(spec.Form, "key/" + spec.Form, "submitted", TrialExportStatus.Accepted, "ok", [],
                Side(spec, spec.Before, spec.BeforeSignatures), Side(spec, spec.After, spec.AfterSignatures), [])],
            TrialExportStatus.Accepted, "ok", [])).ToArray();
        var digest = BaselineDigest(baseline);
        var provenance = new TrialExportProvenance(null, proposalId, "revision-" + proposalId[^6..],
            new("motif-intent-v1", digest), new("project", digest, "test", "2026-01-01T00:00:00Z", digest),
            null, null, new(parserBefore, parserAfter), new(null, null), null);
        TrialExportArtifact.Publish(database, jobId, new TrialExportDenominators(words.Length, words.Length, words.Length,
            new(words.Length, words.Length), new(words.Length, words.Length)), provenance, [], words);
        return proposalId;
    }

    /// <summary>Stores the Proposal's current revision, so a Trial's revision matches it unless the test changes the content.</summary>
    public static void SaveProposal(MotifDatabase database, string proposalId, string proposalJson, string? supersededBy = null)
    {
        var revision = FrozenProposalRevision.Create(proposalJson);
        var repository = new SIL.Motif.Worker.Store.ProposalRepository(database);
        repository.SaveRevision(new SIL.Motif.Worker.Store.ProposalRevisionRecord(
            CanonicalId.Parse(proposalId), revision.IntentDigest, revision.ProposalJson, "proposed", null, null, null));
        if (supersededBy is null) return;
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Proposals SET Status='superseded', SupersededBy=$by WHERE ProposalId=$id;";
        command.Parameters.AddWithValue("$by", supersededBy);
        command.Parameters.AddWithValue("$id", proposalId);
        command.ExecuteNonQuery();
    }

    /// <summary>A Proposal document whose intent digest differs from the one <see cref="AddTrial"/> stores.</summary>
    public static string OtherProposalJson(string proposalId) => JsonSerializer.Serialize(new
    {
        contractVersions = new Dictionary<string, string> { ["lexical"] = "1.0" }, proposalId,
        operations = new[] { new { operationId = CanonicalId.Mint("operation/").Value, kind = "lexical/lexSense/setGloss",
            target = CanonicalId.FromGuid(Guid.NewGuid()).Value, after = new { ws = "en", text = "changed" } } },
    });

    private static TrialExportSide Side(FormSpec spec, int[] readings, IReadOnlyList<ReadingSignature>? signatures) =>
        new("complete", spec.Dispatched, spec.Complete, TrialExportStatus.Accepted, "ok", [],
            (signatures ?? [.. (readings ?? []).Select(Signature)]).Select(signature => new TrialExportReading(signature.Id, signature,
                ReadingAcceptance.LexiconBacked, [])).ToArray());

    /// <summary>
    /// The Reading for a number. The entry is <c>number % 1000</c>; <c>number / 1000</c> picks the sense: 0 unresolved,
    /// 1 resolved to the first sense, 2 resolved to the second, 3 ambiguous between both.
    /// </summary>
    public static ReadingSignature Signature(int number)
    {
        static string Id(int value) => CanonicalId.FromGuid(new Guid(value, 0, 0, [1, 2, 3, 4, 5, 6, 7, 8])).Value;
        var sense = number / 1000;
        var first = Id(900001);
        var second = Id(900002);
        var (senseId, candidates, status) = sense switch
        {
            1 => ((string?)first, ImmutableArray<string>.Empty, ReadingSenseStatus.Resolved),
            2 => (second, ImmutableArray<string>.Empty, ReadingSenseStatus.Resolved),
            3 => (null, [first, second], ReadingSenseStatus.Ambiguous),
            _ => (null, ImmutableArray<string>.Empty, ReadingSenseStatus.Unresolved),
        };
        return new([new ReadingMorpheme(Id(number % 1000), senseId, candidates, status, null, null, null, true, null, [])]);
    }
}
