using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Execution;
using SIL.Motif.Runner.AppliedLog;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class MultipleOutcomeRuleComposerTests(PristineProjectFixture pristine)
{
    private LcmCache Project()
    {
        var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "n", "m", "t", "a", "u");
        return cache;
    }

    private static PhonologicalRuleItem Seg(LcmCache cache, string text) => new(Phoneme: RealParserReadings.Phonemes(cache)[text]);
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
    private static JsonObject Wire(LcmCache cache, CanonicalId id) => PhonologicalRuleReader.ToJson(
        PhonologicalRuleReader.Read(Get<IPhSegmentRule>(cache, id))!);
    private static CanonicalId Feature(LcmCache cache, string name) => Created(ApplyOps(cache,
        AuthorExceptionFeatureComposer.Build(cache, new(name, Anal(cache), null))), CmPossibilityListPossibilitiesOperationKinds.Create);
    private static AuthorPhonologicalRuleIntent Intent(LcmCache cache, params PhonologicalRuleOutcome[] outcomes) =>
        new("alternatives", PhonologicalRuleDirection.Simultaneous, [Seg(cache, "n")], [], [], [], RightHandSides: outcomes);
    private static CanonicalId Author(LcmCache cache, AuthorPhonologicalRuleIntent intent) => Created(
        ApplyOps(cache, AuthorPhonologicalRuleComposer.Build(cache, intent).Reverse().ToArray()), PhPhonDataPhonRulesOperationKinds.Create);
    private static EditPhonologicalRuleIntent Edit(CanonicalId id, JsonArray outcomes, JsonObject expected) =>
        EditPhonologicalRuleIntentParser.Parse(Json(new { rule = id.Value, rightHandSides = outcomes, expected }));

    [Fact]
    public void EachOutcomeKeepsItsOwnOutputContextsAndApplicabilityThroughSaveReadAndAuthorRoundTrip()
    {
        using var cache = Project();
        var need = Feature(cache, "required");
        var avoid = Feature(cache, "excluded");
        var category = AddCategory(cache, "Word");
        var id = Author(cache, Intent(cache,
            new([Seg(cache, "m")], [new(Phoneme: RealParserReadings.Phonemes(cache)["a"], Minimum: 0, Maximum: 1)], [], [need], [avoid], [category]),
            new([Seg(cache, "t"), Seg(cache, "u")], [], [new(Boundary: "word")], [avoid], [need])));
        var loader = new FwDataProjectLoader();
        loader.Save(cache);
        using var reopened = loader.LoadScratchCache(cache.ProjectId.Path);
        var rule = (IPhRegularRule)Get<IPhSegmentRule>(reopened, id);
        var rhs = rule.RightHandSidesOS.ToArray();
        Assert.Equal(2, rhs.Length);
        Assert.Equal(category.ToGuid(), Assert.Single(rhs[0].InputPOSesRC).Guid);
        Assert.Empty(rhs[1].InputPOSesRC);
        Assert.Equal(need.ToGuid(), Assert.Single(rhs[0].ReqRuleFeatsRC).ItemRA.Guid);
        Assert.Equal(avoid.ToGuid(), Assert.Single(rhs[1].ReqRuleFeatsRC).ItemRA.Guid);
        Assert.Equal(avoid.ToGuid(), Assert.Single(rhs[0].ExclRuleFeatsRC).ItemRA.Guid);
        Assert.Equal(need.ToGuid(), Assert.Single(rhs[1].ExclRuleFeatsRC).ItemRA.Guid);
        Assert.Equal(2, reopened.LangProject.PhonologicalDataOA.PhonRuleFeatsOA.PossibilitiesOS.Count);
        var wire = Wire(reopened, id);
        Assert.Equal(2, wire["rightHandSides"]!.AsArray().Count);
        var parsed = AuthorPhonologicalRuleIntentParser.Parse(Json(wire)) with { Name = "copy", Placement = new(id, null) };
        var copy = Author(reopened, parsed);
        var copied = Wire(reopened, copy);
        copied["name"] = "alternatives";
        Assert.Equal(wire.ToJsonString(), copied.ToJsonString());
    }

    [Fact]
    public void GuardedSequenceReplacementAddsReordersAndRemovesOutcomesAndRefusesStaleFeatureOrOrderGuards()
    {
        using var cache = Project();
        var feature = Feature(cache, "tag");
        var id = Author(cache, Intent(cache, new([Seg(cache, "m")], [], [], [feature]), new([Seg(cache, "t")], [], [])));
        ApplyOps(cache, SetPhonologicalRuleDisabledComposer.Build(cache, new(id, false, true)));
        var old = Wire(cache, id);
        var outcomes = old["rightHandSides"]!.AsArray();
        var added = new JsonArray(outcomes[1]!.DeepClone(), outcomes[0]!.DeepClone(), new JsonObject
        {
            ["output"] = PhonologicalRuleReader.ItemsJson([Seg(cache, "u")]),
        });
        ApplyOps(cache, EditPhonologicalRuleComposer.Build(cache, Edit(id, added, old)).Reverse().ToArray());
        Assert.Equal(["t", "m", "u"], ((IPhRegularRule)Get<IPhSegmentRule>(cache, id)).RightHandSidesOS.Select(r =>
            ((IPhSimpleContextSeg)r.StrucChangeOS.Single()).FeatureStructureRA.CodesOS[0].Representation.get_String(cache.DefaultVernWs).Text));
        var minted = 0;
        Assert.Throws<InvalidOperationException>(() => EditPhonologicalRuleComposer.Build(cache, Edit(id, added, old),
            () => { minted++; return CanonicalId.Mint(); }));
        Assert.Equal(0, minted);
        var current = Wire(cache, id);
        var changedFeature = current.DeepClone().AsObject();
        changedFeature["rightHandSides"]![1]!["requiredFeatures"] = new JsonArray();
        Assert.Throws<InvalidOperationException>(() => EditPhonologicalRuleComposer.Build(cache, Edit(id, added, changedFeature)));
        var keep = new JsonArray(current["rightHandSides"]![1]!.DeepClone());
        ApplyOps(cache, EditPhonologicalRuleComposer.Build(cache, Edit(id, keep, current)).Reverse().ToArray());
        Assert.True(Get<IPhSegmentRule>(cache, id).Disabled);
        Assert.Single(((IPhRegularRule)Get<IPhSegmentRule>(cache, id)).RightHandSidesOS);
        Assert.Single(Wire(cache, id)["rightHandSides"]!.AsArray());
    }

    [Fact]
    public void LegacyEditsOfRuleWidePartsPreserveEveryOutcomeButOutcomeShorthandAndUnguardedReplacementAreRefused()
    {
        using var cache = Project();
        var id = Author(cache, Intent(cache, new([Seg(cache, "m")], [], []), new([Seg(cache, "t")], [], [])));
        var rule = (IPhRegularRule)Get<IPhSegmentRule>(cache, id);
        var ids = rule.RightHandSidesOS.Select(r => r.Guid).ToArray();
        ApplyOps(cache, EditPhonologicalRuleComposer.Build(cache, new(id, new(Name: "renamed", Input: [Seg(cache, "a")]))));
        Assert.Equal(ids, rule.RightHandSidesOS.Select(r => r.Guid));
        Assert.Equal("renamed", rule.Name.BestAnalysisAlternative.Text);
        Assert.Throws<InvalidOperationException>(() => EditPhonologicalRuleComposer.Build(cache, new(id, new(Output: []))));
        Assert.Throws<InvalidOperationException>(() => EditPhonologicalRuleComposer.Build(cache,
            new(id, new(RightHandSides: [new([], [], [])]))));
    }

    [Theory]
    [InlineData(0)]
    public void EmptyOutcomesAreRefusedInJsonAndTypedAuthoringAndEditsBeforeMinting(int count)
    {
        using var cache = Project();
        var outcomes = Enumerable.Repeat(new PhonologicalRuleOutcome([Seg(cache, "m")], [], []), count).ToArray();
        var invalid = Intent(cache, outcomes);
        var minted = 0;
        Assert.Throws<InvalidOperationException>(() => AuthorPhonologicalRuleComposer.Build(cache, invalid,
            () => { minted++; return CanonicalId.Mint(); }));
        Assert.Equal(0, minted);
        Assert.Throws<ContractParseException>(() => AuthorPhonologicalRuleIntentParser.Parse(Json(PhonologicalRuleReader.ToJson(invalid))));
        var id = Author(cache, Intent(cache, new PhonologicalRuleOutcome([Seg(cache, "m")], [], [])));
        Assert.Throws<InvalidOperationException>(() => EditPhonologicalRuleComposer.Build(cache,
            new(id, new(RightHandSides: outcomes), new(RightHandSides: [new([Seg(cache, "m")], [], [])])),
            () => { minted++; return CanonicalId.Mint(); }));
        Assert.Equal(0, minted);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(32)]
    public void LongerOutcomeSequencesCanBeAuthoredCopiedAndReplacedWithoutTruncation(int count)
    {
        using var cache = Project();
        var outcomes = Enumerable.Range(0, count).Select(i => new PhonologicalRuleOutcome(
            [Seg(cache, i % 2 == 0 ? "m" : "t")], [], [])).ToArray();
        var id = Author(cache, Intent(cache, outcomes));
        var wire = Wire(cache, id);
        Assert.Equal(count, wire["rightHandSides"]!.AsArray().Count);
        var copy = Author(cache, AuthorPhonologicalRuleIntentParser.Parse(Json(wire)) with
            { Name = "copy", Placement = new(id, null) });
        Assert.Equal(count, ((IPhRegularRule)Get<IPhSegmentRule>(cache, copy)).RightHandSidesOS.Count);
        var reversed = new JsonArray(wire["rightHandSides"]!.AsArray().Reverse().Select(o => o!.DeepClone()).ToArray());
        ApplyOps(cache, EditPhonologicalRuleComposer.Build(cache, Edit(id, reversed, wire)).Reverse().ToArray());
        Assert.Equal(reversed.ToJsonString(), Wire(cache, id)["rightHandSides"]!.ToJsonString());
    }

    [Theory]
    [InlineData("category", false)]
    [InlineData("required", false)]
    [InlineData("excluded", false)]
    [InlineData("required-item", false)]
    [InlineData("excluded-item", false)]
    [InlineData("context", false)]
    [InlineData("nested-context", false)]
    [InlineData("output", false)]
    [InlineData("category", true)]
    [InlineData("required", true)]
    [InlineData("excluded", true)]
    [InlineData("required-item", true)]
    [InlineData("excluded-item", true)]
    [InlineData("context", true)]
    [InlineData("nested-context", true)]
    [InlineData("output", true)]
    public void OutcomeDriftAfterStagingOrBoundDryRunRefusesBeforeMutation(string drift, bool bound)
    {
        using var cache = Project();
        var required = Feature(cache, "required");
        var excluded = Feature(cache, "excluded");
        var category = AddCategory(cache, "Word");
        var other = Feature(cache, "other");
        var id = Author(cache, Intent(cache,
            new([Seg(cache, "m")], [new(Phoneme: Seg(cache, "a").Phoneme, Minimum: 0, Maximum: 1)], [], [required], [excluded]),
            new([Seg(cache, "t")], [], [])));
        var rule = (IPhRegularRule)Get<IPhSegmentRule>(cache, id);
        var rhs = rule.RightHandSidesOS[0];
        var expected = Wire(cache, id);
        var replacement = new JsonArray(expected["rightHandSides"]![1]!.DeepClone());
        var proposal = Proposal(EditPhonologicalRuleComposer.Build(cache, Edit(id, replacement, expected)).Reverse().ToArray());
        var anchor = bound ? ScratchDryRun.Of(cache, proposal).Anchor : null;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            switch (drift)
            {
                case "category": rhs.InputPOSesRC.Add(Get<IPartOfSpeech>(cache, category)); break;
                case "required": rhs.ReqRuleFeatsRC.Clear(); break;
                case "excluded": rhs.ExclRuleFeatsRC.Clear(); break;
                case "required-item": rhs.ReqRuleFeatsRC.Single().ItemRA = Get<ICmObject>(cache, other); break;
                case "excluded-item": rhs.ExclRuleFeatsRC.Single().ItemRA = Get<ICmObject>(cache, other); break;
                case "context":
                    ((IPhSimpleContextSeg)((IPhIterationContext)rhs.LeftContextOA).MemberRA).FeatureStructureRA =
                        Get<IPhPhoneme>(cache, Seg(cache, "u").Phoneme!.Value); break;
                case "nested-context": ((IPhIterationContext)rhs.LeftContextOA).Maximum = 2; break;
                case "output": ((IPhSimpleContextSeg)rhs.StrucChangeOS[0]).FeatureStructureRA =
                    Get<IPhPhoneme>(cache, Seg(cache, "u").Phoneme!.Value); break;
            }
        });
        var changed = Wire(cache, id).ToJsonString();
        var graph = rule.AllOwnedObjects.Select(o => o.Guid).Order().ToArray();
        var applied = ProjectAppliedLog.ReadAll(cache).Count;
        if (bound)
        {
            Assert.NotEqual(anchor!.FootprintDigest, FootprintProbe.ComputeCurrentFootprintDigest(cache, proposal));
            var refusal = Assert.Throws<PreconditionRefusal>(() => ProposalApplier.Apply(cache, proposal, anchor, "outcome-tests"));
            Assert.Equal(PreconditionKind.FootprintDrift, refusal.Kind);
        }
        Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(cache, proposal));
        Assert.Equal(changed, Wire(cache, id).ToJsonString());
        Assert.Equal(graph, rule.AllOwnedObjects.Select(o => o.Guid).Order());
        Assert.Equal(applied, ProjectAppliedLog.ReadAll(cache).Count);
        Assert.True(rhs.IsValidObject);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("same-rule")]
    [InlineData("other-rule")]
    [InlineData("process")]
    public void ReplacingTheLastRhsAlphaUseCollectsOnlyUnreferencedConstraintsAfterSaveAndReopen(string remaining)
    {
        using var cache = pristine.NewScratch();
        var f = new FeaturedSegments(cache);
        var variable = new PhonologicalRuleVariable("alpha", f.Place);
        var alpha = new PhonologicalRuleItem(NaturalClass: f.Nasals, Variables: [variable]);
        var plain = new PhonologicalRuleItem(NaturalClass: f.Stops);
        var id = Author(cache, new("alpha retirement", PhonologicalRuleDirection.Simultaneous, [plain], [], [], [],
            RightHandSides: [new([alpha], [], []), new([plain], [], [])]));
        var constraint = Assert.Single(cache.LangProject.PhonologicalDataOA.FeatConstraintsOS);
        var constraintId = Id(constraint);
        if (remaining is "other-rule" or "process")
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var context = cache.ServiceLocator.GetInstance<IPhSimpleContextNCFactory>().Create();
                if (remaining == "other-rule")
                {
                    var other = cache.ServiceLocator.GetInstance<IPhRegularRuleFactory>().Create();
                    cache.LangProject.PhonologicalDataOA.PhonRulesOS.Add(other);
                    other.StrucDescOS.Add(context);
                }
                else
                {
                    var process = cache.ServiceLocator.GetInstance<IMoAffixProcessFactory>().Create();
                    cache.LangProject.LexDbOA.Entries.First().AlternateFormsOS.Add(process);
                    process.InputOS.Add(context);
                }
                context.FeatureStructureRA = Get<IPhNaturalClass>(cache, f.Nasals);
                context.PlusConstrRS.Add(constraint);
            });
        var expected = Wire(cache, id);
        var outcomes = expected["rightHandSides"]!.AsArray();
        var replacement = new JsonArray(outcomes[remaining == "same-rule" ? 0 : 1]!.DeepClone());
        ApplyOps(cache, EditPhonologicalRuleComposer.Build(cache, Edit(id, replacement, expected)).Reverse().ToArray());
        Assert.Equal(remaining != "none", Exists(cache, constraintId));
        new FwDataProjectLoader().Save(cache);
        using var reopened = new FwDataProjectLoader().LoadScratchCache(cache.ProjectId.Path);
        Assert.Equal(remaining != "none", Exists(reopened, constraintId));
        if (remaining != "none") Assert.NotEmpty(Get<IPhFeatureConstraint>(reopened, constraintId).ReferringObjects);
    }

    [Fact]
    public void ACompleteLegacyGuardCanConvertOneOutcomeIntoSeveral()
    {
        using var cache = Project();
        var id = Author(cache, new("single", PhonologicalRuleDirection.Simultaneous, [Seg(cache, "n")], [Seg(cache, "m")], [], []));
        var expected = Wire(cache, id);
        Assert.Null(expected["rightHandSides"]);
        var outcomes = PhonologicalRuleReader.OutcomesJson([new([Seg(cache, "m")], [], []), new([Seg(cache, "t")], [], [])]);
        ApplyOps(cache, EditPhonologicalRuleComposer.Build(cache, Edit(id, outcomes, expected)));
        Assert.Equal(2, ((IPhRegularRule)Get<IPhSegmentRule>(cache, id)).RightHandSidesOS.Count);
    }

    [Theory]
    [InlineData("overlap")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("ambiguous")]
    public void InvalidLaterOutcomesAreRefusedBeforeAnyIdentityIsMinted(string scenario)
    {
        using var cache = Project();
        var feature = Feature(cache, "tag");
        var bad = scenario switch
        {
            "overlap" => new PhonologicalRuleOutcome([], [], [], [feature], [feature]),
            "duplicate" => new([], [], [], [feature, feature]),
            "unknown" => new([], [], [], [Seg(cache, "n").Phoneme!.Value]),
            _ => new([], [], []),
        };
        var intent = Intent(cache, new([], [], []), bad);
        if (scenario == "ambiguous") intent = intent with { Output = [Seg(cache, "m")] };
        var minted = 0;
        Assert.Throws<InvalidOperationException>(() => AuthorPhonologicalRuleComposer.Build(cache, intent,
            () => { minted++; return CanonicalId.Mint(); }));
        Assert.Equal(0, minted);
        Assert.Empty(cache.LangProject.PhonologicalDataOA.PhonRulesOS);
    }

    [Theory]
    [InlineData(24)]
    [InlineData(25)]
    public void TheWholeRuleRespectsFieldWorksTwentyFourVariableLoaderLimit(int count)
    {
        using var cache = pristine.NewScratch();
        var f = new FeaturedSegments(cache);
        var input = Enumerable.Range(0, 8).Select(i => new PhonologicalRuleItem(NaturalClass: f.Nasals,
            Variables: [new($"v{i}", f.Place)])).ToArray();
        PhonologicalRuleOutcome Outcome(int start, bool extra) => new(
            Enumerable.Range(start, 8).Select(i => new PhonologicalRuleItem(Change: [], Variables: [new($"v{i}", f.Place)])).ToArray(),
            [], extra ? [new(NaturalClass: f.Stops, Variables: [new("last", f.Place)])] : []);
        var intent = new AuthorPhonologicalRuleIntent("many variables", PhonologicalRuleDirection.Simultaneous,
            input, [], [], [], RightHandSides: [Outcome(8, false), Outcome(16, count == 25)]);
        var minted = 0;
        if (count == 25)
        {
            var error = Assert.Throws<InvalidOperationException>(() => AuthorPhonologicalRuleComposer.Build(cache, intent,
                () => { minted++; return CanonicalId.Mint(); }));
            Assert.Contains("24", error.Message);
            Assert.Equal(0, minted);
        }
        else
        {
            Author(cache, intent);
            Assert.Equal(24, cache.LangProject.PhonologicalDataOA.FeatConstraintsOS.Count);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FeatureChangesAndAlphaSharingSurviveOutcomeReplacementWithAndWithoutReplacedInput(bool inputReference, bool replaceInput)
    {
        using var cache = pristine.NewScratch();
        var f = new FeaturedSegments(cache);
        var input = new PhonologicalRuleItem(NaturalClass: f.Nasals,
            Variables: inputReference ? [new("alpha", f.Place)] : null);
        var output = new PhonologicalRuleItem(Change: [], Variables: [new("alpha", f.Place)]);
        var id = Author(cache, new("alpha outcomes", PhonologicalRuleDirection.Simultaneous, [input], [], [], [],
            RightHandSides: [new([output], [], [new(NaturalClass: f.Stops, Variables: [new("alpha", f.Place)])]),
                new([new(Change: [new(f.Surface.Feature, f.Surface.Value)], Variables: [new("alpha", f.Place, "-")])], [], [])]));
        var data = cache.LangProject.PhonologicalDataOA;
        var constraint = Assert.Single(data.FeatConstraintsOS);
        var oldConstraint = constraint.Guid;
        Assert.Equal(oldConstraint, Assert.Single(((IPhSimpleContextNC)((IPhRegularRule)Get<IPhSegmentRule>(cache, id))
            .RightHandSidesOS[1].StrucChangeOS.Single()).MinusConstrRS).Guid);
        var loader = new FwDataProjectLoader();
        loader.Save(cache);
        using var reopened = loader.LoadScratchCache(cache.ProjectId.Path);
        var wire = Wire(reopened, id);
        var editWire = new JsonObject
        {
            ["rule"] = id.Value, ["rightHandSides"] = wire["rightHandSides"]!.DeepClone(), ["expected"] = wire.DeepClone(),
        };
        if (replaceInput) editWire["input"] = wire["input"]!.DeepClone();
        var edit = EditPhonologicalRuleIntentParser.Parse(Json(editWire));
        ApplyOps(reopened, EditPhonologicalRuleComposer.Build(reopened, edit).Reverse().ToArray());
        var after = PhonologicalRuleReader.Read(Get<IPhSegmentRule>(reopened, id))!;
        var names = after.Outcomes.SelectMany(o => o.Output).SelectMany(i => i.Variables ?? []).Select(v => v.Variable).Distinct().ToArray();
        Assert.Single(names);
        Assert.Equal(names[0], after.Outcomes[0].Right.Single().Variables!.Single().Variable);
        if (inputReference) Assert.Equal(names[0], after.Input.Single().Variables!.Single().Variable);
        Assert.Equal(CanonicalId.FromGuid(oldConstraint).Value, names[0]);
        Assert.Equal("-", after.Outcomes[1].Output.Single().Variables!.Single().Polarity);
        loader.Save(reopened);
    }
}
