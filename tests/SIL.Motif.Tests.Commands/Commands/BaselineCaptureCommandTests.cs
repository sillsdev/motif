using System;
using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using SIL.Motif.Commands;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Cli.Rendering;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Pins <see cref="BaselineCaptureCommand"/> over a real, file-backed seeded project: a first capture,
/// idempotent recapture of unchanged bytes, a genuinely new capture after a save, the lock-file
/// observation, and both renderings of the typed response.
/// </summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group0)]
public sealed class BaselineCaptureCommandTests : IDisposable
{
    private readonly PristineProjectFixture _pristine;
    private readonly string _managedRootsParent =
        Path.Combine(Path.GetTempPath(), "SIL.Motif.BaselineCaptureCommandTests", Guid.NewGuid().ToString("N"));

    public BaselineCaptureCommandTests(PristineProjectFixture pristine)
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
    public void FirstCaptureRecordsANewBaselineFromTheSavedFile()
    {
        var fwDataPath = _pristine.CopyProjectFile();

        var outcome = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), NewManagedRoot());

        Assert.True(outcome.Succeeded);
        var response = outcome.Value!;
        Assert.False(response.ReusedExistingBytes);
        Assert.False(response.FieldWorksHeldProject);
        Assert.True(File.Exists(response.FwDataPath));
        Assert.NotEqual(fwDataPath, response.FwDataPath);
        Assert.Equal(File.GetLastWriteTimeUtc(fwDataPath), response.SourceLastWriteUtc.UtcDateTime);
    }

    // The window lists Known projects from the same store the CLI fills, so capture itself must fill it.
    [Fact]
    public void ASuccessfulCaptureRecordsTheProjectAsKnownUnderTheManagedRoot()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var managedRoot = NewManagedRoot();
        Assert.Empty(KnownProjectsQuery.List(managedRoot));

        var outcome = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), managedRoot);

        Assert.True(outcome.Succeeded);
        var known = Assert.Single(KnownProjectsQuery.List(managedRoot));
        Assert.Equal(Path.GetFullPath(fwDataPath), known.FullFwDataPath);
        Assert.Equal(outcome.Value!.Token.ProjectIdentity, known.ProjectIdentity);
        Assert.NotNull(known.WordformCount);
        Assert.Equal("Not parsed yet", known.StateText);
    }

    [Fact]
    public void AReplacedProjectKeepsItsSavedIdentityAndDoesNotShowThePreviousBaselineCounts()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var managedRoot = NewManagedRoot();
        var outcome = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), managedRoot);
        Assert.True(outcome.Succeeded);
        var replacementIdentity = Guid.NewGuid();
        var document = XDocument.Load(fwDataPath);
        var languageProject = document.Descendants("rt").Single(element =>
            element.Attribute("class")?.Value == "LangProject");
        languageProject.SetAttributeValue("guid", replacementIdentity.ToString("D"));
        document.Save(fwDataPath);

        var known = Assert.Single(KnownProjectsQuery.List(managedRoot));

        Assert.Equal(replacementIdentity.ToString("D"), known.ProjectIdentity);
        Assert.Null(known.WordformCount);
        Assert.Null(known.StateText);
    }

    [Fact]
    public void ACorruptBaselineTimestampLeavesRecentMetadataEmpty()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var managedRoot = NewManagedRoot();
        var outcome = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), managedRoot);
        Assert.True(outcome.Succeeded);
        var locator = new ProjectLocator(Path.GetFullPath(fwDataPath), Path.GetFileNameWithoutExtension(fwDataPath));
        var catalog = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, MotifProductVersion.Current);
        using (var database = catalog.OpenOwned(locator))
        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE Baselines SET PublishedUtc = 'invalid timestamp';";
            command.ExecuteNonQuery();
        }

        var known = Assert.Single(KnownProjectsQuery.List(managedRoot));

        Assert.Equal(outcome.Value!.Token.ProjectIdentity, known.ProjectIdentity);
        Assert.Null(known.WordformCount);
        Assert.Null(known.StateText);
    }

    [Fact]
    public void KnownProjectStateShowsChangesNotAppliedYetWhenTheProjectStoreHasOperations()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var managedRoot = NewManagedRoot();
        var outcome = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), managedRoot);
        Assert.True(outcome.Succeeded);
        var fullPath = Path.GetFullPath(fwDataPath);
        var project = new ProjectLocator(fullPath, Path.GetFileNameWithoutExtension(fullPath));
        var catalog = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, MotifProductVersion.Current);
        using (var database = catalog.OpenOwned(project))
        {
            var proposalId = CanonicalId.FromGuid(Guid.NewGuid());
            var draft = new DraftDocument
            {
                ProposalId = proposalId.Value,
                Operations = [new DraftOperation { OperationId = "operation-1", Kind = "lexical/setGloss" }],
            };
            new ProposalRepository(database).CreateDraft(PendingChanges.DraftName, proposalId,
                JsonSerializer.Serialize(draft), ProposalRoles.PendingChanges);
        }

        var known = Assert.Single(KnownProjectsQuery.List(managedRoot));

        Assert.Equal("Changes not applied yet", known.StateText);
    }

    [Fact]
    public void ARecentProjectWithASaveAfterItsBaselineNeedsRefreshDespiteAnAssessmentPointer()
    {
        var path = _pristine.CopyProjectFile();
        var root = NewManagedRoot();
        var outcome = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root);
        Assert.True(outcome.Succeeded);
        var locator = new ProjectLocator(path, Path.GetFileNameWithoutExtension(path));
        using (var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, MotifProductVersion.Current)
                   .OpenOwned(locator))
        {
            var assessments = new AssessmentRepository(database);
            assessments.Record(new NewAssessmentRecord("recent-results", null, null, "pangloss", "parse-time",
                "{}", "sha256:scope", new SIL.Motif.Host.Assess.AssessmentSubject.Words(
                    SIL.Motif.Host.Corpus.Selection.Create("Default", ["one"]), "whitespace", "1"),
                JsonSerializer.Serialize(outcome.Value!.Token), null, null, "sha256:grammar",
                null, null, null, new AssessmentRecordMaterial.Words([], null, null, [])));
            assessments.PromoteToCurrent("recent-results");
        }
        Assert.Equal("Results available", Assert.Single(KnownProjectsQuery.List(root)).StateText);
        File.SetLastWriteTimeUtc(path, outcome.Value!.SourceLastWriteUtc.UtcDateTime.AddMinutes(1));

        var recent = Assert.Single(KnownProjectsQuery.List(root));

        Assert.Equal("Refresh needed", recent.StateText);
        Assert.NotNull(recent.WordformCount);
    }

    [Fact]
    public void RecapturingTheSameSavedBytesReusesTheExistingPublication()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var managedRoot = NewManagedRoot();

        var first = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), managedRoot);
        Assert.True(first.Succeeded);

        var second = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), managedRoot);

        Assert.True(second.Succeeded);
        Assert.True(second.Value!.ReusedExistingBytes);
        Assert.Equal(first.Value!.Token.BundleDigest, second.Value.Token.BundleDigest);
        Assert.Equal(first.Value.Token.SemanticSnapshotDigest, second.Value.Token.SemanticSnapshotDigest);
    }

    // The window holds whatever token a capture answers with, and an Assessment records the stored one.
    [Fact]
    public void RecapturingTheSameSavedBytesAnswersWithTheBaselineTheStoreKeepsCurrent()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var managedRoot = NewManagedRoot();
        var first = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), managedRoot);
        Assert.True(first.Succeeded);

        var second = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), managedRoot);

        Assert.True(second.Succeeded);
        var current = CurrentBaselineQuery.Query(new CurrentBaselineRequest(fwDataPath));
        Assert.True(current.Succeeded);
        Assert.Equal(current.Value!.Token, second.Value!.Token);
        Assert.Equal(first.Value!.Token, second.Value.Token);
    }

    [Fact]
    public void RecapturingAfterTheProjectIsSavedAgainProducesANewCapture()
    {
        using var cache = _pristine.NewScratch();
        var fwDataPath = cache.ProjectId.Path;
        var managedRoot = NewManagedRoot();

        var first = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), managedRoot);
        Assert.True(first.Succeeded);

        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor,
            () => cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create());
        new FwDataProjectLoader().Save(cache);

        var second = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), managedRoot);

        Assert.True(second.Succeeded);
        Assert.False(second.Value!.ReusedExistingBytes);
        Assert.NotEqual(first.Value!.Token.BundleDigest, second.Value.Token.BundleDigest);
        Assert.NotEqual(first.Value.Token.SemanticSnapshotDigest, second.Value.Token.SemanticSnapshotDigest);
    }

    [Fact]
    public void ReportsFieldWorksHoldsTheProjectWhenALockFileIsPresent()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        using var lockHandle = new FileStream(
            fwDataPath + ".lock", FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        var outcome = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), NewManagedRoot());

        Assert.True(outcome.Succeeded);
        Assert.True(outcome.Value!.FieldWorksHeldProject);
    }

    [Fact]
    public void ReportsFieldWorksDoesNotHoldTheProjectWhenNoLockFileExists()
    {
        var fwDataPath = _pristine.CopyProjectFile();

        var outcome = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), NewManagedRoot());

        Assert.True(outcome.Succeeded);
        Assert.False(outcome.Value!.FieldWorksHeldProject);
    }

    [WindowsFileLockFact]
    public void PublishedCaptureSucceedsWhenMachineRegistrationCannotOpen()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var managedRoot = NewManagedRoot();
        using var machineDb = new FileStream(
            Path.Combine(managedRoot, "motif.db"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);

        var outcome = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), managedRoot);

        Assert.True(outcome.Succeeded);
        Assert.NotNull(outcome.Value!.Token);
        Assert.NotNull(outcome.Value.RegistrationFailure);
        Assert.Contains("capture succeeded", outcome.Value.RegistrationFailure!.Message, StringComparison.Ordinal);
        Assert.Contains("motif.db", outcome.Value.RegistrationFailure!.Message, StringComparison.Ordinal);
        var rendered = CommandTextRenderer.Render(outcome, asJson: false);
        Assert.Contains("Warning:", rendered.Output, StringComparison.Ordinal);
    }

    [WindowsFileLockFact]
    public void BusyPublicationPreservesItsOriginalBaselineFactsAndSharingDiagnostics()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var managedRoot = NewManagedRoot();
        var incoming = Path.Combine(managedRoot, "baselines", ".incoming-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(incoming);
        var heldPath = Path.Combine(incoming, "held.txt");
        File.WriteAllText(heldPath, "held");

        var held = new FileStream(heldPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        SIL.Motif.Contract.Commands.CommandOutcome<BaselineCaptureResponse> outcome;
        try
        {
            outcome = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), managedRoot);
        }
        finally
        {
            held.Dispose();
        }

        var refusal = outcome.Refusal!;
        Assert.Equal("baseline.busy", refusal.Code);
        Assert.Equal(FailureReason.Busy, refusal.Reason);
        Assert.Contains(heldPath, refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(fwDataPath, refusal.Facts["projectPath"]);
        Assert.False(refusal.Facts.ContainsKey("fwDataPath"));
        Assert.Equal("incoming-reclamation", refusal.Facts["baselinePublicationPhase"]);
        Assert.Equal("0x80070020", refusal.Facts["exceptionHResult"]);
        Assert.Equal(typeof(IOException).FullName, refusal.Facts["exceptionType"]);
        Assert.Equal(4, refusal.Facts.Count);
    }

    [Fact]
    public void HumanRenderingNamesTheFreshnessAsOfFieldWorksLastSave()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var outcome = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), NewManagedRoot());
        Assert.True(outcome.Succeeded);

        var rendered = CommandTextRenderer.Render(outcome, asJson: false);

        Assert.Equal(0, rendered.ExitCode);
        Assert.Contains("as of FieldWorks' last save", rendered.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonRendersAndBindsBackToTheTypedResponse()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var outcome = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), NewManagedRoot());
        Assert.True(outcome.Succeeded);

        var rendered = CommandTextRenderer.Render(outcome, asJson: true);

        Assert.Equal(0, rendered.ExitCode);
        var bound = ProjectionJson.Deserialize<BaselineCaptureResponse>(rendered.Output);
        Assert.NotNull(bound);
        Assert.Equal(outcome.Value!.Token.BundleDigest, bound!.Token.BundleDigest);
        Assert.Equal(outcome.Value.FwDataPath, bound.FwDataPath);
        Assert.Equal(outcome.Value.ReusedExistingBytes, bound.ReusedExistingBytes);
    }

    private string NewManagedRoot()
    {
        var root = Path.Combine(_managedRootsParent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
