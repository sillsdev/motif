using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

/// <summary>Slot retirement and rename: what the composers refuse and what they stage, without the parser.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class AffixSlotEditorComposerTests(PristineProjectFixture pristine)
{
    [Fact]
    public void RetiringASlotRefusesWhileATemplateStillListsItAndEmptyingATemplateIsAllowedButWarnedAbout()
    {
        using var cache = pristine.NewScratch();
        var ws = Anal(cache);
        var verb = Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Verb", ws, "V"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        var slot = SlotId(cache, verb, "ONLY", optional: false);
        var template = Created(ApplyOps(cache, AuthorAffixTemplateComposer.Build(cache,
            new AuthorAffixTemplateIntent(verb, "T", ws, [], [slot], true))),
            PartOfSpeechAffixTemplatesOperationKinds.Create);
        var refusal = Assert.Throws<InvalidOperationException>(() =>
            RetireAffixSlotComposer.Build(cache, new RetireAffixSlotIntent(slot)));
        Assert.Contains("1 affix template", refusal.Message);

        ApplyOps(cache, EditAffixTemplateComposer.Build(cache,
            new EditAffixTemplateIntent(template, [], [], [slot], [])));
        ApplyOps(cache, RetireAffixSlotComposer.Build(cache, new RetireAffixSlotIntent(slot)));
        Assert.False(Exists(cache, slot));
        Assert.Empty(Get<IMoInflAffixTemplate>(cache, template).SuffixSlotsRS);
    }

    [Fact]
    public void ARenameNeedsTheCurrentNameAndAnIntentThatSaysWhatToChange()
    {
        using var cache = pristine.NewScratch();
        var ws = Anal(cache);
        var verb = Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Verb", ws, "V"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        var slot = SlotId(cache, verb, "NUM", optional: false);
        Assert.Throws<InvalidOperationException>(() => EditAffixSlotComposer.Build(cache,
            new EditAffixSlotIntent(slot, null, null, new EditAffixSlotRename(ws, "WRONG", "Number"))));
        Assert.Empty(EditAffixSlotComposer.Build(cache,
            new EditAffixSlotIntent(slot, null, null, new EditAffixSlotRename(ws, "NUM", "NUM"))));
        var both = EditAffixSlotComposer.Build(cache, new EditAffixSlotIntent(slot, false, true,
            new EditAffixSlotRename(ws, "NUM", "Number")));
        Assert.Equal([MoInflAffixSlotNameOperationKinds.SetName, MoInflAffixSlotOptionalOperationKinds.SetOptional],
            both.Select(operation => operation.Kind));
        var empty = JsonDocument.Parse("""{"target":"x-1"}""").RootElement;
        Assert.ThrowsAny<Exception>(() => EditAffixSlotIntentParser.Parse(empty));
    }

    private static CanonicalId SlotId(LcmCache cache, CanonicalId category, string name, bool optional) =>
        Created(ApplyOps(cache, AuthorAffixSlotComposer.Build(cache,
            new AuthorAffixSlotIntent(category, name, Anal(cache), optional))),
            PartOfSpeechAffixSlotsOperationKinds.Create);
}
