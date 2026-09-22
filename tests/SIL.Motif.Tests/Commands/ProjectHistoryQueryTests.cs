using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.Assess;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Pins <see cref="ProjectHistoryQuery"/>: an empty history before anything happened, one Baseline entry
/// once one is captured, one Assessment entry per <c>assess</c> run, and every entry newest first.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class ProjectHistoryQueryTests : IDisposable
{
    private static readonly SelectionRequest PastedWord = new(false, [], ["motifa"], false, null);
    private static readonly IReadOnlyList<AssessmentKind> CollectedKinds =
        [AssessmentKind.ParseTime, AssessmentKind.ObjectTiming];

    private readonly PristineProjectFixture _pristine;
    private readonly string _managedRootsParent =
        Path.Combine(Path.GetTempPath(), "SIL.Motif.ProjectHistoryQueryTests", Guid.NewGuid().ToString("N"));

    public ProjectHistoryQueryTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        Directory.CreateDirectory(_managedRootsParent);
    }

    public void Dispose()
    {
        try { Directory.Delete(_managedRootsParent, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void BeforeAnythingHappenedTheHistoryIsEmpty()
    {
        var fwDataPath = _pristine.CopyProjectFile();

        var outcome = ProjectHistoryQuery.Query(new ProjectHistoryRequest(fwDataPath));

        Assert.True(outcome.Succeeded);
        Assert.Empty(outcome.Value!.Entries);
    }

    [Fact]
    public void OneBaselineEntryAndOneAssessmentEntryPerRun_NewestFirst()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var managedRoot = NewManagedRoot();

        var first = AssessCommand.Run(
            new AssessRequest(fwDataPath, PastedWord), managedRoot, NewAssessor(), NewInvoker(),
            onProgress: null, CancellationToken.None);
        Assert.True(first.Succeeded, first.Refusal?.Message);

        var second = AssessCommand.Run(
            new AssessRequest(fwDataPath, PastedWord), managedRoot, NewAssessor(), NewInvoker(),
            onProgress: null, CancellationToken.None);
        Assert.True(second.Succeeded, second.Refusal?.Message);

        var outcome = ProjectHistoryQuery.Query(new ProjectHistoryRequest(fwDataPath));

        Assert.True(outcome.Succeeded);
        var entries = outcome.Value!.Entries;
        Assert.Equal(3, entries.Count);
        Assert.Equal(
            [ProjectHistoryKind.Assessment, ProjectHistoryKind.Assessment, ProjectHistoryKind.Baseline],
            entries.Select(entry => entry.Kind));
        // Newest first: every entry's time is at or after the one that follows it.
        Assert.True(entries[0].At >= entries[1].At);
        Assert.True(entries[1].At >= entries[2].At);
        Assert.All(entries.Where(entry => entry.Kind == ProjectHistoryKind.Assessment),
            entry => Assert.StartsWith("Assessment: ", entry.Summary, StringComparison.Ordinal));
        Assert.Equal("Baseline captured.", entries.Single(entry => entry.Kind == ProjectHistoryKind.Baseline).Summary);
    }

    private FakeAssessor NewAssessor() => new("fake-assessor", CollectedKinds)
    {
        CaptureEvidence = (scope, candidate) => FakeAssessmentEvidence.Capture(_managedRootsParent, scope, candidate),
    };

    private static FakeInvoker NewInvoker() => new()
    {
        Respond = _ => new SIL.Motif.Host.PanGloss.PanGlossOutcome.Completed("fake stats", string.Empty, TimeSpan.Zero),
    };

    private string NewManagedRoot()
    {
        var root = Path.Combine(_managedRootsParent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
