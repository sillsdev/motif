using System.Linq;
using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Model.AppliedLog;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.AppliedLog;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.LCModel;
using Xunit;

namespace SIL.Motif.Tests.Apply;

/// <summary>A Receipt records its mode and message, and an Apply with a note writes one Notebook record in the same unit of work.</summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group0)]
public sealed class ProposalApplierModeAndNoteTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly SeededProject _seed;

    public ProposalApplierModeAndNoteTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _seed = pristine.Seed;
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void TheReceiptRecordsAutonomousModeAndMessage_AndTheLogReadsThemBack()
    {
        var proposal = SetGloss("autonomous gloss");
        var receipt = ProposalApplier.Apply(_cache, proposal, ScratchDryRun.Of(_cache, proposal).Anchor, "",
            "Added the gloss the corpus supports", ApplyModes.Autonomous);

        Assert.Equal((ApplyModes.Autonomous, "Added the gloss the corpus supports"), (receipt.Mode, receipt.Message));
        var logged = Assert.Single(ProjectAppliedLog.ReadAll(_cache), e => e.ProposalId == proposal.ProposalId.ToGuid());
        Assert.Equal((ApplyModes.Autonomous, "Added the gloss the corpus supports"), (logged.Mode, logged.Description));
    }

    [Fact]
    public void TheDefaultModeIsPerson()
    {
        var proposal = SetGloss("person gloss");
        var receipt = ProposalApplier.Apply(_cache, proposal, ScratchDryRun.Of(_cache, proposal).Anchor, "", "label");
        Assert.Equal(ApplyModes.Person, receipt.Mode);
    }

    [Fact]
    public void AFormat1LogEntryReadsAsPerson_AndANewEntryRoundTripsItsMode()
    {
        var digest = new string('a', 64);
        Assert.True(AppliedLogFormat.TryParse($"Motif|1|20260724T055701Z||{digest}|old | label", Guid.NewGuid(),
            out var old, out _, out _));
        Assert.Equal((ApplyModes.Person, "old | label"), (old!.Mode, old.Description));

        var entry = new AppliedLogEntry(Guid.NewGuid(), AppliedLogFormat.CurrentFormatVersion, "20260724T055701Z", "",
            digest, "why | here", ApplyModes.Autonomous);
        Assert.True(AppliedLogFormat.TryParse(AppliedLogFormat.Format(entry), entry.ProposalId, out var parsed,
            out _, out _));
        Assert.Equal(entry, parsed);
        Assert.Throws<ArgumentException>(() => AppliedLogFormat.Format(entry with { Mode = "robot" }));
    }

    [Fact]
    public void ANoteWritesOneRecordWithTitleRationaleReceiptAndMessage_AndLeavesTheEffectDigestAlone()
    {
        var plain = SetGloss("plain gloss");
        var withNote = SetGloss("noted gloss");
        var plainReceipt = ProposalApplier.Apply(_cache, plain, ScratchDryRun.Of(_cache, plain).Anchor, "", "m");
        var recordsBefore = _cache.LangProject.ResearchNotebookOA.RecordsOC.Count;

        var receipt = ProposalApplier.Apply(_cache, withNote, ScratchDryRun.Of(_cache, withNote).Anchor, "",
            "Checked against the lexicon", ApplyModes.Autonomous,
            new ApplyNote("Gloss the sense", "# Why\n\nThe corpus attests it."));

        Assert.Single(receipt.ActualEffects);
        Assert.Equal(plainReceipt.ActualEffects.Count, receipt.ActualEffects.Count);
        Assert.Equal(recordsBefore + 1, _cache.LangProject.ResearchNotebookOA.RecordsOC.Count);
        var record = Assert.Single(_cache.LangProject.ResearchNotebookOA.RecordsOC, r => TitleOf(r) == "Gloss the sense");
        Assert.Equal("Gloss the sense", TitleOf(record));
        Assert.Equal(ApplyNoteWriter.RecordTypeName, record.TypeRA!.Name.AnalysisDefaultWritingSystem.Text);
        var paragraphs = record.DescriptionOA.ParagraphsOS.Cast<IStTxtPara>().Select(p => p.Contents.Text).ToArray();
        Assert.Contains("# Why", paragraphs);
        Assert.Contains("The corpus attests it.", paragraphs);
        Assert.Contains(paragraphs, p => p.StartsWith("Receipt: Proposal " + withNote.ProposalId.Value, StringComparison.Ordinal));
        Assert.Contains("Message: Checked against the lexicon", paragraphs);
    }

    [Fact]
    public void TheNoteRecordTypeIsCreatedOnFirstUseAndReused()
    {
        var first = SetGloss("one");
        var second = SetGloss("two");
        ProposalApplier.Apply(_cache, first, ScratchDryRun.Of(_cache, first).Anchor, "", "m", ApplyModes.Person,
            new ApplyNote("First", null));
        ProposalApplier.Apply(_cache, second, ScratchDryRun.Of(_cache, second).Anchor, "", "m", ApplyModes.Person,
            new ApplyNote("Second", null));

        var types = _cache.LangProject.ResearchNotebookOA.RecTypesOA.PossibilitiesOS
            .Where(t => t.Name.AnalysisDefaultWritingSystem.Text == ApplyNoteWriter.RecordTypeName).ToArray();
        Assert.Single(types);
        Assert.All(_cache.LangProject.ResearchNotebookOA.RecordsOC.Where(r => TitleOf(r) is "First" or "Second"),
            r => Assert.Same(types[0], r.TypeRA));
    }

    [Fact]
    public void TheNoteSharesTheChangesUnitOfWork()
    {
        var proposal = SetGloss("undone gloss");
        var before = _cache.LangProject.ResearchNotebookOA.RecordsOC.Count;
        ProposalApplier.Apply(_cache, proposal, ScratchDryRun.Of(_cache, proposal).Anchor, "", "m", ApplyModes.Person,
            new ApplyNote("Undone", null));
        Assert.Equal(before + 1, _cache.LangProject.ResearchNotebookOA.RecordsOC.Count);

        _cache.ActionHandlerAccessor.Undo(); // the one unit of work holds both, so both go together

        Assert.Equal(before, _cache.LangProject.ResearchNotebookOA.RecordsOC.Count);
        Assert.DoesNotContain(ProjectAppliedLog.ReadAll(_cache), e => e.ProposalId == proposal.ProposalId.ToGuid());
    }

    [Fact]
    public void ALaterProposalAppliedWithANoteWritesItsOwnNote_AndTheEarlierNoteRemains()
    {
        var original = SetGloss(SeededProject.FirstGloss + " changed");
        var later = SetGloss(SeededProject.FirstGloss);
        ProposalApplier.Apply(_cache, original, ScratchDryRun.Of(_cache, original).Anchor, "", "m", ApplyModes.Person,
            new ApplyNote("Change gloss", "Change it."));
        ProposalApplier.Apply(_cache, later, ScratchDryRun.Of(_cache, later).Anchor, "", "m", ApplyModes.Person,
            new ApplyNote("Restore gloss", "Put it back."));

        var titles = _cache.LangProject.ResearchNotebookOA.RecordsOC.Select(TitleOf).ToArray();
        Assert.Contains("Change gloss", titles);
        Assert.Contains("Restore gloss", titles);
    }

    private static string? TitleOf(IRnGenericRec record) => record.Title.Text;

    private Proposal SetGloss(string text)
    {
        var ws = _cache.WritingSystemFactory.GetStrFromWs(_cache.DefaultAnalWs);
        using var after = JsonDocument.Parse(JsonSerializer.Serialize(new { ws, text }));
        var operation = new OperationEnvelope(CanonicalId.Mint(), LexicalSenseOperationKinds.SetGloss,
            target: CanonicalId.FromGuid(_seed.FirstSenseId), after: after.RootElement.Clone());
        return new Proposal(new Dictionary<string, string> { ["lexical"] = "1.0" }, CanonicalId.Mint(), null,
            new[] { operation });
    }
}
