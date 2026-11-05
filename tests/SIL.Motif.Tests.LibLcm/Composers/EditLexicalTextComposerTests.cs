using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Infrastructure;
using SIL.LCModel.Core.Text;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Execution;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class EditLexicalTextComposerTests(PristineProjectFixture pristine)
{
    public static IEnumerable<object[]> Fields() => new[] { "definition", "gloss" }
        .Concat(EditLexicalTextComposer.NoteFields).Select(field => new object[] { field });

    [Theory]
    [MemberData(nameof(Fields))]
    public void EveryFieldSavesReopensAndClearsOnlyTheSelectedAlternative(string field)
    {
        var loader = new FwDataProjectLoader();
        string path;
        CanonicalId target;
        string ws;
        string other;
        using (var cache = pristine.NewScratch())
        {
            path = cache.ProjectId.Path;
            var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.FirstEntryId);
            target = Id(field == "note" ? entry : entry.SensesOS[0]);
            ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
            other = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
            var accessor = Accessor(cache, target, field);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                accessor.set_String(cache.DefaultAnalWs, TsStringUtils.MakeString("caf\u00e9", cache.DefaultAnalWs));
                accessor.set_String(cache.DefaultVernWs, TsStringUtils.MakeString("keep this alternative", cache.DefaultVernWs));
            });
            loader.Save(cache);
            var intent = Parse(target, ws, "cafe\u0301", "  full meaning and example\nsecond line  ");
            var operations = EditLexicalTextComposer.Build(cache, intent, field);
            Assert.Single(operations);
            Assert.Equal("caf\u00e9".Normalize(NormalizationForm.FormD),
                accessor.get_String(cache.DefaultAnalWs).Text.Normalize(NormalizationForm.FormD));
            Apply(cache, operations);
            loader.Save(cache);
        }
        using (var reopened = loader.LoadScratchCache(path))
        {
            var accessor = Accessor(reopened, target, field);
            Assert.Equal("  full meaning and example\nsecond line  ", Text(reopened, accessor, ws));
            Assert.Equal("keep this alternative", Text(reopened, accessor, other));
            var clear = EditLexicalTextComposer.Build(reopened,
                Parse(target, ws, "  full meaning and example\nsecond line  ", ""), field);
            Assert.Contains("/clear", Assert.Single(clear).Kind, StringComparison.Ordinal);
            Apply(reopened, clear);
            loader.Save(reopened);
        }
        using var cleared = loader.LoadScratchCache(path);
        Assert.Equal("", Text(cleared, Accessor(cleared, target, field), ws));
        Assert.Equal("keep this alternative", Text(cleared, Accessor(cleared, target, field), other));
    }

    [Fact]
    public void AStaleLaterAlternativeRefusesTheWholeEditAndUnknownOrDuplicateSystemsRefuse()
    {
        using var cache = pristine.NewScratch();
        var target = CanonicalId.FromGuid(pristine.Seed.FirstSenseId);
        var ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        var other = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var good = new LexicalTextAlternative(ws, "", "new");
        var intent = new EditLexicalTextIntent(target, [good, new(other, "stale", "new")]);
        var refusal = Assert.Throws<InvalidOperationException>(() => EditLexicalTextComposer.Build(cache, intent, "definition"));
        Assert.Contains("not the expected 'stale'", refusal.Message, StringComparison.Ordinal);
        Assert.Equal("", Text(cache, Accessor(cache, target, "definition"), ws));
        Assert.Throws<InvalidOperationException>(() => EditLexicalTextComposer.Build(cache,
            new(target, [good, good]), "definition"));
        Assert.Throws<InvalidOperationException>(() => EditLexicalTextComposer.Build(cache,
            new(target, [new("qaa-x-not-in-project", "", "new")]), "definition"));
    }

    [Fact]
    public void EmptyExpectationsWhitespaceAndUnrestrictedGlossLengthAreAllowed()
    {
        using var cache = pristine.NewScratch();
        var target = CanonicalId.FromGuid(pristine.Seed.FirstSenseId);
        var ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        var other = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var operations = EditLexicalTextComposer.Build(cache, new(target,
            [new(ws, "", " \t "), new(other, "", "another full meaning")]), "general-note");
        Assert.Equal(2, operations.Count);
        Apply(cache, operations);
        Assert.Equal(" \t ", Text(cache, Accessor(cache, target, "general-note"), ws));
        Assert.Empty(EditLexicalTextComposer.Build(cache, new(target,
            [new(ws, " \t ", " \t ")]), "general-note"));
        var gloss = Accessor(cache, target, "gloss");
        Apply(cache, EditLexicalTextComposer.Build(cache, new(target,
            [new(ws, Text(cache, gloss, ws), "more than three words are still accepted")]), "gloss"));
        Assert.Equal("more than three words are still accepted", Text(cache, gloss, ws));
    }

    [Fact]
    public void EntryNoteWorksOnAffixesAndAFormTargetExplainsWhereToRecordItsExample()
    {
        using var cache = pristine.NewScratch();
        ILexEntry entry = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
            entry.LexemeFormOA = cache.ServiceLocator.GetInstance<IMoAffixAllomorphFactory>().Create();
            entry.LexemeFormOA.MorphTypeRA = cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>()
                .GetObject(MoMorphTypeTags.kguidMorphPrefix);
        });
        var ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        Apply(cache, EditLexicalTextComposer.Build(cache, Parse(Id(entry), ws, "", "example for this prefix"), "note"));
        Assert.Equal("example for this prefix", entry.Comment.get_String(cache.DefaultAnalWs).Text);
        var error = Assert.Throws<InvalidOperationException>(() => EditLexicalTextComposer.Build(cache,
            Parse(Id(entry.LexemeFormOA), ws, "", "example"), "note"));
        Assert.Contains("owning entry's Note", error.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => EditLexicalTextComposer.Build(cache,
            Parse(Id(entry), ws, "", "example"), "grammar-note"));
    }

    [Fact]
    public void ChangedTextAfterDryRunPreventsApply()
    {
        using var cache = pristine.NewScratch();
        var target = CanonicalId.FromGuid(pristine.Seed.FirstSenseId);
        var ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        var proposal = Proposal(EditLexicalTextComposer.Build(cache, Parse(target, ws, "", "proposed"), "definition"));
        var dry = ScratchDryRun.Of(cache, proposal);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor,
            () => Accessor(cache, target, "definition").set_String(cache.DefaultAnalWs,
                TsStringUtils.MakeString("edited elsewhere", cache.DefaultAnalWs)));
        var refusal = Assert.Throws<PreconditionRefusal>(() => ProposalApplier.Apply(cache, proposal, dry.Anchor, "test"));
        Assert.Equal(PreconditionKind.FootprintDrift, refusal.Kind);
        Assert.Equal("edited elsewhere", Text(cache, Accessor(cache, target, "definition"), ws));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"alternatives\":[]}")]
    [InlineData("{\"alternatives\":[{\"writingSystem\":\"en\",\"text\":\"new\"}]}")]
    [InlineData("{\"alternatives\":[{\"writingSystem\":\"en\",\"expectedText\":null,\"text\":\"new\"}]}")]
    [InlineData("{\"alternatives\":[{\"writingSystem\":\"en\",\"expectedText\":\"\",\"text\":\"new\",\"extra\":true}]}")]
    public void MissingGuardsAndMalformedIntentsRefuse(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.ThrowsAny<ContractParseException>(() => EditLexicalTextIntentParser.Parse(document.RootElement));
    }

    private static EditLexicalTextIntent Parse(CanonicalId target, string ws, string expected, string text) =>
        EditLexicalTextIntentParser.Parse(JsonSerializer.SerializeToElement(new
        {
            target = target.Value,
            alternatives = new[] { new { writingSystem = ws, expectedText = expected, text } },
        }));

    private static CanonicalId Id(ICmObject item) => CanonicalId.FromGuid(item.Guid);
    private static Proposal Proposal(IReadOnlyList<OperationEnvelope> operations) =>
        new(new Dictionary<string, string> { ["lexical"] = "1.0" }, CanonicalId.Mint(), null, operations);
    private static void Apply(LcmCache cache, IReadOnlyList<OperationEnvelope> operations)
    {
        var proposal = Proposal(operations);
        var dry = ScratchDryRun.Of(cache, proposal);
        ProposalApplier.Apply(cache, proposal, dry.Anchor, "test");
    }
    private static string Text(LcmCache cache, ITsMultiString accessor, string ws) =>
        accessor.get_String(cache.WritingSystemFactory.GetWsFromStr(ws))?.Text ?? "";
    private static ITsMultiString Accessor(LcmCache cache, CanonicalId target, string field)
    {
        var obj = cache.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(target.ToGuid());
        if (obj is ILexEntry entry) return entry.Comment;
        var sense = (ILexSense)obj;
        return field switch
        {
            "definition" => sense.Definition, "gloss" => sense.Gloss, "anthropology-note" => sense.AnthroNote,
            "discourse-note" => sense.DiscourseNote, "general-note" => sense.GeneralNote, "grammar-note" => sense.GrammarNote,
            "phonology-note" => sense.PhonologyNote, "semantics-note" => sense.SemanticsNote,
            "sociolinguistics-note" => sense.SocioLinguisticsNote,
            _ => throw new InvalidOperationException(field),
        };
    }
}
