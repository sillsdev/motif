using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Pins the names a trace reading shows as refs: each carries its kind, the identity PanGloss recorded and how far
/// that identity can be trusted, a FieldWorks destination only when the trace's Baseline is this project's, and the
/// key PanGloss's statistics time it under.
/// </summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group0)]
public sealed class TraceRefsTests : IDisposable
{
    private readonly PristineProjectFixture _pristine;
    private readonly string _managedRoot =
        Path.Combine(Path.GetTempPath(), "SIL.Motif.TraceRefsTests", Guid.NewGuid().ToString("N"));

    public TraceRefsTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        Directory.CreateDirectory(_managedRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_managedRoot, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void CapturedFieldWorksLabelsRoundTripWithoutChangingProducerNames()
    {
        var (fwDataPath, phonRule, _, affixMsa) = ProjectWithRules();
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), _managedRoot).Succeeded);
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(SagdTrace(identity: (phonRule, affixMsa))
                .Replace("Vowel harmony", "Producer name", StringComparison.Ordinal), string.Empty, TimeSpan.FromMilliseconds(4)),
        };
        var response = WordTraceQuery.Query(new WordTraceRequest(fwDataPath, "sagd"), new PanGlossTracer(invoker),
            CancellationToken.None).Value!;
        var live = Assert.Single(response.Reading!.Refs, reference => reference.Kind == "phonologicalRule");
        Assert.Equal("Producer name", live.Label);
        Assert.Equal("Vowel harmony", new TraceDisplayLabels(response.Reading.Refs).Resolve(live.Id, live.Label));
        using var serialized = System.Text.Json.JsonDocument.Parse(ProjectionJson.Serialize(live));
        Assert.Equal("Vowel harmony", serialized.RootElement.GetProperty("capturedFieldWorksLabel").GetString());
        var reopened = WordTraceQuery.LoadDiagnostic(response.DiagnosticJson).Value!;
        var saved = Assert.Single(reopened.Reading!.Refs, reference => reference.Id == live.Id);
        using var savedJson = System.Text.Json.JsonDocument.Parse(ProjectionJson.Serialize(saved));
        Assert.Equal("Vowel harmony", savedJson.RootElement.GetProperty("capturedFieldWorksLabel").GetString());
        Assert.Equal(live.Label, saved.Label);
        Assert.Equal("Vowel harmony", new TraceDisplayLabels(reopened.Reading.Refs).Resolve(saved.Id, saved.Label));
        Assert.Null(saved.FieldWorks);
    }

    [Fact]
    public void AReplacementProjectAtTheSamePathCannotReceiveRecordedTraceLinks()
    {
        var (fwDataPath, phonRule, _, affixMsa) = ProjectWithRules();
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), _managedRoot).Succeeded);
        var source = XDocument.Load(fwDataPath);
        source.Descendants("rt").Single(element => (string?)element.Attribute("class") == "LangProject")
            .SetAttributeValue("guid", Guid.NewGuid().ToString("D"));
        source.Save(fwDataPath);

        var response = QueryCaptured(fwDataPath, phonRule, affixMsa);

        Assert.False(response.Provenance!.CanNavigate);
        Assert.Equal("mismatch", response.Provenance.ProjectIdentityStatus);
        Assert.All(response.Reading!.Refs, reference => Assert.Null(reference.FieldWorks));
        Assert.NotEmpty(response.Reading.Refs);
        Assert.All(response.Reading.Attempts.SelectMany(candidate => candidate.RichMorphs), morph => Assert.Null(morph.FieldWorksLink));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("malformed")]
    [InlineData("no-identity")]
    [InlineData("truncated-after-identity")]
    public void AnUnverifiableSourceCannotReceiveRecordedTraceLinks(string condition)
    {
        var (fwDataPath, phonRule, _, affixMsa) = ProjectWithRules();
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), _managedRoot);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var evidence = captured.Value!;
        var baseline = new BaselineRecord("trace", evidence.Token, Path.GetDirectoryName(evidence.FwDataPath)!,
            evidence.FwDataPath, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var original = File.ReadAllText(fwDataPath);
        if (condition == "missing") File.Delete(fwDataPath);
        else if (condition == "truncated-after-identity")
            File.WriteAllText(fwDataPath, original[..original.LastIndexOf("</languageproject>", StringComparison.Ordinal)]);
        else File.WriteAllText(fwDataPath, condition == "malformed" ? "<languageproject>" : "<languageproject />");

        var traced = WordTraceQuery.LoadDiagnostic(SagdTrace(identity: (phonRule, affixMsa))).Value!;
        var response = TraceDiagnosticCapture.Attach(traced, baseline, new ProjectLocator(fwDataPath, "trace"));

        Assert.False(response.Provenance!.CanNavigate);
        Assert.Equal("unknown", response.Provenance.ProjectIdentityStatus);
        Assert.All(response.Reading!.Refs, reference => Assert.Null(reference.FieldWorks));
        Assert.NotEmpty(response.Reading.Refs);
    }

    [Fact]
    public void SourceIdentityVerificationRequiresOnlyReadAccess()
    {
        var (fwDataPath, phonRule, _, affixMsa) = ProjectWithRules();
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), _managedRoot).Succeeded);
        using var source = new FileStream(fwDataPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);

        var response = QueryCaptured(fwDataPath, phonRule, affixMsa);

        Assert.True(response.Provenance!.CanNavigate);
        Assert.NotNull(Assert.Single(response.Reading!.Refs, reference => reference.Kind == "phonologicalRule").FieldWorks);
        Assert.False(File.Exists(fwDataPath + ".lock"));
    }

    private WordTraceResponse QueryCaptured(string path, Guid phonRule, Guid affixMsa)
    {
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(
                SagdTrace(identity: (phonRule, affixMsa)), string.Empty, TimeSpan.FromMilliseconds(4)),
        };
        var outcome = WordTraceQuery.Query(new WordTraceRequest(path, "sagd"), new PanGlossTracer(invoker),
            CancellationToken.None);
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        return outcome.Value!;
    }

    [Fact]
    public void KumataNamesEveryStepAndMorphByTheIdentityPanGlossRecorded()
    {
        var reading = Load("trace-details-v2-kumata.json").Reading!;
        var refs = reading.Refs.ToDictionary(reference => reference.Id);

        Assert.Equal(reading.Refs.Count, refs.Count);
        Assert.All(reading.Attempts.SelectMany(attempt => attempt.Steps).Where(step => step.Source is not null),
            step => Assert.Contains(step.RefId!, refs.Keys));
        var affix = refs["morphRule:00000000-0000-0000-0000-000000000109"];
        Assert.Equal(("morphologicalRule", "-ta", "authored"), (affix.Kind, affix.Label, affix.IdentityQuality));
        Assert.Equal(new TraceTimingKey("morph_rule", "00000000-0000-0000-0000-000000000109"), affix.TimingKey);
        var template = refs["template:0"];
        Assert.Equal(("template", "NounTemplate", "grammar-local"), (template.Kind, template.Label, template.IdentityQuality));
        Assert.Null(template.TimingKey);
        Assert.Equal("stratum", refs["stratum:0"].Kind);

        var morphs = Assert.Single(reading.Analyses).Morphs;
        var stem = refs[morphs[0].RefId!];
        Assert.Equal(("morph", "kuma", "dog"), (stem.Kind, stem.Label, stem.Gloss));
        Assert.Equal("00000000-0000-0000-0000-000000000104", stem.Identity);
        Assert.Equal(new TraceTimingKey("lex_entry", "00000000-0000-0000-0000-000000000104"), stem.TimingKey);
        // An affix is timed as the rule that adds it, so its morph and its rule share one statistics key.
        Assert.Equal(affix.TimingKey, refs[morphs[1].RefId!].TimingKey);
        // A loaded file belongs to no open project, so nothing in it opens FieldWorks.
        Assert.All(reading.Refs, reference => Assert.Null(reference.FieldWorks));
    }

    [Fact]
    public void ATraceWithNoRecordedIdentityNamesRuleOccurrencesAndClaimsNoIdentity()
    {
        var reading = WordTraceQuery.LoadDiagnostic(SagdTrace(identity: null)).Value!.Reading!;

        var rules = reading.Refs.Where(reference => reference.Kind == "morphologicalRule").ToArray();
        Assert.Equal(3, rules.Length);
        Assert.Equal(3, rules.Select(rule => rule.Id).Distinct().Count());
        Assert.All(rules, rule =>
        {
            Assert.StartsWith("morphRule:step:", rule.Id);
            Assert.Equal("ed_suffix", rule.Label);
            Assert.Null(rule.Identity);
            Assert.Equal(TraceRefIds.UnknownQuality, rule.IdentityQuality);
            Assert.Null(rule.TimingKey);
        });
        var stop = Assert.Single(reading.StopGroups);
        Assert.Null(stop.RuleRefId);
        Assert.Equal(stop.RuleRefId, Assert.Single(stop.Attempts).StoppedByRefId);
    }

    [Fact]
    public void AnAuthoredRulesRefCarriesItsIdentityAndALinkToItsFieldWorksTool()
    {
        var (fwDataPath, phonRule, affixEntry, affixMsa) = ProjectWithRules();
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), _managedRoot).Succeeded);
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(
                SagdTrace(identity: (phonRule, affixMsa)), string.Empty, TimeSpan.FromMilliseconds(4)),
        };

        var outcome = WordTraceQuery.Query(new WordTraceRequest(fwDataPath, "sagd"), new PanGlossTracer(invoker),
            CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var refs = outcome.Value!.Reading!.Refs.ToDictionary(reference => reference.Id);
        var phonological = refs["phonRule:" + phonRule.ToString("D")];
        Assert.Equal(("phonologicalRule", "Vowel harmony", "authored"),
            (phonological.Kind, phonological.Label, phonological.IdentityQuality));
        Assert.Equal(phonRule.ToString("D"), phonological.Identity);
        Assert.Equal(new TraceTimingKey("phon_rule", phonRule.ToString("D")), phonological.TimingKey);
        Assert.NotNull(phonological.FieldWorks);
        Assert.Equal(("PhonologicalRuleEdit", "Phonological Rules", phonRule.ToString("D")),
            (phonological.FieldWorks.Tool, phonological.FieldWorks.ToolName, phonological.FieldWorks.ObjectId));
        Assert.Equal("PhonologicalRuleEdit", FieldWorksLinks.ToolOf(phonological.FieldWorks.Link));

        // An affix rule is known by its grammatical info, and opens on the entry that owns it.
        var affix = refs["morphRule:" + affixMsa.ToString("D")];
        Assert.Equal("morphologicalRule", affix.Kind);
        Assert.Equal(("lexiconEdit", "Lexicon Edit", affixEntry.ToString("D")),
            (affix.FieldWorks!.Tool, affix.FieldWorks.ToolName, affix.FieldWorks.ObjectId));
    }

    [Fact]
    public void ATraceFromAnotherProjectsBaselineKeepsItsIdentitiesAndCarriesNoLink()
    {
        var (fwDataPath, phonRule, _, affixMsa) = ProjectWithRules();
        var traced = WordTraceQuery.LoadDiagnostic(SagdTrace(identity: (phonRule, affixMsa))).Value!;
        var otherProject = Guid.NewGuid().ToString("D");
        var baseline = new BaselineRecord("other", new BaselineToken(otherProject, "sha256:" + new string('a', 64),
                "1", "2026-10-01T00:00:00Z", "sha256:" + new string('b', 64)),
            Path.GetDirectoryName(fwDataPath)!, fwDataPath, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var attached = TraceDiagnosticCapture.Attach(traced, baseline, new ProjectLocator(fwDataPath, otherProject));

        Assert.False(attached.Provenance!.CanNavigate);
        var rule = Assert.Single(attached.Reading!.Refs, reference => reference.Kind == "phonologicalRule");
        Assert.Equal(phonRule.ToString("D"), rule.Identity);
        Assert.All(attached.Reading.Refs, reference => Assert.Null(reference.FieldWorks));
    }

    private static WordTraceResponse Load(string fixture) => WordTraceQuery.LoadDiagnostic(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "TestFixtures", fixture))).Value!;

    private (string FwDataPath, Guid PhonRule, Guid AffixEntry, Guid AffixMsa) ProjectWithRules()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var loader = new FwDataProjectLoader();
        using var cache = loader.LoadCache(fwDataPath);
        var services = cache.ServiceLocator;
        IPhRegularRule rule = null!;
        ILexEntry entry = null!;
        IMoInflAffMsa msa = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            rule = services.GetInstance<IPhRegularRuleFactory>().Create();
            cache.LangProject.PhonologicalDataOA.PhonRulesOS.Add(rule);
            rule.Name.set_String(cache.DefaultAnalWs, "Vowel harmony");
            entry = services.GetInstance<ILexEntryFactory>().Create();
            var allomorph = services.GetInstance<IMoAffixAllomorphFactory>().Create();
            entry.LexemeFormOA = allomorph;
            allomorph.MorphTypeRA = services.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphSuffix);
            allomorph.Form.set_String(cache.DefaultVernWs, TsStringUtils.MakeString("d", cache.DefaultVernWs));
            msa = services.GetInstance<IMoInflAffMsaFactory>().Create();
            entry.MorphoSyntaxAnalysesOC.Add(msa);
        });
        loader.Save(cache);
        return (fwDataPath, rule.Guid, entry.Guid, msa.Guid);
    }

    // The captured sagd derivation, with each rule's identity as PanGloss records it when it records one.
    private static string SagdTrace((Guid PhonRule, Guid AffixMsa)? identity)
    {
        string Identity(string kind, Guid? id) => id is null ? "" :
            $",\"sourceIdentity\":{{\"kind\":\"{kind}\",\"id\":\"{id:D}\",\"quality\":\"authored\"}}";
        var affix = Identity("morphRule", identity?.AffixMsa);
        var phon = identity is null ? "" :
            "{\"type\":\"PhonologicalRuleSynthesis\",\"source\":\"Vowel harmony\",\"inputShape\":\"sagd\"," +
            "\"outputShape\":\"sagd\"" + Identity("phonRule", identity.Value.PhonRule) + ",\"children\":[]},";
        return TraceEnvelope.Of("32+PAST|sag+?d",
            "{\"type\":\"WordAnalysis\",\"inputShape\":\"sagd\",\"children\":[" +
            "{\"type\":\"MorphologicalRuleAnalysis\",\"source\":\"ed_suffix\",\"subrule\":0,\"outputShape\":\"sag\"" + affix +
            ",\"children\":[" +
            "{\"type\":\"LexicalLookup\",\"source\":\"S\",\"inputShape\":\"sag\",\"children\":[]}," +
            "{\"type\":\"MorphologicalRuleSynthesis\",\"source\":\"ed_suffix\",\"subrule\":0,\"outputShape\":\"sagd\"" + affix +
            ",\"children\":[" + phon +
            "{\"type\":\"Successful\",\"outputShape\":\"sagd\",\"children\":[]}]}," +
            "{\"type\":\"MorphologicalRuleSynthesis\",\"source\":\"ed_suffix\"" + affix +
            ",\"failureReason\":\"NonPartialRuleProhibitedAfterFinalTemplate\",\"inputShape\":\"sag\",\"children\":[]}," +
            "{\"type\":\"Failed\",\"failureReason\":\"PartialParse\",\"outputShape\":\"sag\",\"children\":[]}]}]}");
    }
}
