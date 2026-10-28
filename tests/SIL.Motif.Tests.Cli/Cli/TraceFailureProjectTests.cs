using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parser;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Cli;

[Collection(LcmCacheParallelCollections.Group1)]
[Trait("MotifTestLevel", "System")]
public sealed class TraceFailureProjectTests(PristineProjectFixture pristine, ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-trace-failures-" + Guid.NewGuid().ToString("N"));

    [RealParserFact]
    public async Task EmptyLexiconNamesTheRequestedShapeAndFrozenEntryCount()
    {
        string project;
        using (var cache = pristine.NewScratch())
        {
            RealParserProject.PrepareForParsing(cache, "m", "t", "u");
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                foreach (var entry in cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances().ToArray())
                    entry.Delete();
            });
            new FwDataProjectLoader().Save(cache);
            project = cache.ProjectId.Path;
        }
        var captured = BaselineCaptureCommand.Capture(new(project), _root);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var parser = PanGlossExecutable.TryLocate()!;
        var result = await CliInProcess.RunAsync(_root, parser, false, "trace", "--project", project, "--word", "mtu", "--json");
        Assert.True(result.ExitCode == 0, result.Error);
        var response = ProjectionJson.Deserialize<WordTraceResponse>(result.Output)!;
        Assert.True(response.Complete);
        Assert.False(response.InvalidShape);
        Assert.False(response.Parsed);
        Assert.Equal(0, response.HostCapture!.LexiconEntryCount);
        const string reason = "No lexical root matched 'mtu'; the traced grammar's lexicon has 0 entries.";
        Assert.Equal(reason, Assert.Single(response.Reading.NoParseReasons));
        var saved = Path.Combine(_root, "empty-mtu.trace.json");
        File.WriteAllText(saved, response.DiagnosticJson);
        var text = await CliInProcess.RunAsync(_root, null, false, "trace", "--load", saved);
        Assert.True(text.ExitCode == 0, text.Error);
        Assert.Contains(reason, text.Output);
        output.WriteLine(text.Output);
    }

    [RealParserFact]
    public async Task SeededStemPrefixAndRewriteGiveDistinctRecordedFailureReasons()
    {
        string project;
        using (var cache = pristine.NewScratch())
        {
            Seed(cache);
            new FwDataProjectLoader().Save(cache);
            project = cache.ProjectId.Path;
        }
        var captured = BaselineCaptureCommand.Capture(new(project), _root);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var parser = PanGlossExecutable.TryLocate()!;
        var reasons = new List<string>();
        foreach (var (word, expected) in new[]
                 {
                     ("mtu", "No lexical root matched 'mtu'"),
                     ("baya", "did not complete the required rules and templates"),
                     ("nbaya", "builds a form that differs from the word being parsed"),
                 })
        {
            var text = await CliInProcess.RunAsync(_root, parser, false, "trace", "--project", project, "--word", word);
            output.WriteLine(word + ":\n" + text.Output + text.Error);
            Assert.True(text.ExitCode == 0, text.Error);
            Assert.Contains(expected, text.Output);
            reasons.Add(expected);
            var json = await CliInProcess.RunAsync(_root, parser, false, "trace", "--project", project, "--word", word, "--json");
            Assert.True(json.ExitCode == 0, json.Error);
            var response = ProjectionJson.Deserialize<WordTraceResponse>(json.Output)!;
            Assert.True(response.Complete);
            Assert.False(response.Parsed);
            File.WriteAllText(Path.Combine(_root, word + ".trace.json"), response.DiagnosticJson);
            if (word == "baya")
                Assert.Contains(Walk(response.Reading.Root), step => step.EventEvidence?.PartialParseCause == "ApplicableTemplatesNotApplied");
            if (word == "nbaya")
            {
                var failed = Assert.Single(response.Reading.Attempts, attempt => attempt.FailureReason == "SurfaceFormMismatch");
                Assert.Contains(failed.RichMorphs, morph => morph.Form == "baya");
                Assert.Contains(failed.RichMorphs, morph => morph.Form == "n");
                Assert.Equal("nbaya", failed.FailureRequired);
                Assert.Contains("m", failed.FailureActual);
            }
        }
        Assert.Equal(3, reasons.Distinct().Count());
        var control = await CliInProcess.RunAsync(_root, parser, false, "trace", "--project", project, "--word", "mbaya", "--json");
        Assert.True(control.ExitCode == 0, control.Error);
        Assert.True(ProjectionJson.Deserialize<WordTraceResponse>(control.Output)!.Parsed);
        output.WriteLine("Recorded diagnostics: " + _root);
    }

    private void Seed(LcmCache cache)
    {
        RealParserProject.PrepareForParsing(cache);
        var phonemes = new Dictionary<string, CanonicalId>();
        IFsClosedFeature feature = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            feature = cache.ServiceLocator.GetInstance<IFsClosedFeatureFactory>().Create();
            cache.LangProject.PhFeatureSystemOA.FeaturesOC.Add(feature);
            feature.Name.set_String(cache.DefaultAnalWs, "Segment identity");
            feature.Abbreviation.set_String(cache.DefaultAnalWs, "SEG");
        });
        foreach (var letter in new[] { "a", "b", "m", "n", "t", "u", "w", "y" })
        {
            IFsSymFeatVal value = null!;
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                value = cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create();
                feature.ValuesOC.Add(value);
                value.Name.set_String(cache.DefaultAnalWs, letter);
                value.Abbreviation.set_String(cache.DefaultAnalWs, letter);
            });
            var operations = AuthorPhonemeComposer.Build(cache, new(letter, [letter],
                [new(CanonicalId.FromGuid(feature.Guid), CanonicalId.FromGuid(value.Guid))]));
            Execute(cache, operations);
            phonemes.Add(letter, operations.Single(operation =>
                operation.Kind == PhPhonemeSetPhonemesOperationKinds.Create).EntityId!.Value);
        }
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var services = cache.ServiceLocator;
            var stem = services.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.FirstEntryId);
            stem.LexemeFormOA!.Form.set_String(cache.DefaultVernWs, "baya");
            services.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.SecondEntryId).Delete();
            var pos = services.GetInstance<IPartOfSpeechRepository>().GetObject(pristine.Seed.PartOfSpeechId);
            var slot = services.GetInstance<IMoInflAffixSlotFactory>().Create();
            pos.AffixSlotsOC.Add(slot);
            slot.Name.set_String(cache.DefaultAnalWs, "Class prefix");
            slot.Optional = false;
            var template = services.GetInstance<IMoInflAffixTemplateFactory>().Create();
            pos.AffixTemplatesOS.Add(template);
            template.Name.set_String(cache.DefaultAnalWs, "Required class prefix");
            template.Final = true;
            template.PrefixSlotsRS.Add(slot);
            var prefix = services.GetInstance<ILexEntryFactory>().Create(
                services.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphPrefix),
                TsStringUtils.MakeString("n", cache.DefaultVernWs), "class prefix",
                SandboxGenericMSA.Create(MsaType.kInfl, pos));
            ((IMoInflAffMsa)prefix.MorphoSyntaxAnalysesOC.Single()).SlotsRC.Add(slot);
        });
        Execute(cache, AuthorPhonologicalRuleComposer.Build(cache, new("n becomes m before b",
            PhonologicalRuleDirection.Simultaneous, [new(Phoneme: phonemes["n"])],
            [new(Phoneme: phonemes["m"])], [new(Boundary: "word")], [new(Phoneme: phonemes["b"])])));
    }

    private static void Execute(LcmCache cache, IReadOnlyList<OperationEnvelope> operations)
    {
        var proposal = new Proposal(new Dictionary<string, string> { ["grammar"] = "1.0" }, CanonicalId.Mint(), null, operations);
        var dryRun = ScratchDryRun.Of(cache, proposal);
        ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "trace-failure-fixture");
    }

    private static IEnumerable<TraceStep> Walk(TraceStep step) => new[] { step }.Concat(step.Children.SelectMany(Walk));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
