using System.Diagnostics;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Runner.AppliedLog;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Snapshotting;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Commands;

/// <summary>Opt-in corpus authoring measurement; set MOTIF_TEXT_SCALE=1 to run it.</summary>
[Collection(LcmCacheParallelCollections.Group2)]
public sealed class AuthorTextScaleTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    private readonly string _measurementPath = Path.Combine(AppContext.BaseDirectory,
        "text-authoring-scale-" + Guid.NewGuid().ToString("N") + ".log");

    [TextScaleFact]
    [Trait("Category", "TextAuthoringScale")]
    public void DraftDryRunAndApplyATwoHundredFiftyTwoThousandWordText()
    {
        string path, ws;
        using (var seed = pristine.NewScratch())
        {
            new FwDataProjectLoader().Save(seed);
            path = seed.ProjectId.Path;
            ws = seed.WritingSystemFactory.GetStrFromWs(seed.DefaultVernWs);
        }
        var input = JsonSerializer.Serialize(new
        {
            title = "Synthetic corpus", ws,
            paragraphs = Enumerable.Range(0, 2000).Select(paragraph => new
            {
                segments = Enumerable.Range(0, 18).Select(sentence => new
                {
                    text = string.Join(" ", Enumerable.Range(0, 7).Select(word =>
                        "word" + ((paragraph * 126 + sentence * 7 + word) % 1000))) + ".",
                    freeTranslation = "Synthetic translation.",
                    notes = new[] { "Synthetic note." },
                }).ToArray(),
            }).ToArray(),
        });
        const string draftName = "corpus-scale";
        Assert.True(ProposalCommands.New(new(path, "0.1.0", draftName, "Synthetic corpus measurement")).Succeeded);
        var staged = Measure("Draft", () => ProposalCommands.ComposeAuthorText(new(path, "0.1.0", draftName, input)));
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        string proposalJson;
        using (var database = ProjectMotifDatabase.Open(path))
        {
            var draftJson = new ProposalRepository(database).GetDraft(draftName).ProposalJson!;
            proposalJson = ProposalCommands.BuildProposalJson(ProposalCommands.DeserializeDraft(draftJson));
            Log($"TEXT_SCALE inputBytes={System.Text.Encoding.UTF8.GetByteCount(input)} draftBytes={System.Text.Encoding.UTF8.GetByteCount(draftJson)} proposalBytes={System.Text.Encoding.UTF8.GetByteCount(proposalJson)}");
        }
        var proposal = ProposalJsonParser.Parse(proposalJson);
        using var cache = new FwDataProjectLoader().LoadScratchCache(path);
        var dryRun = Measure("DryRun", () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "motif-text-authoring-scale-" + Guid.NewGuid().ToString("N"));
            using var scratch = DryRunScratch.Adopt(new ScratchCacheFactory().CreateFromFileCopy(cache.ProjectId.Path, root),
                "corpus text measurement", onDisposed: () => Directory.Delete(root, true));
            return ProposalDryRunner.Run(scratch, PrerequisiteExecutionPlan.Create(proposal, [],
                ProjectAppliedLog.ReadAll(cache).Select(entry => entry.ProposalId).ToArray()));
        });
        var effect = dryRun.ExpectedEffects.Last();
        Assert.Equal("252000", effect.After["words"]);
        Assert.Equal("36000", effect.After["segments"]);
        Assert.Equal("2000", effect.After["paragraphs"]);
        var receipt = Measure("Apply", () =>
        {
            var watch = Stopwatch.StartNew();
            long lastMilliseconds = 0, lastAllocated = GC.GetTotalAllocatedBytes(false);
            return ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "tester", string.Empty, (_, operation) =>
            {
                var elapsed = watch.ElapsedMilliseconds;
                var allocated = GC.GetTotalAllocatedBytes(false);
                if (operation.Kind == LangProjectTextsOperationKinds.Create)
                    Log($"TEXT_SCALE textOperationMs={elapsed - lastMilliseconds} textOperationAllocatedBytes={allocated - lastAllocated}");
                lastMilliseconds = elapsed;
                lastAllocated = allocated;
            });
        });
        Assert.Equal(effect.After["structure"], receipt.ActualEffects.Last().After["structure"]);
        var textId = proposal.Operations.Last(op => op.Kind == LangProjectTextsOperationKinds.Create).EntityId!.Value.ToGuid();
        var text = cache.ServiceLocator.GetInstance<ITextRepository>().GetObject(textId);
        var summary = Measure("EffectSummary", () => TextOwningSnapshots.Summary(cache, text));
        Assert.Equal(effect.After["structure"], summary["structure"]);
        Assert.Equal(252000, text.ContentsOA.ParagraphsOS.OfType<IStTxtPara>().SelectMany(p => p.SegmentsOS)
            .Sum(s => s.AnalysesRS.Count(a => a.Wordform is not null)));
        Assert.All(text.ContentsOA.ParagraphsOS.OfType<IStTxtPara>().SelectMany(p => p.SegmentsOS), segment =>
        {
            Assert.Equal("Synthetic translation.", segment.FreeTranslation.get_String(cache.DefaultAnalWs).Text);
            Assert.Single(segment.NotesOS);
        });
    }

    private void Log(string line)
    {
        output.WriteLine(line);
        File.AppendAllText(_measurementPath, line + Environment.NewLine);
    }

    private T Measure<T>(string stage, Func<T> work)
    {
        var heldBefore = GC.GetTotalMemory(true);
        var allocatedBefore = GC.GetTotalAllocatedBytes(true);
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var workingBefore = process.WorkingSet64;
        Log($"TEXT_SCALE stage={stage} started");
        var watch = Stopwatch.StartNew();
        var result = work();
        watch.Stop();
        var heldAfter = GC.GetTotalMemory(true);
        process.Refresh();
        Log($"TEXT_SCALE stage={stage} elapsedMs={watch.ElapsedMilliseconds} allocatedBytes={GC.GetTotalAllocatedBytes(true) - allocatedBefore} heldBefore={heldBefore} heldAfter={heldAfter} workingBefore={workingBefore} workingAfter={process.WorkingSet64} processPeakWorking={process.PeakWorkingSet64}");
        return result;
    }
}

/// <summary>Keeps the corpus measurement out of ordinary and scheduled suites unless explicitly requested.</summary>
public sealed class TextScaleFactAttribute : FactAttribute
{
    public TextScaleFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("MOTIF_TEXT_SCALE") != "1") Skip = "Set MOTIF_TEXT_SCALE=1 to measure corpus text authoring.";
    }
}
