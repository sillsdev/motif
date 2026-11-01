using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Texts;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// The Default Selection Assess composes and the one read from the Baseline's project summary must list the same
/// words, or an Assessment is recorded under a Selection no reader asks for.
/// </summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
public sealed class TextCoverageSelectionParityTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-text-coverage-parity", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void SelectionComposerWordsEqualTheWordsResolvedFromTheSummaryForTheSameTexts()
    {
        using var cache = pristine.NewScratch();
        var text = SeedTextWithDigitsAndAnOtherWritingSystemWord(cache);
        new FwDataProjectLoader().Save(cache);
        var project = new ProjectLocator(cache.ProjectId.Path, cache.ProjectId.Name);
        using var database = MotifDatabase.OpenOwned(Path.Combine(_root, "parity.motif.db"), project,
            MotifSchema.CurrentSchema, new Version(1, 0));

        var composed = SelectionComposer.Compose(cache, new SelectionRequest(false, [text.Guid], [], false, null),
            new AssessmentRepository(database));
        var resolved = CurrentEvidenceQuery.ResolveSelection(ProjectSummaryReader.Read(cache),
            new NamedSelectionRecord("Default", [text.Guid], [], "2026-10-31T00:00:00Z", "2026-10-31T00:00:00Z",
                SelectionLimitPolicy.Estimated(StepCap.Default), 1));

        Assert.True(composed.Succeeded, composed.Refusal?.Message);
        Assert.True(resolved.Succeeded, resolved.Refusal?.Message);
        Assert.Equal(resolved.Value!.Selection.Words, composed.Value!.Selection.Words);
        Assert.Equal(resolved.Value.Selection.Sha256, composed.Value.Selection.Sha256);
        Assert.DoesNotContain("1990", composed.Value.Selection.Words);
        Assert.DoesNotContain("explanation", composed.Value.Selection.Words);
    }

    [Fact]
    public void AStoreUpgradedFromSchema56RecapturesItsBaselineSoAFreshAssessmentIsFoundByTheOverview()
    {
        using var cache = pristine.NewScratch();
        var text = SeedTextWithDigitsAndAnOtherWritingSystemWord(cache);
        new FwDataProjectLoader().Save(cache);
        var path = cache.ProjectId.Path;
        Assert.True(SelectionCommands.SetDefault(new SetDefaultSelectionRequest(path, "Default", [text.Guid], [])).Succeeded);
        var first = Assess(path);
        Assert.True(first.Succeeded, first.Refusal?.Message);

        // What a schema 56 Motif stored: a summary that listed digit-only tokens and other-writing-system words.
        var project = new ProjectLocator(Path.GetFullPath(path), Path.GetFileNameWithoutExtension(path));
        var storePath = ProjectDatabaseCatalog.DatabasePathFor(project);
        using (var store = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = storePath, Pooling = false }.ToString()))
        {
            store.Open();
            var summary = JsonNode.Parse(Scalar(store, "SELECT SummaryJson FROM BaselineSummaries;"))!.AsObject();
            var byWord = summary["Texts"]!.AsArray().Select(node => node!["OccurrencesByWord"]!.AsObject())
                .First(words => words.ContainsKey("motifa"));
            byWord["1990"] = 1;
            byWord["explanation"] = 1;
            using var update = store.CreateCommand();
            update.CommandText = "UPDATE BaselineSummaries SET SummaryJson = $json; PRAGMA user_version = 56;";
            update.Parameters.AddWithValue("$json", summary.ToJsonString());
            update.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        var second = Assess(path);

        Assert.True(second.Succeeded, second.Refusal?.Message);
        Assert.True(File.Exists(storePath + ".schema56.bak"));
        Assert.False(second.Value!.Baseline.ReusedExistingBytes);
        var overview = OverviewCommand.Overview(new OverviewRequest(path));
        Assert.True(overview.Succeeded, overview.Refusal?.Message);
        Assert.Contains(overview.Value!.AssessmentId, second.Value.AssessmentIds);
        Assert.Equal(3, overview.Value.SelectionWordCount);
    }

    private CommandOutcome<AssessCommandResponse> Assess(string path)
    {
        var managed = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(managed);
        var assessor = new FakeAssessor("fake-assessor", [AssessmentKind.ParseTime, AssessmentKind.ObjectTiming])
        {
            CaptureEvidence = (scope, candidate) => FakeAssessmentEvidence.Capture(_root, scope, candidate),
        };
        var invoker = new FakeInvoker
        {
            Respond = _ => new SIL.Motif.Host.PanGloss.PanGlossOutcome.Completed("fake stats", string.Empty, TimeSpan.Zero),
        };
        return AssessCommand.Run(new AssessRequest(path), managed, assessor, invoker, null, CancellationToken.None);
    }

    private static string Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (string)command.ExecuteScalar()!;
    }

    // Two vernacular spellings of one wordform, a digit-only token and an analysis-only word.
    private static IText SeedTextWithDigitsAndAnOtherWritingSystemWord(LcmCache cache)
    {
        var services = cache.ServiceLocator;
        IText text = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            text = services.GetInstance<ITextFactory>().Create();
            text.Name.set_String(cache.DefaultAnalWs, "coverage");
            text.ContentsOA = services.GetInstance<IStTextFactory>().Create();
            var paragraph = services.GetInstance<IStTxtParaFactory>().Create();
            text.ContentsOA.ParagraphsOS.Add(paragraph);
            var segment = services.GetInstance<ISegmentFactory>().Create();
            paragraph.SegmentsOS.Add(segment);
            IWfiWordform Wordform(string form, bool vernacular) => services.GetInstance<IWfiWordformFactory>().Create(
                TsStringUtils.MakeString(form, vernacular ? cache.DefaultVernWs : cache.DefaultAnalWs));
            var both = Wordform("motifb", vernacular: true);
            var second = services.WritingSystems.VernacularWritingSystems.First(ws => ws.Handle != cache.DefaultVernWs);
            both.Form.set_String(second.Handle, "motifb-alternate");
            foreach (var wordform in new[]
                     {
                         Wordform("motifa", true), both, Wordform("1990", true), Wordform("explanation", false), both,
                     })
                segment.AnalysesRS.Add(wordform);
        });
        return text;
    }
}
