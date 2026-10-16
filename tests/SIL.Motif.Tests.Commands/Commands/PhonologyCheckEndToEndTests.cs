using System.Xml.Linq;
using SIL.LCModel;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Phonology;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>Runs a phonology check from the saved Example Set through the job runner to the stored result.</summary>
public abstract class PhonologyCheckEndToEndBase : IDisposable
{
    protected const string SwahiliCopyVariable = "MOTIF_T18_SWH_COPY";
    protected const string RuleFourGuid = "01a137f6-eaa6-cc60-97db-a0de419276f9";
    protected readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.PhonologyCheckEndToEnd",
        Guid.NewGuid().ToString("N"));

    protected async Task<(string AssessmentId, PhonologyResultsPageResponse Page)> Check(
        string project, IReadOnlyList<PhonologyExample> examples, string? parserPath)
    {
        var managedRoot = Path.Combine(_root, "managed-" + Guid.NewGuid().ToString("N"));
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(project), managedRoot);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var saved = PhonologyExampleCommands.Save(new SavePhonologyExamplesRequest(project, "Check", examples));
        Assert.True(saved.Succeeded, saved.Refusal?.Message);
        var queued = PhonologyExampleCommands.Check(new PhonologyCheckRequest(project,
            saved.Value!.ExampleSet.Revision.RevisionId));
        Assert.True(queued.Succeeded, queued.Refusal?.Message);

        await using var runner = new InProcessRunnerLauncher(new JobRunnerLaunchOptions(managedRoot, parserPath));
        runner.Start(project);
        await runner.WhenIdleAsync().WaitAsync(TimeSpan.FromMinutes(3));
        var status = JobCommands.Show(new ShowJobRequest(project, queued.Value!.JobId, MotifProductVersion.CurrentText));
        Assert.True(status.Value!.Status == JobStatus.Completed, "The check job ended " + status.Value.Status +
            " (" + status.Value.FailureCategory + ").");

        var assessments = JobCommands.Assessments(new JobAssessmentsRequest(project, queued.Value.JobId,
            MotifProductVersion.CurrentText));
        Assert.True(assessments.Succeeded, assessments.Refusal?.Message);
        var assessmentId = Assert.Single(assessments.Value!.Assessments).AssessmentId;
        var page = PhonologyExampleCommands.Results(new PhonologyResultsRequest(project, assessmentId,
            new PhonologyResultsRead.Page(100)));
        Assert.True(page.Succeeded, page.Refusal?.Message);
        return (assessmentId, Assert.IsType<PhonologyResultsPageResponse>(page.Value));
    }

    protected string BlankProject()
    {
        Directory.CreateDirectory(_root);
        var directory = Path.Combine(_root, "blank-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using (var cache = NewLangProjFixture.CreateCache(directory))
            new FwDataProjectLoader().Save(cache);
        return NewLangProjFixture.FwDataPath(directory);
    }

    protected string CopySwahili(string source, string name)
    {
        var target = Path.Combine(_root, name);
        CopyDirectory(source, target);
        return Path.Combine(target, "swh.fwdata");
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (var directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
    }

    protected static void RemoveRuleReference(string fwdata, string guid)
    {
        var document = XDocument.Load(fwdata);
        var rules = document.Descendants("rt").First(rt => (string?)rt.Attribute("class") == "PhPhonData")
            .Element("PhonRules")!;
        rules.Elements().First(element => string.Equals((string?)element.Attribute("guid"), guid,
            StringComparison.OrdinalIgnoreCase)).Remove();
        document.Save(fwdata);
    }

    protected static IReadOnlyList<PhonologyExample> Examples() =>
    [
        new("a", "a-ku", ["e-ki"], new PhonologyGenerationContext.FwdataEntry(
            "01a137cb-5854-43af-3726-2ae782b6acb3", "01a137cb-5854-679c-d51a-fda4a7aee768", PhonologyMsaClass.MoStemMsa)),
        new("b", "pya", ["pya"], new PhonologyGenerationContext.FwdataEntry(
            "01a137cb-5832-fc63-4303-a5f32c34a084", "01a137cb-5832-2cc0-cd47-ec51ab7ed477", PhonologyMsaClass.MoStemMsa)),
    ];

    protected static IReadOnlyList<PhonologyExample> SwahiliExamples()
    {
        PhonologyExample Example(string id, string underlying, string expected, string entry, string msa,
            string locator) => new(id, underlying, [expected], new PhonologyGenerationContext.FwdataEntry(
                entry, msa, PhonologyMsaClass.MoStemMsa), new CitationView("PH-NASAL-N: Steere 1894; Polomé 1967", locator));
        const string wili = "01a137cb-5854-43af-3726-2ae782b6acb3", wiliMsa = "01a137cb-5854-679c-d51a-fda4a7aee768";
        const string baya = "01a137cb-582c-8ae4-513b-2d97208d62c1", bayaMsa = "01a137cb-582d-607b-a096-ab39aa375c0c";
        return
        [
            Example("r4-w-hardening", "m+wili", "mbili", wili, wiliMsa, "P2 §4"),
            Example("r2-labial-assimilation", "n+baya", "mbaya", baya, bayaMsa, "P2 §4"),
            Example("r3-r4-feeding", "n+wili", "mbili", wili, wiliMsa, "P2 §4"),
            Example("unchanged-pya", "pya", "pya", "01a137cb-5832-fc63-4303-a5f32c34a084",
                "01a137cb-5832-2cc0-cd47-ec51ab7ed477", "control"),
            Example("unchanged-already-labial", "m+baya", "mbaya", baya, bayaMsa, "control"),
        ];
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
public sealed class PhonologyCheckUnavailableTests : PhonologyCheckEndToEndBase
{
    [Fact]
    public async Task AMissingParserRecordsAnUnavailableCheckNeverAPass()
    {
        var project = BlankProject();
        var run = await Check(project, Examples(), parserPath: Path.Combine(_root, "no-such-pangloss"));

        var unavailable = Assert.IsType<PhonologyInvocationBinding.Unavailable>(run.Page.Assessment.Invocation);
        Assert.True(unavailable.Reason is PhonologyInvocationReason.AbsentProducer or
            PhonologyInvocationReason.ProducerUnavailable, unavailable.Reason + ": " + unavailable.Detail);
        Assert.All(run.Page.Items, item => Assert.IsType<PhonologyRowSummary.Unavailable>(item.Evidence));
        Assert.Equal(Examples().Count, run.Page.Items.Count);
    }
}

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
public sealed class PhonologyCheckRealParserTests : PhonologyCheckEndToEndBase
{
    [RealParserFact]
    public async Task AGrammarTheParserCannotLoadIsRecordedUnavailableNeverAgreeing()
    {
        var project = BlankProject();
        var run = await Check(project, Examples(), PanGlossExecutable.TryLocate());

        var refused = Assert.IsType<PhonologyInvocationBinding.Unavailable>(run.Page.Assessment.Invocation);
        Assert.Equal(PhonologyInvocationReason.InvocationRefused, refused.Reason);
        Assert.Equal(Examples().Count, run.Page.Items.Count);
        Assert.All(run.Page.Items, item => Assert.IsType<PhonologyRowSummary.Unavailable>(item.Evidence));
    }

    [SwahiliRealParserFact]
    public async Task SwahiliExamplesAgreeAndRemovingARuleTurnsThemIntoDisagreements()
    {
        var parser = PanGlossExecutable.TryLocate();
        var source = Environment.GetEnvironmentVariable("MOTIF_T18_SWH_COPY")!;
        var intact = CopySwahili(source, "intact");
        var positive = await Check(intact, SwahiliExamples(), parser);
        var counts = Assert.IsType<PhonologyInvocationBinding.Produced>(positive.Page.Assessment.Invocation);
        Assert.Equal((5, 0, 0), (counts.AgreeCount, counts.DisagreeCount, counts.UnavailableCount));

        var unknown = await Check(CopySwahili(source, "unknown-entry"), [new PhonologyExample("ghost", "pya", ["pya"],
            new PhonologyGenerationContext.FwdataEntry(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(),
                PhonologyMsaClass.MoStemMsa))], parser);
        var ghost = Assert.IsType<PhonologyInvocationBinding.Produced>(unknown.Page.Assessment.Invocation);
        Assert.Equal((0, 0, 1), (ghost.AgreeCount, ghost.DisagreeCount, ghost.UnavailableCount));

        var removed = CopySwahili(source, "removed-r4");
        RemoveRuleReference(removed, RuleFourGuid);
        var negative = await Check(removed, SwahiliExamples(), parser);
        var changed = Assert.IsType<PhonologyInvocationBinding.Produced>(negative.Page.Assessment.Invocation);
        Assert.True(changed.DisagreeCount >= 1, "Removing a rule must turn at least one example into a disagreement.");
        var detail = PhonologyExampleCommands.Results(new PhonologyResultsRequest(removed, negative.AssessmentId,
            new PhonologyResultsRead.Example("r3-r4-feeding")));
        var row = Assert.IsType<PhonologyExampleResultResponse>(detail.Value);
        Assert.Equal(PhonologyRowDisposition.Disagree,
            Assert.IsType<PhonologyRowEvidence.Produced>(row.Result.Evidence).Disposition);
    }

    private sealed class SwahiliRealParserFactAttribute : RealParserFactAttribute
    {
        public SwahiliRealParserFactAttribute()
        {
            var copy = Environment.GetEnvironmentVariable("MOTIF_T18_SWH_COPY");
            if (Skip is null && (string.IsNullOrWhiteSpace(copy) || !File.Exists(Path.Combine(copy, "swh.fwdata"))))
                Skip = "MOTIF_T18_SWH_COPY does not name a copy of the Swahili project; the acceptance check was not run.";
        }
    }
}
