using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group0)]
public sealed class RewriteLexemeFormComposerTests(PristineProjectFixture pristine) : IDisposable
{
    private const string Marked = "ba\u0301go";
    private readonly LcmCache _cache = pristine.NewScratch();

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void FoldStripsOnlyGraveAcuteAndCircumflexAndReturnsNfc()
    {
        Assert.Equal("bago", LexemeFormFolding.Fold(Marked));
        Assert.Equal("agu", LexemeFormFolding.Fold("agu\u0304\u0300"));
        Assert.Equal("dug", LexemeFormFolding.Fold("du\u0304\u0301g"));
        Assert.Equal("kumbinsir", LexemeFormFolding.Fold("kumbinsi\u030Cr"));
        Assert.Equal("a\u00F1o", LexemeFormFolding.Fold("a\u00F1o"));
        Assert.Equal("a\u00F1o", LexemeFormFolding.Fold("a\u0301n\u0303o"));
        Assert.Equal(new[] { 0x0300, 0x0301, 0x0302, 0x0304, 0x030C }, LexemeFormFolding.StrippedCodePoints);
    }

    [Fact]
    public void ARoundTripRewritesTheLexemeFormThroughDryRunAndApply()
    {
        var entry = NewEntry(Marked);
        var proposal = Proposal(Intent(entry, Marked, "bago"));

        Assert.All(proposal.Operations, operation => Assert.False(string.IsNullOrWhiteSpace(operation.Rationale)));
        Assert.Equal(MoFormFormOperationKinds.SetForm, Assert.Single(proposal.Operations).Kind);
        Assert.Equal(Marked.Normalize(NormalizationForm.FormD), LexemeForm(entry));

        var dryRun = ScratchDryRun.Of(_cache, proposal);
        Assert.Equal(Marked.Normalize(NormalizationForm.FormD), LexemeForm(entry));
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "motif-tests");

        Assert.Equal("bago", LexemeForm(entry));
    }

    [Fact]
    public void AStaleExpectedFormRefusesTheWholeBatchAndChangesNothing()
    {
        var good = NewEntry(Marked);
        var other = NewEntry("ta\u0301go");
        var intent = Intent(new[] { (good, Marked, "bago"), (other, "something else", "tago") });

        var error = Assert.Throws<InvalidOperationException>(() => RewriteLexemeFormComposer.Build(_cache, intent));

        Assert.Contains("not the expected 'something else'", error.Message, StringComparison.Ordinal);
        Assert.Equal(Marked.Normalize(NormalizationForm.FormD), LexemeForm(good));
    }

    [Fact]
    public void AnEmptyCitationFormGetsTheOldSpellingAndAFilledOneIsKept()
    {
        var empty = NewEntry(Marked);
        var filled = NewEntry("ta\u0301go");
        SetCitation(filled, "my citation");
        var intent = Intent(new[] { (empty, Marked, "bago"), (filled, "ta\u0301go", "tago") }, setCitation: true);

        var operations = RewriteLexemeFormComposer.Build(_cache, intent);

        Assert.Equal([LexEntryCitationFormOperationKinds.SetCitationForm, MoFormFormOperationKinds.SetForm,
            MoFormFormOperationKinds.SetForm], operations.Select(operation => operation.Kind).ToArray());
        SoundSystemComposerTests.Execute(_cache, operations);
        Assert.Equal(Marked.Normalize(NormalizationForm.FormD), Citation(empty));
        Assert.Equal("bago", LexemeForm(empty));
        Assert.Equal("my citation", Citation(filled));
        Assert.Equal("tago", LexemeForm(filled));
    }

    [Fact]
    public void WithoutTheCitationFlagTheCitationFormIsLeftEmpty()
    {
        var entry = NewEntry(Marked);

        var operations = RewriteLexemeFormComposer.Build(_cache, Intent(entry, Marked, "bago"));

        Assert.DoesNotContain(operations, operation => operation.Kind == LexEntryCitationFormOperationKinds.SetCitationForm);
    }

    [Fact]
    public void FoldModeComputesTheNewFormAndRefusesAnExplicitOne()
    {
        var entry = NewEntry(Marked);
        var json = JsonSerializer.SerializeToElement(new
        {
            foldStressMarks = true,
            entries = new[] { new { entryGuid = Id(entry), writingSystem = Ws, expectedForm = Marked } },
        });

        SoundSystemComposerTests.Execute(_cache,
            RewriteLexemeFormComposer.Build(_cache, RewriteLexemeFormIntentParser.Parse(json)));

        Assert.Equal("bago", LexemeForm(entry));
        Assert.Throws<ContractParseException>(() => RewriteLexemeFormIntentParser.Parse(JsonSerializer.SerializeToElement(new
        {
            foldStressMarks = true,
            entries = new[] { new { entryGuid = Id(entry), writingSystem = Ws, expectedForm = Marked, newForm = "x" } },
        })));
        Assert.Throws<ContractParseException>(() => RewriteLexemeFormIntentParser.Parse(JsonSerializer.SerializeToElement(new
        {
            entries = new[] { new { entryGuid = Id(entry), writingSystem = Ws, expectedForm = Marked } },
        })));
    }

    [Fact]
    public void AThousandEntriesComposeQuickly()
    {
        var entries = new List<(ILexEntry, string, string)>();
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            for (var i = 0; i < 1000; i++)
            {
                var form = $"ba\u0301g{i}o";
                entries.Add((CreateEntry(form), form, $"bag{i}o"));
            }
        });
        var intent = Intent(entries, setCitation: true);

        var clock = Stopwatch.StartNew();
        var operations = RewriteLexemeFormComposer.Build(_cache, intent);
        clock.Stop();

        Assert.Equal(2000, operations.Count);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"Composing 1,000 entries took {clock.Elapsed}.");
    }

    private string Ws => _cache.ServiceLocator.WritingSystems.DefaultVernacularWritingSystem.Id;

    private static string Id(ILexEntry entry) => CanonicalId.FromGuid(entry.Guid).Value;

    private ILexEntry NewEntry(string form)
    {
        ILexEntry? entry = null;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => entry = CreateEntry(form));
        return entry!;
    }

    private ILexEntry CreateEntry(string form)
    {
        var stem = _cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem);
        return _cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create(stem,
            TsStringUtils.MakeString(form, _cache.DefaultVernWs), "gloss", new SandboxGenericMSA { MsaType = MsaType.kStem });
    }

    private string LexemeForm(ILexEntry entry) => entry.LexemeFormOA.Form.get_String(_cache.DefaultVernWs).Text;

    private string Citation(ILexEntry entry) => entry.CitationForm.get_String(_cache.DefaultVernWs).Text;

    private void SetCitation(ILexEntry entry, string text) =>
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor,
            () => entry.CitationForm.set_String(_cache.DefaultVernWs, TsStringUtils.MakeString(text, _cache.DefaultVernWs)));

    private RewriteLexemeFormIntent Intent(ILexEntry entry, string expected, string next) =>
        Intent(new[] { (entry, expected, next) });

    private RewriteLexemeFormIntent Intent(IEnumerable<(ILexEntry Entry, string Expected, string Next)> rewrites,
        bool setCitation = false) =>
        new(rewrites.Select(r => new LexemeFormRewrite(CanonicalId.FromGuid(r.Entry.Guid), Ws, r.Expected, r.Next)).ToArray(),
            setCitation, false);

    private Proposal Proposal(RewriteLexemeFormIntent intent) =>
        new(new Dictionary<string, string> { ["grammar"] = "1.0" }, CanonicalId.Mint(), null,
            RewriteLexemeFormComposer.Build(_cache, intent));
}
