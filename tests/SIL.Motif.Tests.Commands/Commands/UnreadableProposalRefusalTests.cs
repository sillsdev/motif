using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Commands that read one Proposal whose stored content no longer parses.
/// </summary>
public sealed class UnreadableProposalRefusalTests : IDisposable
{
    internal const string UnknownKind = "lexical/lexSense/nope";

    private static readonly JsonSerializerOptions DraftOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.UnreadableProposalRefusalTests",
        Guid.NewGuid().ToString("N"));

    public UnreadableProposalRefusalTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Fact]
    public void AContractParseFailureInsideAStoreRunIsAProposalRefusalNotAnException()
    {
        var outcome = ProjectStoreCommand.RunOperation<string>("/p/project.fwdata",
            () => throw new ContractParseException("Unknown operation kind 'x'."));

        Assert.Equal(RefusalCodes.ProposalUnreadable, outcome.Refusal?.Code);
        Assert.Equal(FailureReason.Refused, outcome.Refusal!.Reason);
        Assert.Contains("Unknown operation kind 'x'.", outcome.Refusal.Message, StringComparison.Ordinal);
        Assert.Equal("/p/project.fwdata", outcome.Refusal.Facts["fwDataPath"]);
    }

    [Fact]
    public void AnUnrelatedFailureInsideAStoreRunStillThrows()
    {
        Assert.Throws<InvalidOperationException>(() => ProjectStoreCommand.RunOperation<string>("/p/project.fwdata",
            () => throw new InvalidOperationException("a bug")));
        Assert.Throws<System.Text.Json.JsonException>(() => ProjectStoreCommand.RunOperation<string>(
            "/p/project.fwdata", () => throw new System.Text.Json.JsonException("a bug")));
    }

    [Fact]
    public void OnlyUnparsableContentIsUnreadableAndEveryOtherLoadFailureStaysInconsistent()
    {
        var unreadable = ProposalCommands.ProposalLoadRefusal(new ContractParseException("Unknown operation kind 'x'."));
        Assert.Equal(RefusalCodes.ProposalUnreadable, unreadable.Code);
        Assert.Equal(FailureReason.Refused, unreadable.Reason);

        foreach (var other in new Exception[] { new IOException("busy"), new InvalidOperationException("bug"),
            new InvalidDataException("store row damaged") })
            Assert.Equal(RefusalCodes.ProposalInconsistent, ProposalCommands.ProposalLoadRefusal(other).Code);
    }

    [Fact]
    public void ALongParserMessageIsBoundedInTheMessageAndTheDetailFact()
    {
        var longMessage = new ContractParseException("Unknown operation kind '" + new string('k', 5000) + "'.");

        var outcome = ProjectStoreCommand.RunOperation<string>("/p/project.fwdata", () => throw longMessage);

        Assert.InRange(outcome.Refusal!.Message.Length, 1, 600);
        Assert.InRange(outcome.Refusal.Facts["exceptionDetail"].Length, 1, 400);
        Assert.InRange(ProposalCommands.ProposalLoadRefusal(longMessage).Message.Length, 1, 600);
    }

    [Fact]
    public void ADraftWhoseContentIsJsonNullIsUnreadableToReadAndToQueue()
    {
        var (project, bad, _, _) = Seed("null");

        var read = Trials.Read(new ReadTrialRequest(project, bad));
        var queued = JobCommands.EnqueueTrial(new EnqueueTrialRequest(project, "1.0", bad, Words: ["cat"]));

        foreach (var refusal in new[] { read.Refusal, queued.Refusal })
        {
            Assert.Equal(RefusalCodes.ProposalUnreadable, refusal?.Code);
            Assert.Equal(FailureReason.Refused, refusal!.Reason);
        }
    }

    [Fact]
    public void ReadingTheTrialOfAnUnreadableProposalIsRefused()
    {
        var seeded = Seed();

        AssertUnreadable(Trials.Read(new ReadTrialRequest(seeded.Project, seeded.Bad)).Refusal);
        Assert.True(Trials.Read(new ReadTrialRequest(seeded.Project, seeded.Good)).Succeeded);
    }

    [Fact]
    public void ReadingTheOverviewOfAnUnreadableProposalIsRefused()
    {
        var (project, bad, _, _) = Seed();

        AssertUnreadable(OverviewCommand.Overview(new OverviewRequest(project, bad)).Refusal);
    }

    [Fact]
    public void ProducingTheDifferenceOfAnUnreadableProposalIsRefused()
    {
        var (project, bad, _, _) = Seed();

        var produced = ProposalDifferenceCommands.Produce(new ProposalDifferenceProduceRequest(
            project, bad, Guid.NewGuid().ToString("D")), null, CancellationToken.None);

        AssertUnreadable(produced.Refusal);
    }

    [Fact]
    public void QueueingATrialForADraftThatFailsParseIsRefusedAndQueuesNothing()
    {
        var (project, bad, draftJson, _) = Seed();

        var queued = JobCommands.EnqueueTrial(new EnqueueTrialRequest(project, "1.0", bad, Words: ["cat"]));

        AssertUnreadable(queued.Refusal);
        ProjectStoreCommand.Run(project, "1.0", (database, locator) =>
        {
            Assert.Equal(["job/seed"], new JobRepository(database).ListByProjectAndKind(
                ProjectWorkspaceKey.Compute(locator), JobCommands.TrialKind).Select(job => job.JobId));
            Assert.Equal(draftJson, new ProposalRepository(database).GetDraft("tried").ProposalJson);
            return CommandOutcome<string>.Success("checked");
        });
    }

    [Fact]
    public void ADraftThatIsNotJsonAtAllIsRefusedToReadAndToQueue()
    {
        var (project, bad, _, _) = Seed("{ broken");

        var read = Trials.Read(new ReadTrialRequest(project, bad));
        var queued = JobCommands.EnqueueTrial(new EnqueueTrialRequest(project, "1.0", bad, Words: ["cat"]));

        foreach (var refusal in new[] { read.Refusal, queued.Refusal })
        {
            Assert.Equal(RefusalCodes.ProposalUnreadable, refusal?.Code);
            Assert.Equal(FailureReason.Refused, refusal!.Reason);
        }
    }

    private static void AssertUnreadable(Refusal? refusal)
    {
        Assert.Equal(RefusalCodes.ProposalUnreadable, refusal?.Code);
        Assert.Equal(FailureReason.Refused, refusal!.Reason);
        Assert.Contains(UnknownKind, refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A project whose one Draft was tried while it was readable and has since been rewritten with an operation
    /// kind the product does not know, plus a second Proposal that stays readable.
    /// </summary>
    internal (string Project, string Bad, string BadJson, string Good) Seed(string? draftJson = null)
    {
        var project = Path.Combine(_root, Guid.NewGuid().ToString("N"), "project.fwdata");
        Directory.CreateDirectory(Path.GetDirectoryName(project)!);
        File.WriteAllText(project, "synthetic project marker");
        var bad = CanonicalId.Mint("proposal/");
        var good = CanonicalId.Mint("proposal/");
        string unreadableJson = "";
        var seeded = ProjectStoreCommand.Run(project, "1.0", (database, locator) =>
        {
            var proposals = new ProposalRepository(database);
            var jobs = new JobRepository(database);
            var key = ProjectWorkspaceKey.Compute(locator);
            proposals.CreateDraft("tried", bad, JsonSerializer.Serialize(GlossDraft(bad), DraftOptions));
            var seedJob = jobs.Create("job/seed", key, TrialJobHandler.TrialKind, TrialInput(bad), "2026-09-24T11:00:00Z");
            jobs.Transition(seedJob.JobId, JobStatus.Running);
            var wrongKind = GlossDraft(bad);
            wrongKind.Operations[0].Kind = UnknownKind;
            unreadableJson = draftJson ?? JsonSerializer.Serialize(wrongKind, DraftOptions);
            proposals.SaveDraft("tried", unreadableJson);
            var goodJson = ProposalCommands.BuildProposalJson(GlossDraft(good));
            proposals.SaveRevision(new ProposalRevisionRecord(good, IntentDigest.Compute(ProposalJsonParser.Parse(goodJson)),
                goodJson, "proposed", null, null, null));
            return CommandOutcome<string>.Success("seeded");
        });
        Assert.True(seeded.Succeeded, seeded.Refusal?.Message);
        return (project, bad.Value, unreadableJson, good.Value);
    }

    private static string TrialInput(CanonicalId proposalId) => JsonSerializer.Serialize(new
    {
        proposalJson = ProposalCommands.BuildProposalJson(GlossDraft(proposalId)),
        scope = (string?)null,
        words = new[] { "cat" },
        allWords = false,
        baselineAssessmentIds = (string[]?)null,
        limits = (object?)null,
    }, SIL.Motif.Contract.MotifJson.CreateOptions());

    private static DraftDocument GlossDraft(CanonicalId proposalId)
    {
        using var after = JsonDocument.Parse("""{"ws":"en","text":"x"}""");
        return new DraftDocument
        {
            ProposalId = proposalId.Value,
            ContractVersions = new Dictionary<string, string>(StringComparer.Ordinal) { ["lexical"] = "1.0" },
            Operations =
            [
                new DraftOperation
                {
                    OperationId = CanonicalId.Mint("operation/").Value,
                    Kind = "lexical/lexSense/setGloss",
                    Target = CanonicalId.Mint("lexSense/").Value,
                    After = after.RootElement.EnumerateObject()
                        .ToDictionary(property => property.Name, property => property.Value.Clone(),
                            StringComparer.Ordinal),
                },
            ],
        };
    }
}
