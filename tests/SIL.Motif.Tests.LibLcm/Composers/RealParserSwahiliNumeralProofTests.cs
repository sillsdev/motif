using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.LiveHost.Baselines;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RealParserSwahiliNumeralProofTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    private const string ProductVersion = "0.1.0";
    private const int RightToLeftIterative = 2;

    [RealParserFact]
    public async Task NumeralClassEditorsPreserveConcordAndGiveInvariantNumeralsOneZeroAnalysis()
    {
        using var cache = pristine.NewScratch();
        var fixture = SeedBaseline(cache);
        AssertBaselineGraph(cache, fixture);
        var beforeDigest = BaselineSemanticDigest.Compute(cache);
        var projectPath = cache.ProjectId.Path;
        var analysisWs = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        var vernacularWs = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        new FwDataProjectLoader().Save(cache);
        cache.Dispose();

        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);
        var baselineSita = await CompletedTrace(tracer, projectPath, "sita");
        WriteWitness(output, "baseline sita", AssertReading(baselineSita, fixture.NPrefix, fixture.Sita,
            prefixCategory: fixture.Adjective, stemCategory: fixture.Invariable));
        var baselineWawili = await CompletedTrace(tracer, projectPath, "wawili");
        WriteWitness(output, "baseline wawili", AssertReading(baselineWawili, fixture.WaPrefix, fixture.Wili,
            prefixCategory: fixture.Adjective, stemCategory: fixture.Numeral));
        var baselineMbili = await CompletedTrace(tracer, projectPath, "mbili");
        WriteWitness(output, "baseline mbili", AssertReading(baselineMbili, fixture.NPrefix, fixture.Wili,
            prefixCategory: fixture.Adjective, stemCategory: fixture.Numeral));
        foreach (var word in new[] { "ishirini", "arobaini" })
        {
            AssertNoAnalyses(await CompletedTrace(tracer, projectPath, word));
            output.WriteLine($"baseline {word}: completed uncapped search; no analyses");
        }

        const string draftName = "swahili-numeral-classes";
        Assert.True(ProposalCommands.New(new NewDraftRequest(projectPath, ProductVersion, draftName,
            "Use numeral concord and invariant classes.")).Succeeded);
        var concordClass = AuthorClass(projectPath, draftName, fixture.Adjective, analysisWs, "CONCORD", "C");
        var invarClass = AuthorClass(projectPath, draftName, fixture.Adjective, analysisWs, "INVAR", "I");

        var editorBatch = new object[]
        {
            new
            {
                verb = "compose-edit-part-of-speech-inflection-class-default",
                intent = new
                {
                    target = fixture.Adjective.Value,
                    expectedInflectionClass = (string?)null,
                    inflectionClass = concordClass.Value,
                },
            },
            new
            {
                verb = "compose-edit-part-of-speech-inflection-class-default",
                intent = new
                {
                    target = fixture.Invariable.Value,
                    expectedInflectionClass = (string?)null,
                    inflectionClass = invarClass.Value,
                },
            },
            new
            {
                verb = "compose-edit-affix-inflection-classes",
                intent = new
                {
                    target = fixture.NPrefix.Form.Value,
                    expectedInflectionClasses = Array.Empty<string>(),
                    inflectionClasses = new[] { concordClass.Value },
                },
            },
            new
            {
                verb = "compose-edit-affix-inflection-classes",
                intent = new
                {
                    target = fixture.WaPrefix.Form.Value,
                    expectedInflectionClasses = Array.Empty<string>(),
                    inflectionClasses = new[] { concordClass.Value },
                },
            },
        };
        var editors = ProposalCommands.ComposeBatch(new ComposeBatchRequest(projectPath, ProductVersion, draftName,
            JsonSerializer.Serialize(new { intents = editorBatch })));
        Assert.True(editors.Succeeded, editors.Refusal?.Message);

        var zeroPrefix = ProposalCommands.ComposeAuthorLexicalEntry(new ComposeAuthorLexicalEntryRequest(
            projectPath, ProductVersion, draftName, JsonSerializer.Serialize(new
            {
                form = "∅",
                formWs = vernacularWs,
                morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphPrefix).Value,
                gloss = "invariable agreement",
                analysisWs,
                msa = new
                {
                    kind = "inflectional",
                    category = fixture.Invariable.Value,
                    slots = new[] { fixture.Slot.Value },
                },
                inflectionClasses = new[] { invarClass.Value },
            })));
        Assert.True(zeroPrefix.Succeeded, zeroPrefix.Refusal?.Message);
        var zero = MorphFrom(zeroPrefix.Value!, fixture.Invariable);

        var finalized = ProposalCommands.Finalize(new FinalizeRequest(projectPath, ProductVersion, draftName));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var proposalId = CanonicalId.Parse(finalized.Value!.ProposalId);
        var loaded = ProjectStoreCommand.Run<Proposal>(projectPath, ProductVersion, (database, _) =>
            CommandOutcome<Proposal>.Success(ProposalJsonParser.Parse(new ProposalRepository(database)
                .Get(proposalId).ProposalJson!)));
        Assert.True(loaded.Succeeded, loaded.Refusal?.Message);
        var proposal = loaded.Value!;
        AssertClassDependencies(proposal, concordClass, invarClass, fixture, zero);

        var reversed = new Proposal(proposal.ContractVersions, proposal.ProposalId, proposal.Requires,
            proposal.Operations.Reverse().ToArray(), proposal.Extensions);
        using (var reverseTarget = new FwDataProjectLoader().LoadScratchCache(projectPath))
        {
            var reverseDryRun = DryRun(reverseTarget, reversed);
            ProposalApplier.Apply(reverseTarget, reversed, reverseDryRun.Anchor, "numeral-reversed-array");
            AssertApplied(reverseTarget, fixture, concordClass, invarClass, zero);
        }

        var dryRun = DryRunJobRunner.Run(projectPath, ProductVersion, proposalId.Value);
        Assert.True(dryRun.Succeeded, dryRun.Refusal?.Message);
        using (var unchanged = new FwDataProjectLoader().LoadScratchCache(projectPath))
            Assert.Equal(beforeDigest, BaselineSemanticDigest.Compute(unchanged));
        var applied = ProposalCommands.Apply(new ApplyRequest(projectPath, ProductVersion, proposalId.Value, Force: true));
        Assert.True(applied.Succeeded, applied.Refusal?.Message);
        Assert.NotEmpty(applied.Value!.Effects);

        using (var reopened = new FwDataProjectLoader().LoadCache(projectPath))
            AssertApplied(reopened, fixture, concordClass, invarClass, zero);

        foreach (var (word, prefix, stem) in new[]
                 {
                     ("sita", zero, fixture.Sita),
                     ("ishirini", zero, fixture.Ishirini),
                     ("arobaini", zero, fixture.Arobaini),
                     ("wawili", fixture.WaPrefix, fixture.Wili),
                     ("mbili", fixture.NPrefix, fixture.Wili),
                     ("wazuri", fixture.WaPrefix, fixture.Zuri),
                 })
        {
            var trace = await CompletedTrace(tracer, projectPath, word);
            var prefixCategory = prefix == zero ? fixture.Invariable : fixture.Adjective;
            var stemCategory = word == "wazuri" ? fixture.Adjective : fixture.Numeral;
            if (prefix == zero)
                stemCategory = fixture.Invariable;
            WriteWitness(output, word, AssertReading(trace, prefix, stem, prefixCategory, stemCategory));
        }

        Assert.NotEqual(zero.Form, fixture.NPrefix.Form);
        Assert.NotEqual(zero.Form, fixture.WaPrefix.Form);
        foreach (var word in new[] { "wasita", "wili", "zuri" })
        {
            AssertNoAnalyses(await CompletedTrace(tracer, projectPath, word));
            output.WriteLine($"{word}: completed uncapped search; no analyses");
        }
    }

    private static CanonicalId AuthorClass(string projectPath, string draftName, CanonicalId category,
        string analysisWs, string name, string abbreviation)
    {
        var response = ProposalCommands.ComposeAuthorInflectionClass(new ComposeAuthorInflectionClassRequest(
            projectPath, ProductVersion, draftName,
            JsonSerializer.Serialize(new { category = category.Value, name, ws = analysisWs, abbreviation })));
        Assert.True(response.Succeeded, response.Refusal?.Message);
        return CanonicalId.Parse(Assert.Single(response.Value!.Operations,
            operation => operation.Kind == PartOfSpeechInflectionClassesOperationKinds.Create).EntityId!);
    }

    private static void AssertClassDependencies(Proposal proposal, CanonicalId concordClass,
        CanonicalId invarClass, NumeralFixture fixture, Morph zero)
    {
        var creators = proposal.Operations.Where(operation =>
                operation.Kind == PartOfSpeechInflectionClassesOperationKinds.Create)
            .ToDictionary(operation => operation.EntityId!.Value, operation => operation.OperationId);
        var concordCreator = creators[concordClass];
        var invarCreator = creators[invarClass];
        var defaults = proposal.Operations.Where(operation =>
            operation.Kind.Contains("DefaultInflectionClass", StringComparison.Ordinal)).ToArray();
        var concordDefault = Assert.Single(defaults.Where(operation => operation.Target == fixture.Adjective));
        var invarDefault = Assert.Single(defaults.Where(operation => operation.Target == fixture.Invariable));
        Assert.Contains(concordCreator, concordDefault.DependsOn.Select(dependency => dependency.OperationId));
        Assert.Contains(invarCreator, invarDefault.DependsOn.Select(dependency => dependency.OperationId));
        foreach (var form in new[] { fixture.NPrefix.Form, fixture.WaPrefix.Form })
        {
            var edit = Assert.Single(proposal.Operations.Where(operation =>
                operation.Kind == MoAffixFormInflectionClassesOperationKinds.AddRefInflectionClasses &&
                operation.Target == form));
            Assert.Contains(concordCreator, edit.DependsOn.Select(dependency => dependency.OperationId));
        }
        var zeroClassWrite = Assert.Single(proposal.Operations.Where(operation =>
            operation.Kind == MoAffixFormInflectionClassesOperationKinds.AddRefInflectionClasses &&
            operation.Target == zero.Form));
        Assert.Contains(invarCreator, zeroClassWrite.DependsOn.Select(dependency => dependency.OperationId));
    }

    private static Morph MorphFrom(ComposedOperationsResponse response, CanonicalId category)
    {
        var form = CanonicalId.Parse(Assert.Single(response.Operations,
            operation => operation.Kind == LexEntryLexemeFormOperationKinds.CreateLexemeForm).EntityId!);
        var msa = CanonicalId.Parse(Assert.Single(response.Operations,
            operation => operation.Kind == LexEntryMorphoSyntaxAnalysesOperationKinds.Create).EntityId!);
        return new Morph(default, form, msa, category);
    }

    private static NumeralFixture SeedBaseline(LcmCache cache)
    {
        RealParserProject.PrepareForParsing(cache, "a", "b", "h", "i", "l", "m", "n", "o", "r", "s", "t", "w", "y", "z", "u");
        var phonemes = Phonemes(cache);
        IPartOfSpeech adjective = null!;
        IPartOfSpeech numeral = null!;
        IPartOfSpeech invariable = null!;
        IMoInflAffixSlot slot = null!;
        Morph nPrefix = null!;
        Morph waPrefix = null!;
        Morph sita = null!;
        Morph wili = null!;
        Morph ishirini = null!;
        Morph arobaini = null!;
        Morph zuri = null!;
        CanonicalId template = default;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            adjective = CreateCategory(cache, null, "Adjective");
            numeral = CreateCategory(cache, adjective, "Numeral");
            invariable = CreateCategory(cache, numeral, "Invariable numeral");
            slot = CreateSlot(cache, adjective, "S-AC");
            template = CreateTemplate(cache, adjective, "T-ADJ", slot);
            nPrefix = CreateMorph(cache, MoMorphTypeTags.kguidMorphPrefix, "n", "nasal concord", adjective, slot);
            waPrefix = CreateMorph(cache, MoMorphTypeTags.kguidMorphPrefix, "wa", "class 2 concord", adjective, slot);
            sita = CreateMorph(cache, MoMorphTypeTags.kguidMorphStem, "sita", "six", invariable, null);
            wili = CreateMorph(cache, MoMorphTypeTags.kguidMorphStem, "wili", "two", numeral, null);
            ishirini = CreateMorph(cache, MoMorphTypeTags.kguidMorphStem, "ishirini", "twenty",
                invariable, null);
            arobaini = CreateMorph(cache, MoMorphTypeTags.kguidMorphStem, "arobaini", "forty",
                invariable, null);
            zuri = CreateMorph(cache, MoMorphTypeTags.kguidMorphStem, "zuri", "good", adjective, null);

            var coronal = cache.ServiceLocator.GetInstance<IPhNCSegmentsFactory>().Create();
            cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Add(coronal);
            coronal.Name.set_String(cache.DefaultAnalWs, "Coronal consonants");
            coronal.Abbreviation.set_String(cache.DefaultAnalWs, "COR");
            coronal.SegmentsRC.Add(phonemes["s"]);
            coronal.SegmentsRC.Add(phonemes["t"]);
            AddRule(cache, "Delete n before s or t", phonemes["n"], null,
                rightNaturalClass: coronal);
            AddRule(cache, "n becomes m before w", phonemes["n"], phonemes["m"],
                rightPhoneme: phonemes["w"]);
            AddRule(cache, "w becomes b after m", phonemes["w"], phonemes["b"],
                leftPhoneme: phonemes["m"]);
        });

        return new NumeralFixture(CanonicalId.FromGuid(adjective.Guid), CanonicalId.FromGuid(numeral.Guid),
            CanonicalId.FromGuid(invariable.Guid), CanonicalId.FromGuid(slot.Guid), template, nPrefix,
            waPrefix, sita, wili, ishirini, arobaini, zuri);
    }

    private static IPartOfSpeech CreateCategory(LcmCache cache, IPartOfSpeech? parent, string name)
    {
        var category = cache.ServiceLocator.GetInstance<IPartOfSpeechFactory>().Create();
        if (parent is null)
            cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(category);
        else
            parent.SubPossibilitiesOS.Add(category);
        category.Name.set_String(cache.DefaultAnalWs, name);
        return category;
    }

    private static IMoInflAffixSlot CreateSlot(LcmCache cache, IPartOfSpeech category, string name)
    {
        var slot = cache.ServiceLocator.GetInstance<IMoInflAffixSlotFactory>().Create();
        category.AffixSlotsOC.Add(slot);
        slot.Name.set_String(cache.DefaultAnalWs, name);
        return slot;
    }

    private static CanonicalId CreateTemplate(LcmCache cache, IPartOfSpeech category, string name,
        IMoInflAffixSlot slot)
    {
        var template = cache.ServiceLocator.GetInstance<IMoInflAffixTemplateFactory>().Create();
        category.AffixTemplatesOS.Add(template);
        template.Name.set_String(cache.DefaultAnalWs, name);
        template.Final = true;
        template.PrefixSlotsRS.Add(slot);
        return CanonicalId.FromGuid(template.Guid);
    }

    private static Morph CreateMorph(LcmCache cache, Guid morphType, string form, string gloss,
        IPartOfSpeech category, IMoInflAffixSlot? slot)
    {
        var type = cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>().GetObject(morphType);
        var msaType = morphType == MoMorphTypeTags.kguidMorphStem ? MsaType.kStem : MsaType.kInfl;
        var entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create(type,
            TsStringUtils.MakeString(form, cache.DefaultVernWs), gloss,
            SandboxGenericMSA.Create(msaType, category));
        var msa = entry.MorphoSyntaxAnalysesOC.Single();
        if (slot is not null)
            Assert.IsAssignableFrom<IMoInflAffMsa>(msa).SlotsRC.Add(slot);
        return new Morph(CanonicalId.FromGuid(entry.Guid), CanonicalId.FromGuid(entry.LexemeFormOA!.Guid),
            CanonicalId.FromGuid(msa.Guid), CanonicalId.FromGuid(category.Guid));
    }

    private static Dictionary<string, IPhPhoneme> Phonemes(LcmCache cache) =>
        cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.Single().PhonemesOC
            .SelectMany(phoneme => phoneme.CodesOS.Select(code =>
                (Text: code.Representation.get_String(cache.DefaultVernWs)?.Text, Phoneme: phoneme)))
            .Where(item => item.Text is not null)
            .ToDictionary(item => item.Text!, item => item.Phoneme, StringComparer.Ordinal);

    private static void AddRule(LcmCache cache, string name, IPhPhoneme input, IPhPhoneme? output,
        IPhPhoneme? leftPhoneme = null, IPhPhoneme? rightPhoneme = null,
        IPhNCSegments? rightNaturalClass = null)
    {
        var rule = cache.ServiceLocator.GetInstance<IPhRegularRuleFactory>().Create();
        cache.LangProject.PhonologicalDataOA.PhonRulesOS.Add(rule);
        rule.Name.set_String(cache.DefaultAnalWs, name);
        rule.Direction = RightToLeftIterative;
        rule.Disabled = false;
        var inputContext = cache.ServiceLocator.GetInstance<IPhSimpleContextSegFactory>().Create();
        rule.StrucDescOS.Add(inputContext);
        inputContext.FeatureStructureRA = input;
        var rhs = Assert.Single(rule.RightHandSidesOS);
        if (output is not null)
        {
            var outputContext = cache.ServiceLocator.GetInstance<IPhSimpleContextSegFactory>().Create();
            rhs.StrucChangeOS.Add(outputContext);
            outputContext.FeatureStructureRA = output;
        }
        if (leftPhoneme is not null)
        {
            var leftContext = cache.ServiceLocator.GetInstance<IPhSimpleContextSegFactory>().Create();
            rhs.LeftContextOA = leftContext;
            leftContext.FeatureStructureRA = leftPhoneme;
        }
        if (rightNaturalClass is null)
        {
            if (rightPhoneme is not null)
            {
                var rightContext = cache.ServiceLocator.GetInstance<IPhSimpleContextSegFactory>().Create();
                rhs.RightContextOA = rightContext;
                rightContext.FeatureStructureRA = rightPhoneme;
            }
        }
        else
        {
            var context = cache.ServiceLocator.GetInstance<IPhSimpleContextNCFactory>().Create();
            rhs.RightContextOA = context;
            context.FeatureStructureRA = rightNaturalClass;
        }
    }

    private static void AssertApplied(LcmCache cache, NumeralFixture fixture, CanonicalId concordClass,
        CanonicalId invarClass, Morph zero)
    {
        var adjective = Get<IPartOfSpeech>(cache, fixture.Adjective);
        var numeral = Get<IPartOfSpeech>(cache, fixture.Numeral);
        var invariable = Get<IPartOfSpeech>(cache, fixture.Invariable);
        Assert.Equal(concordClass.ToGuid(), adjective.DefaultInflectionClassRA?.Guid);
        Assert.Equal(invarClass.ToGuid(), invariable.DefaultInflectionClassRA?.Guid);
        Assert.Equal([numeral.Guid], adjective.SubPossibilitiesOS.Select(child => child.Guid));
        Assert.Equal([invariable.Guid], numeral.SubPossibilitiesOS.Select(child => child.Guid));
        var template = Get<IMoInflAffixTemplate>(cache, fixture.Template);
        Assert.True(template.Final);
        Assert.Equal([fixture.Slot.ToGuid()], template.PrefixSlotsRS.Select(item => item.Guid));
        foreach (var stem in new[] { fixture.Sita, fixture.Wili, fixture.Ishirini, fixture.Arobaini, fixture.Zuri })
            Assert.Null(Get<IMoStemMsa>(cache, stem.Msa).InflectionClassRA);
        var zeroMsa = Get<IMoInflAffMsa>(cache, zero.Msa);
        Assert.Equal(invariable.Guid, zeroMsa.PartOfSpeechRA?.Guid);
        Assert.Equal([fixture.Slot.ToGuid()], zeroMsa.SlotsRC.Select(item => item.Guid));
        Assert.Equal([invarClass.ToGuid()], Get<IMoAffixAllomorph>(cache, zero.Form)
            .InflectionClassesRC.Select(item => item.Guid));
        Assert.Equal(concordClass.ToGuid(), Assert.Single(Get<IMoAffixAllomorph>(cache, fixture.NPrefix.Form)
            .InflectionClassesRC).Guid);
        Assert.Equal(concordClass.ToGuid(), Assert.Single(Get<IMoAffixAllomorph>(cache, fixture.WaPrefix.Form)
            .InflectionClassesRC).Guid);
    }

    private static T Get<T>(LcmCache cache, CanonicalId id) where T : class, ICmObject =>
        (T)cache.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(id.ToGuid());

    private static PanGlossTraceAnalysis AssertReading(PanGlossTraceOutcome.Completed trace, Morph prefix,
        Morph stem, CanonicalId? prefixCategory = null, CanonicalId? stemCategory = null)
    {
        var analysis = Assert.Single(trace.Document!.Analyses);
        Assert.Equal([prefix.Form.ToGuid(), stem.Form.ToGuid()],
            analysis.Morphs.Select(morph => ParserId(morph.FormId)));
        Assert.Equal([prefix.Msa.ToGuid(), stem.Msa.ToGuid()],
            analysis.Morphs.Select(morph => ParserId(morph.MsaId)));
        if (prefixCategory is not null)
            Assert.Equal(prefixCategory.Value.ToGuid(), ParserId(analysis.Morphs[0].CategoryId));
        if (stemCategory is not null)
            Assert.Equal(stemCategory.Value.ToGuid(), ParserId(analysis.Morphs[1].CategoryId));
        return analysis;
    }

    private static Guid ParserId(string? value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Guid.TryParse(value, out var parsed) ? parsed : CanonicalId.Parse(value).ToGuid();
    }

    private static void WriteWitness(ITestOutputHelper output, string word, PanGlossTraceAnalysis analysis)
    {
        var morphs = analysis.Morphs.Select(morph =>
            $"{CanonicalId.FromGuid(ParserId(morph.FormId)).Value}/{CanonicalId.FromGuid(ParserId(morph.MsaId)).Value}" +
            $"/class={FormatParserId(morph.InflectionClassId)}/pos={FormatParserId(morph.CategoryId)}");
        output.WriteLine($"{word}: completed uncapped search; form/MSA/class/POS IDs {string.Join(" + ", morphs)}");
    }

    private static string FormatParserId(string? value) => value is null
        ? "-"
        : CanonicalId.FromGuid(ParserId(value)).Value;

    private static void AssertNoAnalyses(PanGlossTraceOutcome.Completed trace)
    {
        Assert.Equal("-", trace.Summary.Signature);
        Assert.Empty(trace.Document!.Analyses);
    }

    private static async Task<PanGlossTraceOutcome.Completed> CompletedTrace(PanGlossTracer tracer,
        string projectPath, string word)
    {
        var outcome = await tracer.TraceAsync(projectPath, word, CancellationToken.None);
        var completed = Assert.IsType<PanGlossTraceOutcome.Completed>(outcome);
        Assert.True(completed.Summary.Completed);
        Assert.NotNull(completed.Details);
        Assert.True(completed.Details!.SearchCompleted, $"{word}: {completed.Details}");
        Assert.False(completed.Details.Capped, word);
        Assert.False(completed.Details.TimedOut, word);
        Assert.False(completed.Details.InvalidShape, word);
        Assert.NotNull(completed.Document);
        return completed;
    }

    private static SIL.Motif.Model.DryRun.DryRun DryRun(LcmCache live, Proposal proposal)
    {
        var projectPath = live.ProjectId.Path;
        new FwDataProjectLoader().Save(live);
        var scratchRoot = Path.Combine(Path.GetTempPath(), "motif-swahili-numeral-proof-" + Guid.NewGuid().ToString("N"));
        using var scratch = DryRunScratch.Adopt(new ScratchCacheFactory().CreateFromFileCopy(projectPath, scratchRoot),
            "copy of the seeded Swahili numeral project", () =>
            {
                try { Directory.Delete(scratchRoot, recursive: true); }
                catch { }
            });
        return ProposalDryRunner.Run(scratch, proposal);
    }

    private sealed record Morph(CanonicalId Entry, CanonicalId Form, CanonicalId Msa, CanonicalId Category);

    private static void AssertBaselineGraph(LcmCache cache, NumeralFixture fixture)
    {
        var adjective = Get<IPartOfSpeech>(cache, fixture.Adjective);
        var numeral = Get<IPartOfSpeech>(cache, fixture.Numeral);
        var invariable = Get<IPartOfSpeech>(cache, fixture.Invariable);
        Assert.Null(adjective.DefaultInflectionClassRA);
        Assert.Null(invariable.DefaultInflectionClassRA);
        Assert.Equal([numeral.Guid], adjective.SubPossibilitiesOS.Select(child => child.Guid));
        Assert.Equal([invariable.Guid], numeral.SubPossibilitiesOS.Select(child => child.Guid));
        var template = Get<IMoInflAffixTemplate>(cache, fixture.Template);
        Assert.True(template.Final);
        Assert.Equal([fixture.Slot.ToGuid()], template.PrefixSlotsRS.Select(item => item.Guid));
    }

    private sealed record NumeralFixture(CanonicalId Adjective, CanonicalId Numeral, CanonicalId Invariable,
        CanonicalId Slot, CanonicalId Template, Morph NPrefix, Morph WaPrefix, Morph Sita, Morph Wili,
        Morph Ishirini, Morph Arobaini, Morph Zuri);
}
