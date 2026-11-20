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
        bool Dispatched = true, bool Complete = true);

    public static string BaselineDigest(char fill) => "sha256:" + new string(fill, 64);

    public static string ReadingId(int number) => Signature(number).Id;

    /// <summary>Publishes one completed Trial for a new Proposal and returns the Proposal ID.</summary>
    public static string AddTrial(MotifDatabase database, ProjectLocator project, IEnumerable<FormSpec> forms,
        string? proposalId = null, char baseline = 'a', int minute = 0, bool completed = true,
        string? sample = null, string? wordListDigest = null)
    {
        proposalId ??= CanonicalId.Mint("proposal/").Value;
        var jobId = "job/" + Guid.NewGuid().ToString("D");
        var key = ProjectWorkspaceKey.Compute(project);
        var proposalJson = JsonSerializer.Serialize(new
        {
            contractVersions = new Dictionary<string, string>(), proposalId, operations = Array.Empty<object>(),
        });
        var input = new TrialJobInput(proposalJson, null) { Sample = sample, WordListDigest = wordListDigest };
        var created = new DateTimeOffset(2026, 1, 1, 0, minute, 0, TimeSpan.Zero).ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");
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
        var list = forms.ToList();
        var words = list.Select((spec, index) => new TrialExportWord("case/" + index, spec.WritingSystemId, spec.Form, [],
            [new TrialExportForm(spec.Form, "key/" + spec.Form, "submitted", TrialExportStatus.Accepted, "ok", [],
                Side(spec, spec.Before), Side(spec, spec.After), [])],
            TrialExportStatus.Accepted, "ok", [])).ToArray();
        var digest = BaselineDigest(baseline);
        var provenance = new TrialExportProvenance(null, proposalId, "revision-" + proposalId[^6..],
            new("motif-intent-v1", digest), new("project", digest, "test", "2026-01-01T00:00:00Z", digest),
            null, null, new(null, null), new(null, null), null);
        TrialExportArtifact.Publish(database, jobId, new TrialExportDenominators(words.Length, words.Length, words.Length,
            new(words.Length, words.Length), new(words.Length, words.Length)), provenance, [], words);
        return proposalId;
    }

    private static TrialExportSide Side(FormSpec spec, int[] readings) => new("complete", spec.Dispatched, spec.Complete,
        TrialExportStatus.Accepted, "ok", [],
        readings.Select(number => new TrialExportReading(ReadingId(number), Signature(number),
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
