using SIL.Motif.Commands;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.Commands;
using Xunit;

namespace SIL.Motif.Tests.Assess;

public sealed class TrialCleanTests
{
    private static readonly DroppedGrammarFinding Finding = new("grammar.environment.invalid", [], "dropped");

    [Fact]
    public void ATrialIsCleanOnlyWhenTheCheckRanAndNothingWasIntroduced() =>
        Assert.True(Trial(new DroppedGrammar([], 2)).Clean);

    [Fact]
    public void ATrialThatIntroducedDroppedGrammarIsNotClean() =>
        Assert.False(Trial(new DroppedGrammar([Finding], 0)).Clean);

    [Fact]
    public void ATrialWhoseCheckDidNotRunIsNotClean() =>
        Assert.False(Trial(DroppedGrammar.NotChecked("no parser")).Clean);

    [Fact]
    public void ATrialThatRecordedNoCheckIsNotCleanAndReadsAsNotChecked()
    {
        var trial = Trial(null);

        Assert.False(trial.Clean);
        Assert.False(trial.DroppedGrammarOrUnchecked.Checked);
    }

    private static TrialRecord Trial(DroppedGrammar? dropped) => new("job", "proposal", "digest",
        new BaselineToken("project", "sha256:" + new string('a', 64), "1", "2026-08-29T00:00:00Z",
            "sha256:" + new string('b', 64)),
        null, new Dictionary<string, string>(), [], false, null, JobStatus.Completed, null, TrialFreshness.Current)
    {
        DroppedGrammar = dropped,
    };
}

public sealed class TrialDroppedGrammarByAssessmentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.DroppedGrammarByAssessment",
        Guid.NewGuid().ToString("N"));

    public TrialDroppedGrammarByAssessmentTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Fact]
    public void EachAssessmentIsJudgedByTheTrialThatRecordedIt()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.RecordAssessment(proposal, "older", AssessmentKinds.ParseTime);
        fixture.RecordAssessment(proposal, "newer", AssessmentKinds.ParseTime);
        var finding = new DroppedGrammarFinding("grammar.environment.invalid", [], "dropped");
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "older-trial", ["older"],
            droppedGrammar: new DroppedGrammar([finding], 0));
        fixture.CreateJob(proposal.Json, "2026-09-24T12:00:00Z", "newer-trial", ["newer"],
            droppedGrammar: new DroppedGrammar([], 0));

        var byAssessment = Trials.DroppedGrammarByAssessment(fixture.Database, fixture.Project, proposal.Id);

        Assert.False(byAssessment["older"].IsClean);
        Assert.True(byAssessment["newer"].IsClean);
    }

    [Fact]
    public void ATrialThatRecordedNoCheckReadsAsNotChecked()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.RecordAssessment(proposal, "after", AssessmentKinds.ParseTime);
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "trial", ["after"], recordDroppedGrammar: false);

        var dropped = Trials.DroppedGrammarByAssessment(fixture.Database, fixture.Project, proposal.Id)["after"];

        Assert.False(dropped.Checked);
        Assert.False(dropped.IsClean);
    }

    [Fact]
    public void AnAssessmentNoTrialRecordedHasNoEntry()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();

        Assert.Empty(Trials.DroppedGrammarByAssessment(fixture.Database, fixture.Project, proposal.Id));
    }
}
