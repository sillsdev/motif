using System.Security.Cryptography;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Gdr;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Gdr;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>A query block's saved table (ADR 0005): staged only by a refresh, kept by edit and show, advice in the check, invisible to the example check.</summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class GdrSavedTableTests(PristineProjectFixture pristine)
{
    private static readonly DateOnly Day = new(2026, 11, 26);

    private const string Run = "run: motif query corpus forms --corpus words.tsv forms where 'form ~ \"w.*\"'";

    private static readonly string[] Block = ["```motif-query", "id: ws", "title: Words", Run, "```"];

    private static string Words(int count) => string.Concat(Enumerable.Range(1, count).Select(i => $"w{i:00}\t{i}\n"));

    [Fact]
    public void ARefreshStagesTheFirstTenRowsWithTheCaptionAndApplyShowAndEditLeaveTheTableAttached()
    {
        var path = ProjectWithBlock(14);
        var digest = Show(path, "GDR-2").Baseline!.BundleDigest;
        var before = Fingerprint(path);

        var refreshed = GdrTableCommands.Refresh(new GdrRefreshTablesRequest(path, "GDR-2"), today: Day);

        Assert.True(refreshed.Succeeded, refreshed.Refusal?.Message);
        var table = Assert.Single(Assert.Single(refreshed.Value!.Sections).Tables);
        Assert.Equal(("saved", 10, 14L), (table.Outcome, table.RowsSaved, table.RowsInAll));
        Assert.Equal(before, Fingerprint(path, includePending: false));
        Assert.Equal(5, Show(path, "GDR-2").Discussion.Count);

        var caption = $"Saved 2026-11-26 from Baseline {GdrSavedTables.ShortBaselineId(digest)}; 14 rows in all; Motif recomputes this when shown.";
        var staged = Texts(GdrQuery.Show(new GdrShowRequest(path, "GDR-2", AsOf: refreshed.Value.DraftName)).Value!);
        Assert.Equal(Block.Concat([caption, "form\ttokens"]).Concat(Enumerable.Range(5, 10).Reverse().Select(i => $"w{i:00}\t{i}")).ToArray(), staged);

        Apply(path);
        Capture(path);
        var shown = Show(path, "GDR-2");
        var site = Assert.Single(GdrSavedTables.Sites(GdrMarkdownView.ProjectParagraphs(shown)));
        Assert.Equal((5, 12), (site.TableFirst, site.TableCount));
        Assert.Equal(new string?[] { "Table Header", "Table Row" }, new[] { shown.Discussion[6].Style, shown.Discussion[7].Style });

        var markdown = Markdown(path, "GDR-2");
        Assert.Contains(caption + "\n\n| form | tokens |", markdown.Replace("\\", "", StringComparison.Ordinal), StringComparison.Ordinal);
        var same = GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-2", markdown));
        Assert.True(same.Succeeded, same.Refusal?.Message + "\n---\n" + markdown);
        Assert.Equal(0, same.Value!.OperationCount);
        Assert.Empty(same.Value.Warnings);

        var edited = GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-2", markdown.TrimEnd() + "\n\nA closing paragraph.\n"));
        Assert.True(edited.Succeeded, edited.Refusal?.Message);
        Apply(path);
        Capture(path);
        var after = Show(path, "GDR-2");
        Assert.Equal(Texts(shown).Append("A closing paragraph.").ToArray(), Texts(after));
        Assert.True(Assert.Single(GdrSavedTables.Sites(GdrMarkdownView.ProjectParagraphs(after))).HasTable);
    }

    [Fact]
    public void AnUnchangedTableIsNotRestagedAndAChangedOneIsWithItsOwnCount()
    {
        var path = ProjectWithBlock(3);
        Assert.Equal("saved", Assert.Single(Refresh(path).Tables).Outcome);
        Apply(path);
        Capture(path);

        var same = GdrTableCommands.Refresh(new GdrRefreshTablesRequest(path, "GDR-2"), today: Day.AddDays(1));
        Assert.True(same.Succeeded, same.Refusal?.Message);
        Assert.Equal("unchanged", Assert.Single(Assert.Single(same.Value!.Sections).Tables).Outcome);
        Assert.Null(same.Value.DraftName);

        File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!, "words.tsv"), Words(2));
        var again = GdrTableCommands.Refresh(new GdrRefreshTablesRequest(path, "GDR-2"), today: Day.AddDays(1));
        Assert.True(again.Succeeded, again.Refusal?.Message);
        Assert.Equal("saved", Assert.Single(Assert.Single(again.Value!.Sections).Tables).Outcome);
        Assert.Contains("2 rows in all", Texts(GdrQuery.Show(new GdrShowRequest(path, "GDR-2", AsOf: again.Value.DraftName)).Value!).Single(text => text.StartsWith("Saved ", StringComparison.Ordinal)));
    }

    [Fact]
    public void AHandEditedTableIsIgnoredByGuideEditAndOverwrittenAtTheNextRefresh()
    {
        var path = ProjectWithBlock(3, seeded: ["Saved 2026-01-01 from Baseline 000000000000; 2 rows in all; Motif recomputes this when shown.", "form\ttokens", "typed by hand\t99"]);
        var markdown = Markdown(path, "GDR-2");

        var edit = GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-2", markdown.Replace("typed by hand", "typed again", StringComparison.Ordinal)));
        Assert.True(edit.Succeeded, edit.Refusal?.Message);
        Assert.Equal(0, edit.Value!.OperationCount);
        Assert.Contains("not authored text", Assert.Single(edit.Value.Warnings), StringComparison.Ordinal);

        var refreshed = Refresh(path);
        Assert.Equal("saved", Assert.Single(refreshed.Tables).Outcome);
        Apply(path);
        Capture(path);
        var rows = Texts(Show(path, "GDR-2"));
        Assert.DoesNotContain(rows, row => row.Contains("typed", StringComparison.Ordinal));
        Assert.Equal(["w03\t3", "w02\t2", "w01\t1"], rows[^3..]);
    }

    [Fact]
    public void ABlockTheEditRemovesTakesItsTableAndATableTypedInIsNotKept()
    {
        var path = ProjectWithBlock(3, seeded: ["Saved 2026-01-01 from Baseline 000000000000; 3 rows in all; Motif recomputes this when shown.", "form\ttokens", "w01\t1"]);
        var markdown = Markdown(path, "GDR-2");
        Assert.Contains("```motif-query", markdown, StringComparison.Ordinal);

        var edit = GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-2", "Only prose now.\n"));

        Assert.True(edit.Succeeded, edit.Refusal?.Message + "\n---\n" + markdown);
        Assert.Equal(["Only prose now."], Texts(GdrQuery.Show(new GdrShowRequest(path, "GDR-2", AsOf: edit.Value!.DraftName)).Value!));
    }

    [Fact]
    public void TheCheckAdvisesOnATableFromAnotherBaselineAndOnRowsThatDifferFromALiveRun()
    {
        var path = ProjectWithBlock(3, seeded: ["Saved 2026-01-01 from Baseline 000000000000; 2 rows in all; Motif recomputes this when shown.", "form\ttokens", "w01\t1", "w02\t2"]);
        var before = Fingerprint(path);

        var checkedGuide = GdrQuery.Check(new GdrCheckRequest(path));

        var finding = Assert.Single(checkedGuide.Value!.Findings, item => item.Code == GdrSyncCodes.SavedTableOutOfDate);
        Assert.Equal(("GDR-2", 1), (finding.Gdr, finding.Count));
        Assert.Contains("rows differ from a live run", Assert.Single(finding.Subjects), StringComparison.Ordinal);
        Assert.Equal(before, Fingerprint(path));
        Assert.Single(checkedGuide.Value.QueryBlocks!);

        var noRun = GdrQuery.Check(new GdrCheckRequest(path, RunBlocks: false));
        Assert.DoesNotContain(noRun.Value!.Findings, item => item.Code == GdrSyncCodes.SavedTableOutOfDate);

        var digest = checkedGuide.Value.Baseline!.BundleDigest;
        var source = new GdrSavedTableSource("GDR-2", new GuideQueryDefinition("ws", "Words", Run["run: ".Length..], [], null, new GuideBlockSpan(0, 0, 1, 1, 0, 0)),
            new GdrSavedTableCaption("2026-11-26", GdrSavedTables.ShortBaselineId(digest), false, 3), ["form", "tokens"],
            [["w03", "3"], ["w02", "2"], ["w01", "1"]]);
        Assert.Empty(GdrSavedTableAdvice.Findings([source], digest, Live(path)));
        Assert.Empty(GdrSavedTableAdvice.Findings([source with { Caption = source.Caption with { BaselineId = "000000000000" } }], digest, Live(path)));
        var cold = source with { Query = source.Query with { Run = "motif query corpus forms --corpus words.tsv --grammar grammar.json forms where 'readings > 0'" } };
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!, "grammar.json"), "grammar");
        Assert.Empty(GdrSavedTableAdvice.Findings([cold], digest, Live(path)));
        Assert.Contains("not the current one", Assert.Single(Assert.Single(GdrSavedTableAdvice.Findings(
            [cold with { Caption = cold.Caption with { BaselineId = "000000000000" } }], digest, Live(path))).Subjects), StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!, "words.tsv"), Words(4));
        var changed = Assert.Single(GdrSavedTableAdvice.Findings([source], digest, Live(path)));
        Assert.Contains("rows differ from a live run", Assert.Single(changed.Subjects), StringComparison.Ordinal);
    }

    [Fact]
    public void TheExampleCheckDoesNotReadASavedTable()
    {
        var path = ProjectWithGdrs(cache =>
        {
            HeadingStyles(cache);
            SeededGdrs.SeedTableStyles(cache);
            var analysis = cache.DefaultAnalWs;
            SeedLines(cache, "GDR-2", [("Examples", analysis, "Heading 3"), .. Block.Select(line => (line, analysis, (string?)null)),
                ("Saved 2026-01-01 from Baseline 000000000000; 1 rows in all; Motif recomputes this when shown.", analysis, null),
                ("form\ttokens", analysis, SIL.Motif.Host.RichText.MotifMarkdownStyles.TableHeader),
                ("tabledword\t5", cache.DefaultVernWs, SIL.Motif.Host.RichText.MotifMarkdownStyles.TableRow),
                ("proseword", cache.DefaultVernWs, null)]);
        });

        var checkedGuide = GdrQuery.Check(new GdrCheckRequest(path));

        Assert.Equal(1, checkedGuide.Value!.Counts.Examples);
    }

    [Fact]
    public void AnOtherKindOfRunLineIsSkippedAndKeepsWhatItHas()
    {
        var path = ProjectWithBlock(3, run: "run: motif query forms");

        var refreshed = Refresh(path);

        var table = Assert.Single(refreshed.Tables);
        Assert.Equal("skipped", table.Outcome);
        Assert.Contains("corpus forms", table.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AReleaseInTheBackgroundNeverWaitsForALoadAndStillClosesTheProject()
    {
        var path = ProjectWithBlock(3);
        using var session = new ProjectSession();
        var inside = new ManualResetEventSlim();
        var go = new ManualResetEventSlim();
        var load = Task.Run(() => MorphCatalog.TryLoad(path, null, out _, _ => { inside.Set(); go.Wait(); }, session));
        Assert.True(inside.Wait(TimeSpan.FromSeconds(60)));
        var copy = session.CopyDirectoryUnlocked;
        var before = session.Epoch;

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var released = session.ReleaseInBackground();

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2));
        Assert.Equal(before + 1, session.Epoch);
        go.Set();
        Assert.NotNull(await load);
        await released;
        Assert.False(Directory.Exists(copy));
        Assert.Null(session.CopyDirectory);
    }

    [Fact]
    public void ATimedOutBlocksLateRunIsNotKeptForTheAdvice()
    {
        var gdr = "GDR-2";
        var query = new GuideQueryDefinition("slow", "Slow", Run["run: ".Length..], [], null, new GuideBlockSpan(0, 0, 1, 1, 0, 0));
        var source = new GdrQueryBlockSource(gdr, null, query, []);
        var evidence = new Dictionary<string, GuideEvidenceResult>();
        var release = new ManualResetEventSlim();
        var table = new GuideEvidenceTable(["form", "tokens"], [["a", "1"]], "1 word type", true, null, 1, 1, 0);

        var checkedBlocks = GdrQueryBlockCheck.Check([source], "/nowhere/p.fwdata", true, TimeSpan.FromMilliseconds(200), null,
            (_, _, _) => { release.Wait(); return new GuideEvidenceResult(GuideEvidenceStatus.Ready, table, "x", null, null, 1, new Dictionary<string, double>()); },
            evidence);
        release.Set();
        Thread.Sleep(300);

        Assert.Equal(GdrQueryBlockStatus.Failed, Assert.Single(checkedBlocks.Runs).Status);
        Assert.Empty(evidence);
    }

    private static Func<GdrSavedTableSource, GuideEvidenceResult?> Live(string path) =>
        table => GdrTableCommands.LiveTable(table.Query, path, null, CancellationToken.None);

    [Fact]
    public void TwoBlocksOfOneProjectLoadItOnceAndAChangedProjectLoadsAgain()
    {
        var path = ProjectWithBlock(3);
        var other = ProjectWithBlock(3);
        using var session = new ProjectSession();
        var seen = 0;

        Assert.NotNull(MorphCatalog.TryLoad(path, null, out var failure, _ => seen++, session));
        Assert.NotNull(MorphCatalog.TryLoad(path, null, out failure, _ => seen++, session));
        Assert.Null(failure);
        Assert.Equal((1, 2), (session.Loads, seen));

        File.AppendAllText(path, " ");
        Assert.NotNull(MorphCatalog.TryLoad(path, null, out failure, null, session));
        Assert.Equal(2, session.Loads);
        Assert.NotNull(MorphCatalog.TryLoad(other, null, out failure, null, session));
        Assert.Equal(3, session.Loads);
    }

    [Fact]
    public void TheSessionDeletesItsProjectCopyWhenReleasedDisposedOrIdle()
    {
        var path = ProjectWithBlock(3);
        using var session = new ProjectSession();
        Assert.NotNull(MorphCatalog.TryLoad(path, null, out _, null, session));
        var copy = session.CopyDirectory;
        Assert.True(Directory.Exists(copy));
        session.Release();
        Assert.False(Directory.Exists(copy));
        Assert.Null(session.CopyDirectory);

        var disposed = new ProjectSession();
        Assert.NotNull(MorphCatalog.TryLoad(path, null, out _, null, disposed));
        copy = disposed.CopyDirectory;
        Assert.True(Directory.Exists(copy));
        disposed.Dispose();
        Assert.False(Directory.Exists(copy));

        using var idle = new ProjectSession(TimeSpan.FromMilliseconds(200));
        Assert.NotNull(MorphCatalog.TryLoad(path, null, out _, null, idle));
        copy = idle.CopyDirectory;
        Assert.True(Directory.Exists(copy));
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (idle.CopyDirectory is not null && DateTime.UtcNow < deadline) Thread.Sleep(50);
        Assert.Null(idle.CopyDirectory);
        Assert.False(Directory.Exists(copy));
    }

    [Fact]
    public void TextTypedBetweenABlockAndItsTableDoesNotLeaveASecondCopyAndTheCheckReadsNone()
    {
        var path = ProjectWithBlock(3, seeded: ["Saved 2026-01-01 from Baseline 000000000000; 1 rows in all; Motif recomputes this when shown.", "form\ttokens", "w01\t1"]);
        var markdown = Markdown(path, "GDR-2");
        var fence = markdown.IndexOf("```motif-query", StringComparison.Ordinal);
        var close = markdown.IndexOf("```\n", fence + 3, StringComparison.Ordinal) + 4;
        var typed = markdown[..close] + "\nA sentence typed after the block.\n" + markdown[close..];

        var edit = GdrEditCommands.Edit(new GdrEditRequest(path, "GDR-2", typed));

        Assert.True(edit.Succeeded, edit.Refusal?.Message + "\n---\n" + typed);
        Assert.Contains("must follow its block directly", Assert.Single(edit.Value!.Warnings), StringComparison.Ordinal);
        var staged = Texts(GdrQuery.Show(new GdrShowRequest(path, "GDR-2", AsOf: edit.Value.DraftName)).Value!);
        Assert.Single(staged, text => text.StartsWith("Saved ", StringComparison.Ordinal));
        Assert.Single(staged, text => text == "form\ttokens");
        Assert.Equal("A sentence typed after the block.", staged[^1]);
        Assert.Equal("Saved", staged[5].Split(' ')[0]);

        var texts = new[] { "x", "Saved 2026-01-01 from Baseline 000000000000; 1 rows in all; Motif recomputes this when shown.", "form\ttokens", "orphan\t1", "prose" };
        var styles = new string?[] { null, null, "Table Header", "Table Row", null };
        Assert.Equal([1, 2, 3], GdrSavedTables.TableIndexes(texts, styles).Order());
    }

    private GdrRefreshedSection Refresh(string path)
    {
        var refreshed = GdrTableCommands.Refresh(new GdrRefreshTablesRequest(path, "GDR-2"), today: Day);
        Assert.True(refreshed.Succeeded, refreshed.Refusal?.Message);
        return Assert.Single(refreshed.Value!.Sections);
    }

    private string ProjectWithBlock(int words, string[]? seeded = null, string? run = null)
    {
        var path = ProjectWithGdrs(cache =>
        {
            if (seeded is not null) SeededGdrs.SeedTableStyles(cache);
            var analysis = cache.DefaultAnalWs;
            var lines = Block.Select(line => line == Run && run is not null ? run : line).Select(line => (line, analysis, (string?)null)).ToList();
            if (seeded is not null)
            {
                lines.Add((seeded[0], analysis, null));
                lines.Add((seeded[1], analysis, SIL.Motif.Host.RichText.MotifMarkdownStyles.TableHeader));
                lines.AddRange(seeded.Skip(2).Select(row => (row, analysis, (string?)SIL.Motif.Host.RichText.MotifMarkdownStyles.TableRow)));
            }
            SeedLines(cache, "GDR-2", lines.ToArray());
        });
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!, "words.tsv"), Words(words));
        return path;
    }

    private static string[] Texts(GdrShowResponse shown) => shown.Discussion.Select(paragraph => string.Concat(paragraph.Runs.Select(run => run.Text))).ToArray();

    private static string Fingerprint(string path, bool includePending = true) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) + (includePending ? PendingJson(path) : "");

    private static string PendingJson(string path) => ProjectStoreCommand.Run(path, MotifProductVersion.CurrentText,
        (database, _) => CommandOutcome<string>.Success(new ProposalRepository(database).FindPendingChangesDraft()?.ProposalJson ?? "")).Value!;

    private static void SeedLines(LcmCache cache, string abbreviation, params (string Text, int Ws, string? Style)[] lines) =>
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var record = SeededGdrs.Records(cache).PossibilitiesOS.Single(item => item.Abbreviation.get_String(cache.DefaultAnalWs).Text == abbreviation);
            record.DiscussionOA = cache.ServiceLocator.GetInstance<IStTextFactory>().Create();
            foreach (var (text, ws, style) in lines)
            {
                var paragraph = cache.ServiceLocator.GetInstance<IStTxtParaFactory>().Create();
                record.DiscussionOA.ParagraphsOS.Add(paragraph);
                var builder = TsStringUtils.MakeStrBldr();
                var props = TsStringUtils.MakePropsBldr();
                props.SetIntPropValues((int)FwTextPropType.ktptWs, 0, ws);
                builder.Replace(0, 0, text, props.GetTextProps());
                paragraph.Contents = builder.GetString();
                if (style is not null) paragraph.StyleName = style;
            }
        });

    private static void HeadingStyles(LcmCache cache) => NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
    {
        foreach (var name in new[] { "Heading 3" })
        {
            if (cache.LangProject.StylesOC.Any(style => style.Name == name)) continue;
            var style = cache.ServiceLocator.GetInstance<IStStyleFactory>().Create();
            cache.LangProject.StylesOC.Add(style);
            style.Name = name;
            style.Type = StyleType.kstParagraph;
        }
    });

    private static GdrShowResponse Show(string path, string id)
    {
        var shown = GdrQuery.Show(new GdrShowRequest(path, id));
        Assert.True(shown.Succeeded, shown.Refusal?.Message);
        return shown.Value!;
    }

    private static string Markdown(string path, string id) => new GdrMarkdownView(Show(path, id), Path.GetFileNameWithoutExtension(path)).Fields.Single(field => field.Name == "discussion").Body;

    private static void Capture(string path) => Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), RootFor(path)).Succeeded);

    private static void Apply(string path)
    {
        var runner = new InProcessRunnerLauncher(IsolatedRunner.Options(RootFor(path)));
        try
        {
            var applied = PendingChangesWorkflow.Apply(new ApplyPendingRequest(path, null, null), dryRunTimeout: TimeSpan.FromMinutes(2), runnerLauncher: runner);
            Assert.True(applied.Succeeded, applied.Refusal?.Code + ": " + applied.Refusal?.Message);
            Assert.True(applied.Value!.Applied, applied.Value.Summary);
        }
        finally
        {
            runner.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private string ProjectWithGdrs(Action<LcmCache>? alsoSeed = null)
    {
        using var cache = pristine.NewScratch();
        SeededGdrs.Seed(cache);
        alsoSeed?.Invoke(cache);
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), RootFor(path)).Succeeded);
        return path;
    }

    private static string RootFor(string projectPath)
    {
        var root = Path.Combine(Path.GetDirectoryName(projectPath)!, "gdr-table-root");
        Directory.CreateDirectory(root);
        return root;
    }
}
