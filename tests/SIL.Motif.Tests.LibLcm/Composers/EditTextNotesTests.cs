using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Execution;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class EditTextNotesTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly IText _text;

    public EditTextNotesTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        var vern = _cache.DefaultVernWs;
        var anal = _cache.DefaultAnalWs;
        IText? text = null;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            text = _cache.ServiceLocator.GetInstance<ITextFactory>().Create();
            text.Name.set_String(anal, "Tracking");
            text.ContentsOA = _cache.ServiceLocator.GetInstance<IStTextFactory>().Create();
            foreach (var line in new[] { "alpha beta.", "gamma. alpha delta." })
            {
                var para = _cache.ServiceLocator.GetInstance<IStTxtParaFactory>().Create();
                text.ContentsOA.ParagraphsOS.Add(para);
                para.Contents = TsStringUtils.MakeString(line, vern);
                ParagraphParser.ParseParagraph(para);
            }
            var first = text.ContentsOA.ParagraphsOS.Cast<IStTxtPara>().First().SegmentsOS[0];
            foreach (var placeholder in new[] { "deferred: source review pending", "second" })
            {
                var note = _cache.ServiceLocator.GetInstance<INoteFactory>().Create();
                first.NotesOS.Add(note);
                note.Content.set_String(anal, TsStringUtils.MakeString(placeholder, anal));
            }
        });
        _text = text!;
    }

    public void Dispose() { if (!_cache.IsDisposed) _cache.Dispose(); }

    private ISegment[] Segments => _text.ContentsOA.ParagraphsOS.Cast<IStTxtPara>().SelectMany(p => p.SegmentsOS).ToArray();
    private string[] NoteTexts(ISegment segment) =>
        segment.NotesOS.Select(n => n.Content.get_String(_cache.DefaultAnalWs).Text!).ToArray();

    private static EditTextNotesIntent Intent(object value) =>
        EditTextNotesIntentParser.Parse(JsonSerializer.SerializeToElement(value));

    private static Proposal Proposal(IReadOnlyList<OperationEnvelope> operations) =>
        new(new Dictionary<string, string> { ["texts"] = "1.0" }, CanonicalId.Mint(), null, operations);

    [Fact]
    public void BulkEditsBySegmentAndWordReplaceNotesThroughDryRunAndApply()
    {
        var firstNoteId = Segments[0].NotesOS[0].Guid;
        var operations = EditTextNotesComposer.Build(_cache, Intent(new
        {
            edits = new object[]
            {
                new { text = "Tracking", segment = 1, note = "misspelling of alfa" },
                new { text = "Tracking", word = "gamma", note = "proper name" },
                new { text = "Tracking", word = "delta", note = "loan: English" },
            },
        }));
        Assert.Equal(3, operations.Count);
        Assert.All(operations, o => Assert.Equal(EditTextNotesOperationKinds.Set, o.Kind));
        var proposal = Proposal(operations);
        var dry = ScratchDryRun.Of(_cache, proposal);
        Assert.Equal(3, dry.ExpectedEffects.Count);
        Assert.Equal(["deferred: source review pending", "second"], NoteTexts(Segments[0]));
        ProposalApplier.Apply(_cache, proposal, dry.Anchor, "tester");
        Assert.Equal(["misspelling of alfa"], NoteTexts(Segments[0]));
        Assert.Equal(firstNoteId, Segments[0].NotesOS[0].Guid);
        Assert.Equal(["proper name"], NoteTexts(Segments[1]));
        Assert.Equal(["loan: English"], NoteTexts(Segments[2]));
        Assert.Equal("alpha beta.", _text.ContentsOA.ParagraphsOS.Cast<IStTxtPara>().First().Contents.Text);
    }

    [Fact]
    public void ANoteChangedAfterComposingRefusesAtDryRun()
    {
        var proposal = Proposal(EditTextNotesComposer.Build(_cache,
            Intent(new { edits = new[] { new { text = "Tracking", segment = 1, note = "x" } } })));
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            Segments[0].NotesOS[0].Content.set_String(_cache.DefaultAnalWs, "someone else edited"));
        var refusal = Assert.ThrowsAny<ProposalRefusalException>(() => ScratchDryRun.Of(_cache, proposal));
        Assert.Equal(RefusalCodes.TextNotesStaleSource, refusal.Code);
    }

    [Theory]
    [InlineData("Nope", 1, null)]
    [InlineData("Tracking", 99, null)]
    [InlineData("Tracking", null, "alpha")]
    [InlineData("Tracking", null, "missing")]
    public void UnresolvableOrAmbiguousTargetsAreRefused(string text, int? segment, string? word)
    {
        var edit = segment is { } number ? (object)new { text, segment = number, note = "x" } : new { text, word, note = "x" };
        Assert.ThrowsAny<ContractParseException>(() =>
            EditTextNotesComposer.Build(_cache, Intent(new { edits = new[] { edit } })));
    }

    [Fact]
    public void TwoEditsForOneSegmentAreRefused()
    {
        Assert.Throws<ContractParseException>(() => EditTextNotesComposer.Build(_cache, Intent(new
        {
            edits = new object[]
            {
                new { text = "Tracking", segment = 2, note = "a" },
                new { text = "Tracking", word = "gamma", note = "b" },
            },
        })));
    }

    [Fact]
    public void TheIntentIsClosedAndNamesExactlyOneLocator()
    {
        Assert.Throws<ContractParseException>(() => Intent(new { edits = new[] { new { text = "T", note = "x" } } }));
        Assert.Throws<ContractParseException>(() => Intent(new { edits = new[] { new { text = "T", segment = 1, word = "w", note = "x" } } }));
        Assert.Throws<ContractParseException>(() => Intent(new { edits = new[] { new { text = "T", segment = 0, note = "x" } } }));
        Assert.Throws<ContractParseException>(() => Intent(new { edits = new[] { new { text = "T", segment = 1, note = "x", extra = 1 } } }));
    }

    [Fact]
    public void TsvBecomesTheSameIntent()
    {
        var json = EditTextNotesIntentParser.FromTsv("Text\tLocator\tNote\nTracking\t1\tfirst\nTracking\tgamma\tproper name\n");
        var intent = EditTextNotesIntentParser.Parse(json);
        Assert.Equal(new EditTextNote("Tracking", 1, null, "first", "line 2"), intent.Edits[0]);
        Assert.Equal(new EditTextNote("Tracking", null, "gamma", "proper name", "line 3"), intent.Edits[1]);
        Assert.Throws<ContractParseException>(() => EditTextNotesIntentParser.FromTsv("Tracking\t1\n"));
    }

    [Fact]
    public void ContentInAnotherWritingSystemOfAnyNoteIsGuardedAndRemoved()
    {
        var other = _cache.ServiceLocator.WritingSystems.AnalysisWritingSystems
            .Select(w => w.Handle).Concat(_cache.ServiceLocator.WritingSystems.VernacularWritingSystems.Select(w => w.Handle))
            .First(h => h != _cache.DefaultAnalWs);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            Segments[0].NotesOS[1].Content.set_String(other, TsStringUtils.MakeString("elsewhere", other)));
        var proposal = Proposal(EditTextNotesComposer.Build(_cache,
            Intent(new { edits = new[] { new { text = "Tracking", segment = 1, note = "x" } } })));

        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            Segments[0].NotesOS[1].Content.set_String(other, TsStringUtils.MakeString("changed", other)));
        var refusal = Assert.ThrowsAny<ProposalRefusalException>(() => ScratchDryRun.Of(_cache, proposal));
        Assert.Equal(RefusalCodes.TextNotesStaleSource, refusal.Code);

        var fresh = Proposal(EditTextNotesComposer.Build(_cache,
            Intent(new { edits = new[] { new { text = "Tracking", segment = 1, note = "x" } } })));
        var dry = ScratchDryRun.Of(_cache, fresh);
        ProposalApplier.Apply(_cache, fresh, dry.Anchor, "tester");
        Assert.Single(Segments[0].NotesOS);
        Assert.Equal(["x"], NoteTexts(Segments[0]));
    }

    [Fact]
    public void EveryBadEditIsReportedOnceWithItsNumber()
    {
        var error = Assert.Throws<ContractParseException>(() => EditTextNotesComposer.Build(_cache, Intent(new
        {
            edits = new object[]
            {
                new { text = "Tracking", segment = 1, note = "ok" },
                new { text = "Nope", segment = 1, note = "x" },
                new { text = "Tracking", word = "alpha", note = "x" },
                new { text = "Tracking", segment = 99, note = "x" },
            },
        })));
        Assert.Contains("edit 2", error.Message);
        Assert.Contains("edit 3", error.Message);
        Assert.Contains("edit 4", error.Message);
        Assert.DoesNotContain("edit 1", error.Message);
    }

    [Fact]
    public void OnlyTheExactHeaderLineIsSkippedAndBadRowsNameTheirLines()
    {
        var kept = EditTextNotesIntentParser.Parse(EditTextNotesIntentParser.FromTsv("Text\t1\tnote\n"));
        Assert.Equal(new EditTextNote("Text", 1, null, "note", "line 1"), kept.Edits[0]);
        var skipped = EditTextNotesIntentParser.Parse(EditTextNotesIntentParser.FromTsv("text\tLOCATOR\tnote\nTracking\t1\ta\n"));
        Assert.Single(skipped.Edits);
        var error = Assert.Throws<ContractParseException>(() =>
            EditTextNotesIntentParser.FromTsv("Tracking\t1\ta\nbad\nTracking\tx\n"));
        Assert.Contains("line 2", error.Message);
        Assert.Contains("line 3", error.Message);
    }

    [Fact]
    public void EveryBadTsvRowAndShapeErrorIsReportedTogether()
    {
        var spaced = Assert.Throws<ContractParseException>(() =>
            EditTextNotesIntentParser.FromTsv("Tracking\t1\tgood\nTracking\t2\ttrailing \nTracking\t3\t\u00e9\nbad\n"));
        Assert.DoesNotContain("line 1", spaced.Message);
        Assert.Contains("line 2", spaced.Message);
        Assert.Contains("line 3", spaced.Message);
        Assert.Contains("line 4", spaced.Message);
        var shape = Assert.Throws<ContractParseException>(() => Intent(new
        {
            edits = new object[]
            {
                new { text = "Tracking", segment = 1, note = "ok " },
                new { text = "Tracking", note = "x" },
                new { text = "Tracking", segment = 0, note = "x" },
            },
        }));
        Assert.Contains("edit 1", shape.Message);
        Assert.Contains("edit 2", shape.Message);
        Assert.Contains("edit 3", shape.Message);
    }
}
