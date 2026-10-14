using SIL.Motif.Projection;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Trait("MotifTestLevel", "Unit")]
public sealed class WritingSystemRefusalTests
{
    [Fact]
    public void LibLcmsMissingWritingSystemNamesTheTagTheStoreTheRepositoryAndTheSetting()
    {
        var project = Path.Combine(Path.GetTempPath(), "swh", "swh.fwdata");
        var repository = Path.Combine(Path.GetTempPath(), "empty-ws-repository");

        var explained = ProjectWritingSystemsUnavailableException.FromLoadFailure(
            new KeyNotFoundException("The writing system swh was not found in this manager."), project, repository);

        Assert.NotNull(explained);
        Assert.Equal("swh", explained.WritingSystem);
        Assert.Contains("'swh'", explained.Message, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(Path.GetTempPath(), "swh", "WritingSystemStore"), explained.Message, StringComparison.Ordinal);
        Assert.Contains(repository, explained.Message, StringComparison.Ordinal);
        // The suite selects a private repository through the setting, so the setting's remedy is the one given.
        Assert.Contains(ProjectWritingSystemsUnavailableException.RepositorySetting, explained.Message, StringComparison.Ordinal);
        Assert.Contains("Unset", explained.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnrelatedLookupFailureIsNotExplainedAsAWritingSystemProblem()
    {
        Assert.Null(ProjectWritingSystemsUnavailableException.FromLoadFailure(
            new KeyNotFoundException("The given key was not present."), "p.fwdata", null));
    }

    [Fact]
    public void AMissingWritingSystemIsARefusalAboutSetupNotDamagedData()
    {
        var project = Path.Combine(Path.GetTempPath(), "swh", "swh.fwdata");
        var internalCopy = Path.Combine(Path.GetTempPath(), "assessment-runs", "copy", "swh.fwdata");
        var outcome = ProjectStoreCommand.RunOperation<string>(project, () =>
            throw ProjectWritingSystemsUnavailableException.FromLoadFailure(
                new KeyNotFoundException("The writing system swh was not found in this manager."), internalCopy, "/ws")!);

        Assert.Equal("project.writing-system-missing", outcome.Refusal!.Code);
        Assert.Equal(FailureReason.Refused, outcome.Refusal.Reason);
        Assert.Equal("swh", outcome.Refusal.Facts["writingSystem"]);
        Assert.Equal("/ws", outcome.Refusal.Facts["writingSystemRepository"]);
        // The open that failed was of Motif's own copy; the remedy must name the store beside the caller's project.
        Assert.Equal(Path.Combine(Path.GetTempPath(), "swh", "WritingSystemStore"),
            outcome.Refusal.Facts["projectWritingSystemStore"]);
        Assert.DoesNotContain("assessment-runs", outcome.Refusal.Message, StringComparison.Ordinal);
    }
}
