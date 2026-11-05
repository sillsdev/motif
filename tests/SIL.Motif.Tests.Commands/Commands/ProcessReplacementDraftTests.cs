using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Worker.Store;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class ProcessReplacementDraftTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    [Fact]
    public async Task OneDraftRetargetsTwoProhibitionsAndReplacesTheWholeProcessGraph() =>
        await new ProcessReplacementProof(pristine, output).Run(false);
}

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class ProcessReplacementParserTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    [RealParserFact]
    public async Task TheDefaultRealParserKeepsUnaffectedReadingsAndBothProhibitionsAfterArchiphonemeReplacement() =>
        await new ProcessReplacementProof(pristine, output).Run(true);
}

internal sealed class ProcessReplacementProof(PristineProjectFixture pristine, ITestOutputHelper output)
{
    private const string Version = "0.1.0";
    private const string Draft = "plain-nasal";

    public async Task Run(bool parse)
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "p", "a", "n", "N", "b", "g", "o");
        var ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var analysisWs = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        IPartOfSpeech category = null!;
        IMoInflAffixSlot slot = null!;
        var stems = new Dictionary<string, ILexEntry>();
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            category = cache.ServiceLocator.GetInstance<IPartOfSpeechFactory>().Create();
            cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(category);
            category.Name.set_String(cache.DefaultAnalWs, "Synthetic verb");
            slot = cache.ServiceLocator.GetInstance<IMoInflAffixSlotFactory>().Create();
            category.AffixSlotsOC.Add(slot);
            slot.Optional = true;
            var template = cache.ServiceLocator.GetInstance<IMoInflAffixTemplateFactory>().Create();
            category.AffixTemplatesOS.Add(template);
            template.Final = true;
            template.PrefixSlotsRS.Add(slot);
            foreach (var text in new[] { "bago", "gaba", "baba" })
                stems[text] = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create(
                    cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem),
                    TsStringUtils.MakeString(text, cache.DefaultVernWs), text, SandboxGenericMSA.Create(MsaType.kStem, category));
        });
        var phones = cache.LangProject.PhonologicalDataOA.PhonemeSetsOS[0].PhonemesOC.SelectMany(phone => phone.CodesOS.Select(code =>
                (text: code.Representation.get_String(cache.DefaultVernWs).Text, id: CanonicalId.FromGuid(phone.Guid))))
            .ToDictionary(pair => pair.text, pair => pair.id);
        var initialClass = cache.ServiceLocator.GetInstance<IPhNCSegmentsFactory>().Create();
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Add(initialClass);
            initialClass.Abbreviation.set_String(cache.DefaultAnalWs, "BG");
            initialClass.SegmentsRC.Add((IPhPhoneme)cache.ServiceLocator.ObjectRepository.GetObject(phones["b"].ToGuid()));
            initialClass.SegmentsRC.Add((IPhPhoneme)cache.ServiceLocator.ObjectRepository.GetObject(phones["g"].ToGuid()));
        });
        var processOperations = AuthorAffixProcessComposer.Build(cache, new(CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphPrefix),
            "nasal", analysisWs, new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, Id(category), Slots: [Id(slot)]),
            [new("initial", NaturalClass: Id(initialClass)), new("rest", Remaining: true)],
            [new(Insert: [phones["p"], phones["a"], phones["n"]]), new(Copy: "initial"), new(Copy: "rest")]));
        Execute(cache, processOperations);
        var entryId = processOperations.Single(operation => operation.Kind == LexDbEntriesOperationKinds.Create).EntityId!.Value;
        var processId = processOperations.Single(operation => operation.Kind == LexEntryLexemeFormOperationKinds.CreateLexemeForm).EntityId!.Value;
        var process = (IMoAffixProcess)cache.ServiceLocator.ObjectRepository.GetObject(processId.ToGuid());
        var alternateProcessOperations = AuthorAffixProcessAllomorphComposer.Build(cache,
            new(entryId, [new("initial", Phoneme: phones["o"]), new("rest", Remaining: true)],
                [new(Insert: [phones["p"], phones["a"], phones["n"]]), new(Copy: "rest")]));
        Execute(cache, alternateProcessOperations);
        var alternateProcessId = alternateProcessOperations.Single(operation =>
            operation.Kind == LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms).EntityId!.Value;
        var alternateProcess = (IMoAffixProcess)cache.ServiceLocator.ObjectRepository.GetObject(alternateProcessId.ToGuid());
        var graph = OwnedGraphGuard.Members(process).Concat(OwnedGraphGuard.Members(alternateProcess)).Select(Id).ToArray();
        var entry = (ILexEntry)process.Owner;
        var senses = entry.SensesOS.Select(Id).ToArray();
        var analyses = entry.MorphoSyntaxAnalysesOC.Select(Id).ToArray();
        var rules = new List<IMoAlloAdhocProhib>();
        foreach (var stem in new[] { "gaba", "baba" })
        {
            var operations = AuthorAdhocProhibitionComposer.Build(cache,
                new("allomorph", processId, [Id(stems[stem].LexemeFormOA)], "anywhere"));
            Execute(cache, operations);
            rules.Add((IMoAlloAdhocProhib)cache.ServiceLocator.ObjectRepository.GetObject(operations.Single().EntityId!.Value.ToGuid()));
        }
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        using var invoker = new PanGlossInvoker();
        var names = stems.ToDictionary(pair => Id(pair.Value.LexemeFormOA), pair => pair.Key);
        names[processId] = "NASAL";
        if (parse) await CheckReadings(invoker, path, names);
        Assert.True(ProposalCommands.New(new(path, Version, Draft, "Replace the process without losing restrictions")).Succeeded);
        var states = rules.Select(AdhocReferenceState.Read).ToArray();
        var composed = ProposalCommands.Compose(new(path, Version, Draft,
        [
            Step("compose-author-phonological-rule", new { name = "Realize nasal archiphoneme", direction = "left-to-right",
                input = new[] { new { phoneme = phones["N"].Value } }, output = new[] { new { phoneme = phones["n"].Value } } }),
            Step("compose-author-alternate-form", new { entry = entryId.Value, form = "paN", formWs = ws,
                morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphPrefix).Value }, "plain"),
            Step("compose-edit-adhoc-prohibition-references", new { target = Id(rules[0]).Value, expected = states[0].Wire(),
                references = Desired(states[0]) }),
            Step("compose-edit-adhoc-prohibition-references", new { target = Id(rules[1]).Value, expected = states[1].Wire(),
                references = Desired(states[1]) }),
            Step("compose-delete-affix-process", new { entry = entryId.Value, form = alternateProcessId.Value, replacement = "@plain" }),
            Step("compose-delete-affix-process", new { entry = entryId.Value, form = processId.Value, replacement = "@plain" }),
        ]));
        Assert.True(composed.Succeeded, composed.Refusal?.Message);
        Assert.True(DraftRationale.Write(path, Version, Draft, "Use a sound rule for the nasal while preserving both restrictions.").Succeeded);
        var finalized = ProposalCommands.Finalize(new(path, Version, Draft));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var stored = ProjectStoreCommand.Run<Proposal>(path, Version, (database, _) => CommandOutcome<Proposal>.Success(
            ProposalJsonParser.Parse(new ProposalRepository(database).Get(CanonicalId.Parse(finalized.Value!.ProposalId)).ProposalJson!)));
        Assert.True(stored.Succeeded, stored.Refusal?.Message);
        var proposal = stored.Value!;
        var deletion = proposal.Operations.Single(operation => operation.Kind == AffixProcessDeleteOperationKinds.Delete && operation.Target == processId);
        var edits = proposal.Operations.Where(operation => operation.Kind == AdhocReferenceOperationKinds.Edit).ToArray();
        Assert.All(edits, edit => Assert.Contains(deletion.DependsOn, dependency => dependency.OperationId == edit.OperationId));
        var shuffled = new Proposal(proposal.ContractVersions, proposal.ProposalId, proposal.Requires, proposal.Operations.Reverse().ToArray());
        using var scratch = DryRunScratch.Adopt(new FwDataProjectLoader().LoadScratchCache(path), "process replacement");
        var dryRun = ProposalDryRunner.Run(scratch, shuffled);
        Assert.Contains(dryRun.ExpectedEffects, effect => effect.Preview?.GetRawText().Contains("deletedProcess") == true);
        Assert.Equal(2, dryRun.ExpectedEffects.Count(effect => effect.Preview?.GetRawText().Contains("retargetedProhibition") == true));
        Assert.DoesNotContain(dryRun.ExpectedEffects, effect => senses.Concat(analyses).Append(entryId).Contains(effect.CanonicalId) &&
            effect.Field == "class" && effect.After.Count == 0);
        var text = SIL.Motif.Projection.Rendering.CommandTextRenderer.Render(
            SIL.Motif.Projection.DryRunProjectionBuilder.Build(proposal.ProposalId.Value, dryRun, shuffled));
        Assert.Contains("Delete affix process", text);
        Assert.Contains("Retarget ad hoc prohibition", text);
        Assert.Contains("Keep the entry, senses, grammatical analyses", text);
        ProposalApplier.Apply(cache, shuffled, dryRun.Anchor, "process-proof");
        Assert.All(graph, id => Assert.False(cache.ServiceLocator.ObjectRepository.IsValidObjectId(id.ToGuid())));
        Assert.Equal(senses, entry.SensesOS.Select(Id));
        Assert.Equal(analyses, entry.MorphoSyntaxAnalysesOC.Select(Id));
        var plain = Assert.IsAssignableFrom<IMoAffixAllomorph>(entry.LexemeFormOA);
        Assert.Equal("paN", plain.Form.get_String(cache.DefaultVernWs).Text);
        Assert.All(rules, rule => Assert.Equal(plain.Guid, rule.FirstAllomorphRA.Guid));
        names.Remove(processId);
        names[Id(plain)] = "NASAL";
        new FwDataProjectLoader().Save(cache);
        using (var reopened = new FwDataProjectLoader().LoadScratchCache(path))
            Assert.Equal(plain.Guid, ((ILexEntry)reopened.ServiceLocator.ObjectRepository.GetObject(entryId.ToGuid())).LexemeFormOA.Guid);
        if (parse) await CheckReadings(invoker, path, names);
    }

    private async Task CheckReadings(PanGlossInvoker invoker, string path, IReadOnlyDictionary<CanonicalId, string> names)
    {
        var tracer = new PanGlossTracer(invoker);
        foreach (var (word, expected) in new Dictionary<string, string[]>
        {
            ["panbago"] = ["NASAL+bago"], ["bago"] = ["bago"], ["gaba"] = ["gaba"], ["baba"] = ["baba"],
            ["pangaba"] = [], ["panbaba"] = [], ["paNbago"] = [],
        })
        {
            var result = Assert.IsType<PanGlossTraceOutcome.Completed>(await tracer.TraceAsync(path, word, CancellationToken.None));
            Assert.True(result.Details!.SearchCompleted, word);
            Assert.False(result.Details.Capped || result.Details.TimedOut || result.Details.InvalidShape, word);
            var readings = result.Document!.Analyses.Select(analysis => string.Join("+", analysis.Morphs.Select(morph =>
                names.GetValueOrDefault(CanonicalId.FromGuid(Guid.Parse(morph.FormId!)), "?")))).Distinct().Order().ToArray();
            output.WriteLine($"{word}: {string.Join(", ", readings)}");
            Assert.Equal(expected, readings);
        }
    }

    private static object Desired(AdhocReferenceState state) => new { kind = state.Kind, first = "@plain",
        rest = state.Rest.Select(id => id.Value).ToArray(), allomorphs = Array.Empty<string>(), adjacency = state.Adjacency, disabled = state.Disabled };
    private static ComposeStepRequest Step(string verb, object intent, string? label = null) => new(verb, JsonSerializer.SerializeToElement(intent), label, null);
    private static CanonicalId Id(ICmObject value) => CanonicalId.FromGuid(value.Guid);
    private static void Execute(LcmCache cache, IReadOnlyList<OperationEnvelope> operations)
    {
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            foreach (var operation in OperationExecutionOrder.Sort(operations))
                OperationHandlerRegistry.Resolve(operation.Kind, "process fixture").ApplyAndCaptureEffect(cache, operation, []);
        });
    }
}
