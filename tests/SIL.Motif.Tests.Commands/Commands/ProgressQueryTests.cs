using System.Security.Cryptography;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.WritingSystems;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group0)]
public sealed class ProgressQueryTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-progress-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void MissingProjectionIsARefusal()
    {
        var path = pristine.CopyProjectFile();
        var result = ProgressQuery.Query(new ProgressRequest(path, new ProgressScope("texts")));
        Assert.Equal("progress.index-not-ready", result.Refusal?.Code);
    }

    [Fact]
    public void CapturePagesDeduplicatesScopeAndRejectsAnOldCursorWithoutChangingTheProject()
    {
        var path = pristine.CopyProjectFile();
        Guid first;
        Guid second;
        using (var cache = new FwDataProjectLoader().LoadScratchCache(path))
        {
            first = SeededProject.SeedText(cache, pristine.Seed).TextId;
            second = SeededProject.SeedText(cache, pristine.Seed).TextId;
            new FwDataProjectLoader().Save(cache);
        }
        Capture(path);
        var before = SHA256.HashData(File.ReadAllBytes(path));
        var all = new ProgressScope("texts");
        var summary = ProgressQuery.Query(new ProgressRequest(path, all));
        Assert.True(summary.Succeeded, summary.Refusal?.Message);
        Assert.Equal(2, summary.Value!.Totals!.Passages);
        Assert.Null(summary.Value.Totals.Tokens.ParserGuess);
        Assert.Null(summary.Value.Checked);
        Assert.False(summary.Value.ChangedSince.Complete);
        var one = ProgressQuery.Query(new ProgressRequest(path, new ProgressScope("texts", [first, first])));
        Assert.True(one.Succeeded, one.Refusal?.Message);
        Assert.Equal(1, one.Value!.Totals!.Passages);
        var page = ProgressQuery.Query(new ProgressRequest(path, all, "passages", 1));
        Assert.True(page.Succeeded, page.Refusal?.Message);
        Assert.Single(page.Value!.Rows);
        Assert.NotNull(page.Value.NextCursor);
        var next = ProgressQuery.Query(new ProgressRequest(path, all, "passages", 1, page.Value.NextCursor));
        Assert.True(next.Succeeded, next.Refusal?.Message);
        Assert.Single(next.Value!.Rows);
        Assert.NotEqual(page.Value.Rows[0].Id, next.Value.Rows[0].Id);
        Assert.Contains(next.Value.Rows[0].Id, new[] { first, second });
        var wrongFilter = ProgressQuery.Query(new ProgressRequest(path, all, "passages", 1,
            page.Value.NextCursor, "progress/other"));
        Assert.Equal("progress.invalid-cursor", wrongFilter.Refusal?.Code);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));

        Capture(path);
        var old = ProgressQuery.Query(new ProgressRequest(path, all, "passages", 1, page.Value.NextCursor));
        Assert.Equal("progress.revision-changed", old.Refusal?.Code);
    }

    [Fact]
    public void PendingPublicationNeverReturnsOldCountsAsCurrent()
    {
        var path = pristine.CopyProjectFile();
        Capture(path);
        var marked = ProjectStoreCommand.Run(path, MotifProductVersion.CurrentText, (database, _) =>
        {
            using var connection = database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE ProgressState SET PendingReceiptId = 'receipt/pending' WHERE Id = 1;";
            command.ExecuteNonQuery();
            return CommandOutcome<string>.Success("marked");
        });
        Assert.True(marked.Succeeded, marked.Refusal?.Message);
        var result = ProgressQuery.Query(new ProgressRequest(path, new ProgressScope("texts")));
        Assert.Equal("progress.index-not-ready", result.Refusal?.Code);
        Capture(path);
        Assert.True(ProgressQuery.Query(new ProgressRequest(path, new ProgressScope("texts"))).Succeeded);
    }

    [Fact]
    public void DurableApplyPublishesWithoutACurrentBaseline()
    {
        var path = pristine.CopyProjectFile();
        using (var cache = new FwDataProjectLoader().LoadScratchCache(path))
        {
            SeededProject.SeedText(cache, pristine.Seed);
            new FwDataProjectLoader().Save(cache);
        }
        Capture(path);
        var before = ProgressQuery.Query(new ProgressRequest(path, new ProgressScope("texts")));
        Assert.True(before.Succeeded, before.Refusal?.Message);
        var proposal = ApplyReconciliationTests.CreateMeasuredProposal(path, "progress-apply",
            CanonicalId.FromGuid(pristine.Seed.FirstSenseId).Value, SeededProject.FirstGloss + " progress");

        var applied = ProposalCommands.Apply(new ApplyRequest(path, "1.0", proposal, Force: true));

        Assert.True(applied.Succeeded, applied.Refusal?.Message);
        Assert.False(TextInventoryQuery.Query(new TextInventoryRequest(path)).Value!.HasBaseline);
        var after = ProgressQuery.Query(new ProgressRequest(path, new ProgressScope("texts")));
        Assert.True(after.Succeeded, after.Refusal?.Message);
        Assert.True(after.Value!.Freshness.JudgementsCurrent);
        Assert.NotEqual(before.Value!.Revision, after.Value.Revision);
        Assert.Equal(before.Value.Totals!.Tokens.Total, after.Value.Totals!.Tokens.Total);
    }

    [Fact]
    public void AppliedApprovalUpdatesTheChosenPlaces()
    {
        var path = pristine.CopyProjectFile();
        SeededText text;
        using (var cache = new FwDataProjectLoader().LoadScratchCache(path))
        {
            text = SeededProject.SeedText(cache, pristine.Seed);
            var ws = cache.ServiceLocator.WritingSystems.VernacularWritingSystems.First();
            ws.CharacterSets.Clear();
            var main = new CharacterSetDefinition("main");
            foreach (var character in "abcdefghijklmnopqrstuvwxyz") main.Characters.Add(character.ToString());
            ws.CharacterSets.Add(main);
            var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisRepository>().GetObject(text.ApprovedAnalysisId);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor,
                () => cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.noopinion));
            new FwDataProjectLoader().Save(cache);
        }
        var occurrenceReads = 0;
        try
        {
            TextWordsProjectionBuilder.BeforeOccurrenceRead.Value = () => occurrenceReads++;
            Capture(path);
            Assert.True(occurrenceReads > 0);
        }
        finally { TextWordsProjectionBuilder.BeforeOccurrenceRead.Value = null; }
        var before = ProgressQuery.Query(new ProgressRequest(path, new ProgressScope("texts")));
        Assert.True(before.Succeeded, before.Refusal?.Message);
        Assert.Equal(0, before.Value!.Totals!.Tokens.Approved);
        var pending = PendingChanges.Load(new PendingChangesRequest(path, "1.0"));
        Assert.True(pending.Succeeded, pending.Refusal?.Message);
        var staged = PendingChanges.Put(new PutPendingChangeRequest(path, "1.0", pending.Value!.Revision,
            new ChangeIntent("progress-approve", AnalysisChangeKinds.Approve,
                CanonicalId.FromGuid(text.AnalysedWordformId).Value, SeededProject.AnalysedWordForm,
                StoredAnalysisId: CanonicalId.FromGuid(text.ApprovedAnalysisId).Value)));
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        DraftRationale.Author(path, PendingChanges.DraftName, "Approve a reading", "The reading is supported.");
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(path, "1.0", PendingChanges.DraftName));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var measured = DryRunJobRunner.Run(path, "1.0", finalized.Value!.ProposalId);
        Assert.True(measured.Succeeded, measured.Refusal?.Message);
        occurrenceReads = 0;
        var fingerprints = 0;
        try
        {
            TextWordsProjectionBuilder.BeforeOccurrenceRead.Value = () => occurrenceReads++;
            ProgressSourceIdentity.BeforeHash.Value = () => fingerprints++;
            var applied = ProposalCommands.Apply(new ApplyRequest(path, "1.0", finalized.Value.ProposalId, Force: true));
            Assert.True(applied.Succeeded, applied.Refusal?.Message);
        }
        finally
        {
            TextWordsProjectionBuilder.BeforeOccurrenceRead.Value = null;
            ProgressSourceIdentity.BeforeHash.Value = null;
        }
        Assert.Equal(0, occurrenceReads);
        Assert.Equal(2, fingerprints);
        var after = ProgressQuery.Query(new ProgressRequest(path, new ProgressScope("texts")));
        Assert.True(after.Succeeded, after.Refusal?.Message);
        Assert.Equal(1, after.Value!.Totals!.Tokens.Approved);
        Assert.True(after.Value.Freshness.JudgementsCurrent);
    }

    [Fact]
    public void FreshApprovalRebuildsWhenProjectBytesChangedAtTheSameTimestamp()
    {
        var (path, proposal, _, otherWordform) = PrepareApproval();
        var capturedTime = File.GetLastWriteTimeUtc(path);
        var original = File.ReadAllBytes(path);
        var document = System.Text.Encoding.UTF8.GetString(original);
        var start = document.IndexOf($"<rt class=\"WfiWordform\" guid=\"{otherWordform:D}\"", StringComparison.OrdinalIgnoreCase);
        Assert.True(start >= 0);
        var end = document.IndexOf("</rt>", start, StringComparison.Ordinal);
        Assert.True(end > start);
        var record = document[start..end];
        var changed = record.Replace("<SpellingStatus val=\"0\" />", "<SpellingStatus val=\"2\" />", StringComparison.Ordinal);
        Assert.NotEqual(record, changed);
        var updated = System.Text.Encoding.UTF8.GetBytes(document[..start] + changed + document[end..]);
        Assert.Equal(original.Length, updated.Length);
        File.WriteAllBytes(path, updated);
        File.SetLastWriteTimeUtc(path, capturedTime);
        Assert.Equal(capturedTime, File.GetLastWriteTimeUtc(path));
        var applied = ProposalCommands.Apply(new ApplyRequest(path, "1.0", proposal, Force: true));
        Assert.True(applied.Succeeded, applied.Refusal?.Message);
        var result = ProgressQuery.Query(new ProgressRequest(path, new ProgressScope("texts")));
        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.True(result.Value!.Freshness.JudgementsCurrent);
        Assert.Equal(1, result.Value.Totals!.Tokens.Misspelled);
        Assert.Equal(1, result.Value.Totals.Tokens.Approved);
    }

    [Fact]
    public void ExemplarOnlyChangeIsStaleAndApprovalRebuildsEligibility()
    {
        var (path, proposal, writingSystem, _) = PrepareApproval();
        var before = ProgressQuery.Query(new ProgressRequest(path, new ProgressScope("texts")));
        Assert.Equal(2, before.Value!.Totals!.Tokens.Total);
        var fwdata = File.ReadAllBytes(path);
        var ldml = Path.Combine(Path.GetDirectoryName(path)!, "WritingSystemStore", writingSystem + ".ldml");
        var document = System.Xml.Linq.XDocument.Load(ldml);
        var exemplar = document.Descendants().Single(element => element.Name.LocalName == "exemplarCharacters" &&
            element.Name.NamespaceName.Length == 0 && element.Attribute("type") is null);
        exemplar.Value = "[b-z]";
        document.Save(ldml);
        File.SetLastWriteTimeUtc(ldml, File.GetLastWriteTimeUtc(ldml).AddSeconds(5));
        Assert.Equal(fwdata, File.ReadAllBytes(path));
        var stale = ProgressQuery.Query(new ProgressRequest(path, new ProgressScope("texts")));
        Assert.True(stale.Succeeded, stale.Refusal?.Message);
        Assert.False(stale.Value!.Freshness.JudgementsCurrent);
        var applied = ProposalCommands.Apply(new ApplyRequest(path, "1.0", proposal, Force: true));
        Assert.True(applied.Succeeded, applied.Refusal?.Message);
        var after = ProgressQuery.Query(new ProgressRequest(path, new ProgressScope("texts")));
        Assert.True(after.Succeeded, after.Refusal?.Message);
        Assert.True(after.Value!.Freshness.JudgementsCurrent);
        Assert.Equal(0, after.Value.Totals!.Tokens.Total);
        Assert.Equal(2, after.Value.Coverage.ExcludedPlaces);
    }

    [Fact]
    public void SidecarChangeBeforePublicationRemainsStaleUntilRefresh()
    {
        var (path, proposal, writingSystem, _) = PrepareApproval();
        try
        {
            ProposalCommands.BeforeProgressPublication.Value = () => ExcludeAFromExemplars(path, writingSystem);
            var applied = ProposalCommands.Apply(new ApplyRequest(path, "1.0", proposal, Force: true));
            Assert.True(applied.Succeeded, applied.Refusal?.Message);
        }
        finally { ProposalCommands.BeforeProgressPublication.Value = null; }
        var stale = ProgressQuery.Query(new ProgressRequest(path, new ProgressScope("texts")));
        Assert.True(!stale.Succeeded || !stale.Value!.Freshness.JudgementsCurrent);
        var reapplied = ProposalCommands.Apply(new ApplyRequest(path, "1.0", proposal, Force: true));
        Assert.True(reapplied.Succeeded, reapplied.Refusal?.Message);
        var refreshed = ProgressQuery.Query(new ProgressRequest(path, new ProgressScope("texts")));
        Assert.True(refreshed.Succeeded, refreshed.Refusal?.Message);
        Assert.True(refreshed.Value!.Freshness.JudgementsCurrent);
        Assert.Equal(0, refreshed.Value.Totals!.Tokens.Total);
        Assert.Equal(2, refreshed.Value.Coverage.ExcludedPlaces);
    }

    private static void ExcludeAFromExemplars(string path, string writingSystem)
    {
        var ldml = Path.Combine(Path.GetDirectoryName(path)!, "WritingSystemStore", writingSystem + ".ldml");
        var document = System.Xml.Linq.XDocument.Load(ldml);
        var exemplar = document.Descendants().Single(element => element.Name.LocalName == "exemplarCharacters" &&
            element.Name.NamespaceName.Length == 0 && element.Attribute("type") is null);
        exemplar.Value = "[b-z]";
        document.Save(ldml);
        File.SetLastWriteTimeUtc(ldml, File.GetLastWriteTimeUtc(ldml).AddSeconds(5));
    }

    private (string Path, string Proposal, string WritingSystem, Guid OtherWordform) PrepareApproval()
    {
        var path = pristine.CopyProjectFile();
        SeededText text;
        string writingSystem;
        using (var cache = new FwDataProjectLoader().LoadScratchCache(path))
        {
            text = SeededProject.SeedText(cache, pristine.Seed);
            var ws = cache.ServiceLocator.WritingSystems.VernacularWritingSystems.First();
            writingSystem = ws.Id;
            ws.CharacterSets.Clear();
            var main = new CharacterSetDefinition("main");
            foreach (var character in "abcdefghijklmnopqrstuvwxyz") main.Characters.Add(character.ToString());
            ws.CharacterSets.Add(main);
            var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisRepository>().GetObject(text.ApprovedAnalysisId);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor,
                () => cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.noopinion));
            new FwDataProjectLoader().Save(cache);
        }
        Capture(path);
        var pending = PendingChanges.Load(new PendingChangesRequest(path, "1.0"));
        Assert.True(pending.Succeeded, pending.Refusal?.Message);
        var staged = PendingChanges.Put(new PutPendingChangeRequest(path, "1.0", pending.Value!.Revision,
            new ChangeIntent("identity-approve", AnalysisChangeKinds.Approve,
                CanonicalId.FromGuid(text.AnalysedWordformId).Value, SeededProject.AnalysedWordForm,
                StoredAnalysisId: CanonicalId.FromGuid(text.ApprovedAnalysisId).Value)));
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        DraftRationale.Author(path, PendingChanges.DraftName, "Approve a reading", "The reading is supported.");
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(path, "1.0", PendingChanges.DraftName));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var measured = DryRunJobRunner.Run(path, "1.0", finalized.Value!.ProposalId);
        Assert.True(measured.Succeeded, measured.Refusal?.Message);
        return (path, finalized.Value.ProposalId, writingSystem, text.UnanalysedWordformId);
    }

    [Fact]
    public void ApprovalPublicationWritesAndSerializesOnlyChangedTexts()
    {
        var path = pristine.CopyProjectFile();
        var changedId = Guid.NewGuid();
        var unchangedId = Guid.NewGuid();
        var word = Guid.NewGuid();
        var incidence = new ProgressIncidence(word, null, "en", "word", "unmeasured", "parser-evidence-unavailable", 1);
        var first = new ProgressText(changedId, "Changed", [incidence], new Dictionary<string, int>());
        var second = new ProgressText(unchangedId, "Unchanged", [incidence], new Dictionary<string, int>());
        var original = new ProgressProjection([first, second]);
        var updated = new ProgressProjection([first with { Incidence = [incidence with { Bucket = "approved", Reason = null }] }, second]);
        var serialized = new System.Collections.Concurrent.ConcurrentBag<Guid>();
        var outcome = ProjectStoreCommand.Run(path, MotifProductVersion.CurrentText, (database, _) =>
        {
            using (var connection = database.OpenConnection())
            using (var transaction = connection.BeginTransaction())
            {
                ProgressRepository.Publish(connection, transaction, original, "content/test", "judgement/test", DateTimeOffset.UtcNow);
                transaction.Commit();
            }
            using (var connection = database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "CREATE TABLE Audit (Id TEXT); CREATE TRIGGER AuditUpdate AFTER UPDATE ON ProgressTexts " +
                    "BEGIN INSERT INTO Audit VALUES (new.TextId); END; " +
                    "UPDATE ProgressState SET PendingReceiptId = 'receipt/test' WHERE Id = 1;";
                command.ExecuteNonQuery();
            }
            try
            {
                ProgressRepository.TextSerialized.Value = id => serialized.Add(id);
                ProgressRepository.PublishApplied(database, updated, "receipt/test", DateTimeOffset.UtcNow, "content/test", original);
            }
            finally { ProgressRepository.TextSerialized.Value = null; }
            using var read = database.OpenConnection();
            using var query = read.CreateCommand();
            query.CommandText = "SELECT Id FROM Audit;";
            Assert.Equal(changedId.ToString("D"), query.ExecuteScalar());
            query.CommandText = "SELECT COUNT(*) FROM Audit;";
            Assert.Equal(1L, query.ExecuteScalar());
            return CommandOutcome<string>.Success("published");
        });
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal([changedId], serialized);
    }

    [Fact]
    public void AFailedSaveDoesNotPublishAProgressRevision()
    {
        var path = pristine.CopyProjectFile();
        Capture(path);
        var before = ProgressQuery.Query(new ProgressRequest(path, new ProgressScope("texts")));
        var proposal = ApplyReconciliationTests.CreateMeasuredProposal(path, "progress-unsaved",
            CanonicalId.FromGuid(pristine.Seed.FirstSenseId).Value, SeededProject.FirstGloss + " unsaved");
        var savedPause = ApplySaveCheck.Pause;
        try
        {
            ApplySaveCheck.Pause = TimeSpan.FromMilliseconds(10);
            ProposalCommands.ApplyLoaderOverride.Value = static () => new SaveThatDoesNotLand();
            var applied = ProposalCommands.Apply(new ApplyRequest(path, "1.0", proposal, Force: true));
            Assert.Equal("apply.not-saved", applied.Refusal?.Code);
        }
        finally
        {
            ProposalCommands.ApplyLoaderOverride.Value = null;
            ApplySaveCheck.Pause = savedPause;
        }
        var after = ProgressQuery.Query(new ProgressRequest(path, new ProgressScope("texts")));
        Assert.True(after.Succeeded, after.Refusal?.Message);
        Assert.Equal(before.Value!.Revision, after.Value!.Revision);
    }

    [Fact]
    public void ReceiptRecoveryPublishesOnceAfterAnInterruptedPublication()
    {
        var path = pristine.CopyProjectFile();
        SeededText text;
        using (var cache = new FwDataProjectLoader().LoadScratchCache(path))
        {
            text = SeededProject.SeedText(cache, pristine.Seed);
            var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisRepository>().GetObject(text.ApprovedAnalysisId);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor,
                () => cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.noopinion));
            new FwDataProjectLoader().Save(cache);
        }
        Capture(path);
        var pending = PendingChanges.Load(new PendingChangesRequest(path, "1.0"));
        Assert.True(pending.Succeeded, pending.Refusal?.Message);
        var staged = PendingChanges.Put(new PutPendingChangeRequest(path, "1.0", pending.Value!.Revision,
            new ChangeIntent("recovery-approve", AnalysisChangeKinds.Approve,
                CanonicalId.FromGuid(text.AnalysedWordformId).Value, SeededProject.AnalysedWordForm,
                StoredAnalysisId: CanonicalId.FromGuid(text.ApprovedAnalysisId).Value)));
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        DraftRationale.Author(path, PendingChanges.DraftName, "Approve a reading", "The reading is supported.");
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(path, "1.0", PendingChanges.DraftName));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var proposal = finalized.Value!.ProposalId;
        var measured = DryRunJobRunner.Run(path, "1.0", proposal);
        Assert.True(measured.Succeeded, measured.Refusal?.Message);
        try
        {
            ProposalCommands.BeforeProgressPublication.Value = () => throw new IOException("Publication interrupted.");
            var applied = ProposalCommands.Apply(new ApplyRequest(path, "1.0", proposal, Force: true));
            Assert.True(applied.Succeeded, applied.Refusal?.Message);
            Assert.Equal("progress.index-not-ready", ProgressQuery.Query(
                new ProgressRequest(path, new ProgressScope("texts"))).Refusal?.Code);
        }
        finally { ProposalCommands.BeforeProgressPublication.Value = null; }
        using (var cache = new FwDataProjectLoader().LoadScratchCache(path))
        {
            SeededProject.SeedText(cache, pristine.Seed);
            new FwDataProjectLoader().Save(cache);
        }
        var retry = ProposalCommands.Apply(new ApplyRequest(path, "1.0", proposal, Force: true));
        Assert.True(retry.Succeeded, retry.Refusal?.Message);
        Assert.True(retry.Value!.AlreadyApplied);
        var recovered = ProgressQuery.Query(new ProgressRequest(path, new ProgressScope("texts")));
        Assert.True(recovered.Succeeded, recovered.Refusal?.Message);
        Assert.True(recovered.Value!.Freshness.JudgementsCurrent);
        Assert.Equal(2, recovered.Value.Totals!.Passages);
        var again = ProposalCommands.Apply(new ApplyRequest(path, "1.0", proposal, Force: true));
        Assert.True(again.Succeeded, again.Refusal?.Message);
        var final = ProgressQuery.Query(new ProgressRequest(path, new ProgressScope("texts")));
        Assert.Equal(recovered.Value!.Revision, final.Value!.Revision);
    }

    private sealed class SaveThatDoesNotLand : FwDataProjectLoader
    {
        public override void Save(LcmCache cache) { }
    }

    private void Capture(string path)
    {
        Directory.CreateDirectory(_root);
        var result = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), _root);
        Assert.True(result.Succeeded, result.Refusal?.Message);
    }
}
