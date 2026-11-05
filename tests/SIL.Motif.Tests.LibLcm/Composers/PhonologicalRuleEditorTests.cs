using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

/// <summary>Checks the rule editor: optional items, editing in place, order, disabling and deletion.</summary>
[Collection(LcmCacheParallelCollections.Group1)]
public sealed class PhonologicalRuleEditorTests(PristineProjectFixture pristine)
{
    private static readonly string[] Letters = ["p", "t", "k", "m", "n", "a", "u", "N"];

    private LcmCache Project()
    {
        var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, Letters);
        return cache;
    }

    private static CanonicalId Phoneme(LcmCache cache, string text) =>
        CanonicalId.FromGuid(cache.LangProject.PhonologicalDataOA.PhonemeSetsOS[0].PhonemesOC
            .SelectMany(phoneme => phoneme.CodesOS.Select(code => (Code: code, Phoneme: phoneme)))
            .Single(pair => pair.Code.Representation.get_String(cache.DefaultVernWs)?.Text == text).Phoneme.Guid);

    private static PhonologicalRuleItem Seg(LcmCache cache, string text) => new(Phoneme: Phoneme(cache, text));

    private static EnvironmentContext Ctx(LcmCache cache, string text) => new(Phoneme: Phoneme(cache, text));

    private static CanonicalId AddRule(LcmCache cache, string name, PhonologicalRuleItem[] input,
        PhonologicalRuleItem[] output, EnvironmentContext[] left, EnvironmentContext[] right)
    {
        var rules = cache.LangProject.PhonologicalDataOA.PhonRulesOS;
        Placement? placement = rules.Count == 0 ? null : new Placement(Id(rules[^1]), null);
        return Created(ApplyOps(cache, AuthorPhonologicalRuleComposer.Build(cache, new(name,
            PhonologicalRuleDirection.Simultaneous, input, output, left, right, placement))),
            PhPhonDataPhonRulesOperationKinds.Create);
    }

    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    [Fact]
    public void EightInputAndOutputItemsRoundTripThroughAuthoringAndEditing()
    {
        using var cache = Project();
        var input = Letters.Select(letter => Seg(cache, letter)).ToArray();
        var output = input.Reverse().ToArray();
        var rule = AddRule(cache, "sequence", input, output, [], []);
        var read = PhonologicalRuleReader.Read(Get<IPhSegmentRule>(cache, rule))!;
        var wire = PhonologicalRuleReader.ToJson(read);
        var authored = AuthorPhonologicalRuleIntentParser.Parse(Json(wire));
        Assert.Equal(input, authored.Input);
        Assert.Equal(output, authored.Output);

        var edit = EditPhonologicalRuleIntentParser.Parse(Json(new
        {
            rule = rule.Value,
            input = wire["output"], output = wire["input"], expected = wire,
        }));
        ApplyOps(cache, EditPhonologicalRuleComposer.Build(cache, edit));
        read = PhonologicalRuleReader.Read(Get<IPhSegmentRule>(cache, rule))!;
        Assert.Equal(output, read.Input);
        Assert.Equal(input, read.Output);
        var stale = Assert.Throws<InvalidOperationException>(() => EditPhonologicalRuleComposer.Build(cache, edit));
        Assert.Contains("input, output", stale.Message);
    }

    [Theory]
    [InlineData("input")]
    [InlineData("output")]
    public void NineItemsAreRefusedByAuthorAndEditBeforeAnyChange(string side)
    {
        using var cache = Project();
        var rule = AddRule(cache, "r", [Seg(cache, "n")], [Seg(cache, "m")], [], []);
        var tooMany = Enumerable.Repeat(Seg(cache, "t"), 9).ToArray();
        var before = PhonologicalRuleReader.ToJson(PhonologicalRuleReader.Read(Get<IPhSegmentRule>(cache, rule))!)
            .ToJsonString();
        var authored = new AuthorPhonologicalRuleIntent("too long", PhonologicalRuleDirection.Simultaneous,
            side == "input" ? tooMany : [], side == "output" ? tooMany : [], [], [], new Placement(rule, null));
        var authorError = Assert.Throws<InvalidOperationException>(() => AuthorPhonologicalRuleComposer.Build(cache, authored));
        Assert.Contains($"at most 8 {side} items", authorError.Message);
        Assert.Throws<ContractParseException>(() => AuthorPhonologicalRuleIntentParser.Parse(Json(PhonologicalRuleReader.ToJson(authored))));
        var change = side == "input" ? new PhonologicalRuleParts(Input: tooMany) : new PhonologicalRuleParts(Output: tooMany);
        var editError = Assert.Throws<InvalidOperationException>(() => EditPhonologicalRuleComposer.Build(cache, new(rule, change)));
        Assert.Contains($"at most 8 {side} items", editError.Message);
        var wire = new JsonObject
        {
            ["rule"] = rule.Value, [side] = PhonologicalRuleReader.ItemsJson(tooMany),
        };
        Assert.Throws<ContractParseException>(() => EditPhonologicalRuleIntentParser.Parse(Json(wire)));
        Assert.Equal(before, PhonologicalRuleReader.ToJson(PhonologicalRuleReader.Read(Get<IPhSegmentRule>(cache, rule))!)
            .ToJsonString());
        Assert.Single(cache.LangProject.PhonologicalDataOA.PhonRulesOS);
    }

    [Fact]
    public void InsertionAndDeletionNeedNoItemOnTheEmptySide()
    {
        using var cache = Project();
        var insertion = AddRule(cache, "insert u", [], [Seg(cache, "u")], [Ctx(cache, "p")], [Ctx(cache, "t")]);
        var deletion = AddRule(cache, "delete N", [Seg(cache, "N")], [], [], [new(Boundary: "word")]);

        var inserted = PhonologicalRuleReader.Read(Get<IPhSegmentRule>(cache, insertion))!;
        Assert.Empty(inserted.Input);
        Assert.Single(inserted.Output);
        var deleted = PhonologicalRuleReader.Read(Get<IPhSegmentRule>(cache, deletion))!;
        Assert.Single(deleted.Input);
        Assert.Empty(deleted.Output);
        Assert.Equal("word", Assert.Single(deleted.Right).Boundary);
    }

    [Fact]
    public void OptionalAndRepeatedContextItemsBecomeIterationContextsWithBounds()
    {
        using var cache = Project();
        var optional = AddRule(cache, "n becomes m after optional a", [Seg(cache, "n")], [Seg(cache, "m")],
            [Ctx(cache, "p"), Ctx(cache, "a") with { Minimum = 0, Maximum = 1 }], [Ctx(cache, "t") with { Minimum = 1, Maximum = -1 }]);

        var rhs = ((IPhRegularRule)Get<IPhSegmentRule>(cache, optional)).RightHandSidesOS.Single();
        var sequence = Assert.IsAssignableFrom<IPhSequenceContext>(rhs.LeftContextOA);
        var iteration = Assert.IsAssignableFrom<IPhIterationContext>(sequence.MembersRS[1]);
        Assert.Equal((0, 1), (iteration.Minimum, iteration.Maximum));
        Assert.IsAssignableFrom<IPhSimpleContextSeg>(iteration.MemberRA);
        var single = Assert.IsAssignableFrom<IPhIterationContext>(rhs.RightContextOA);
        Assert.Equal((1, -1), (single.Minimum, single.Maximum));

        var read = PhonologicalRuleReader.Read(Get<IPhSegmentRule>(cache, optional))!;
        Assert.Equal((0, 1), (read.Left[1].Minimum, read.Left[1].Maximum));
        Assert.Equal((1, -1), (read.Right[0].Minimum, read.Right[0].Maximum));
    }

    [Fact]
    public void OptionalAndRepeatedItemsAreParsedForRulesAndRefusedInAllomorphEnvironments()
    {
        var rule = AuthorPhonologicalRuleIntentParser.Parse(Json(new
        {
            name = "r", direction = "simultaneous", input = Array.Empty<object>(), output = Array.Empty<object>(),
            left = new object[] { new { phoneme = CanonicalId.Mint().Value, optional = true } },
            right = new object[] { new { phoneme = CanonicalId.Mint().Value, minimum = 2 } },
        }));
        Assert.Equal((0, 1), (rule.Left[0].Minimum, rule.Left[0].Maximum));
        Assert.Equal((2, -1), (rule.Right[0].Minimum, rule.Right[0].Maximum));
        Assert.ThrowsAny<Exception>(() => AuthorEnvironmentIntentParser.Parse(Json(new
        {
            name = "e", left = new object[] { new { phoneme = CanonicalId.Mint().Value, optional = true } },
            right = Array.Empty<object>(),
        })));
        Assert.ThrowsAny<Exception>(() => AuthorPhonologicalRuleIntentParser.Parse(Json(new
        {
            name = "r", direction = "simultaneous", input = Array.Empty<object>(), output = Array.Empty<object>(),
            left = new object[] { new { phoneme = CanonicalId.Mint().Value, optional = true, minimum = 1 } },
        })));
    }

    [Fact]
    public void ABoundaryOrMarkerCannotBeOptional()
    {
        using var cache = Project();
        Assert.Throws<InvalidOperationException>(() => AuthorPhonologicalRuleComposer.Build(cache, new("r",
            PhonologicalRuleDirection.Simultaneous, [Seg(cache, "n")], [Seg(cache, "m")],
            [new(Boundary: "morpheme", Minimum: 0, Maximum: 1)], [])));
    }

    [Fact]
    public void EditingReplacesOnlyTheNamedPartsAndKeepsTheRuleIdentityAndPosition()
    {
        using var cache = Project();
        var first = AddRule(cache, "first", [Seg(cache, "n")], [Seg(cache, "m")], [Ctx(cache, "a")], [Ctx(cache, "p")]);
        var second = AddRule(cache, "second", [Seg(cache, "t")], [Seg(cache, "k")], [], []);
        var contextsBefore = cache.LangProject.PhonologicalDataOA.ContextsOS.Count;

        var operations = EditPhonologicalRuleComposer.Build(cache, new(first, new(
            Name: "first, renamed", Input: [Seg(cache, "t")], Output: [Seg(cache, "m")],
            Right: [Ctx(cache, "p"), Ctx(cache, "u") with { Minimum = 0, Maximum = 1 }])));
        ApplyOps(cache, operations);

        var rule = Get<IPhSegmentRule>(cache, first);
        Assert.Equal(first, Id(cache.LangProject.PhonologicalDataOA.PhonRulesOS[0]));
        Assert.Equal(second, Id(cache.LangProject.PhonologicalDataOA.PhonRulesOS[1]));
        var read = PhonologicalRuleReader.Read(rule)!;
        Assert.Equal("first, renamed", read.Name);
        Assert.Equal(Phoneme(cache, "t"), Assert.Single(read.Input).Phoneme);
        Assert.Single(read.Output);
        Assert.Equal([Phoneme(cache, "a")], read.Left.Select(c => c.Phoneme!.Value));
        Assert.Equal(2, read.Right.Count);
        Assert.Equal((0, 1), (read.Right[1].Minimum, read.Right[1].Maximum));
        Assert.Single(((IPhRegularRule)rule).RightHandSidesOS);
        // Net +3: the new right adds a sequence, an iteration and u; the replaced contexts are deleted.
        Assert.Equal(contextsBefore + 3, cache.LangProject.PhonologicalDataOA.ContextsOS.Count);
    }

    [Fact]
    public void EditingClearsAContextWhenGivenAnEmptyListAndRefusesAStaleExpectation()
    {
        using var cache = Project();
        var rule = AddRule(cache, "r", [Seg(cache, "n")], [Seg(cache, "m")], [Ctx(cache, "a")], [Ctx(cache, "p")]);

        Assert.Throws<InvalidOperationException>(() => EditPhonologicalRuleComposer.Build(cache, new(rule,
            new(Left: []), new PhonologicalRuleParts(Output: [Seg(cache, "k")]))));
        ApplyOps(cache, EditPhonologicalRuleComposer.Build(cache, new(rule, new(Left: []),
            new PhonologicalRuleParts(Output: [Seg(cache, "m")], Left: [Ctx(cache, "a")]))));

        var read = PhonologicalRuleReader.Read(Get<IPhSegmentRule>(cache, rule))!;
        Assert.Empty(read.Left);
        Assert.Single(read.Right);
        Assert.Throws<InvalidOperationException>(() => EditPhonologicalRuleComposer.Build(cache, new(rule,
            new(Direction: PhonologicalRuleDirection.RightToLeftIterative),
            new PhonologicalRuleParts(Left: [Ctx(cache, "a")]))));
        ApplyOps(cache, EditPhonologicalRuleComposer.Build(cache,
            new(rule, new(Direction: PhonologicalRuleDirection.RightToLeftIterative))));
        Assert.Equal(1, Get<IPhSegmentRule>(cache, rule).Direction);
    }

    [Fact]
    public void ThreeRulesReorderWithTheMinimalMovesAndAnUnchangedOrderIsRefused()
    {
        using var cache = Project();
        var one = AddRule(cache, "one", [Seg(cache, "n")], [Seg(cache, "m")], [], []);
        var two = AddRule(cache, "two", [Seg(cache, "t")], [Seg(cache, "k")], [], []);
        var three = AddRule(cache, "three", [Seg(cache, "p")], [Seg(cache, "u")], [], []);

        var moves = OrderPhonologicalRulesComposer.Build(cache, new(Rule: three, Before: one));
        Assert.Single(moves);
        Assert.All(moves, move => Assert.Equal(PhPhonDataPhonRulesOperationKinds.MovePhonRules, move.Kind));
        ApplyOps(cache, moves);
        Assert.Equal([three, one, two], cache.LangProject.PhonologicalDataOA.PhonRulesOS.Select(Id));

        ApplyOps(cache, OrderPhonologicalRulesComposer.Build(cache, new(Order: [one, two, three])));
        Assert.Equal([one, two, three], cache.LangProject.PhonologicalDataOA.PhonRulesOS.Select(Id));

        Assert.Throws<InvalidOperationException>(() => OrderPhonologicalRulesComposer.Build(cache, new(Order: [one, two, three])));
        Assert.Throws<InvalidOperationException>(() => OrderPhonologicalRulesComposer.Build(cache, new(Order: [one, two])));
        Assert.Throws<InvalidOperationException>(() => OrderPhonologicalRulesComposer.Build(cache, new(Rule: one, After: one)));
    }

    [Fact]
    public void DisablingWritesOneOperationAndRefusesAStaleExpectation()
    {
        using var cache = Project();
        var rule = AddRule(cache, "r", [Seg(cache, "n")], [Seg(cache, "m")], [], []);
        Assert.False(Get<IPhSegmentRule>(cache, rule).Disabled);

        Assert.Throws<InvalidOperationException>(() =>
            SetPhonologicalRuleDisabledComposer.Build(cache, new(rule, ExpectedDisabled: true, Disabled: false)));
        var operations = SetPhonologicalRuleDisabledComposer.Build(cache, new(rule, ExpectedDisabled: false, Disabled: true));
        var operation = Assert.Single(operations);
        Assert.Equal(PhSegmentRuleDisabledOperationKinds.SetDisabled, operation.Kind);
        ApplyOps(cache, operations);
        Assert.True(Get<IPhSegmentRule>(cache, rule).Disabled);

        ApplyOps(cache, SetPhonologicalRuleDisabledComposer.Build(cache, new(rule, ExpectedDisabled: true, Disabled: false)));
        Assert.False(Get<IPhSegmentRule>(cache, rule).Disabled);
    }

    [Fact]
    public void DeletingARuleRemovesItsContextsAndRefusesAWrongName()
    {
        using var cache = Project();
        var keep = AddRule(cache, "keep", [Seg(cache, "n")], [Seg(cache, "m")], [Ctx(cache, "a")], [Ctx(cache, "p")]);
        var poolBefore = cache.LangProject.PhonologicalDataOA.ContextsOS.Count;
        var doomed = AddRule(cache, "doomed", [Seg(cache, "t")], [Seg(cache, "k")],
            [Ctx(cache, "a"), Ctx(cache, "u") with { Minimum = 0, Maximum = 1 }], [Ctx(cache, "p")]);
        Assert.True(cache.LangProject.PhonologicalDataOA.ContextsOS.Count > poolBefore);

        Assert.Throws<InvalidOperationException>(() =>
            DeletePhonologicalRuleComposer.Build(cache, new(doomed, "not its name")));
        ApplyOps(cache, DeletePhonologicalRuleComposer.Build(cache, new(doomed, "doomed")));

        Assert.False(Exists(cache, doomed));
        Assert.Equal([keep], cache.LangProject.PhonologicalDataOA.PhonRulesOS.Select(Id));
        Assert.Equal(poolBefore, cache.LangProject.PhonologicalDataOA.ContextsOS.Count);
    }
}
