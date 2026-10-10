using System.Security.Cryptography;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Commands.Parsimony;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.LiveHost.Baselines;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Projection.Retirement;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Retirement;

[Trait("MotifTestLevel", "System")]
public sealed class AllomorphRetargetSampleTests(ITestOutputHelper output)
{
    private static readonly Guid MbugweRetiredNj = Guid.Parse("30fcb4a3-db6a-4acc-bf4e-347891c01f5a");
    private static readonly Guid MbugweReplacementN = Guid.Parse("0a7d6729-3059-4639-a012-6b6dafda61b8");

    [AllomorphRetirementSampleFact]
    public void MbugweNjBundleRetargetDryRunsOnAnIsolatedProjectCopy()
    {
        var sourceRoot = Path.GetFullPath(Environment.GetEnvironmentVariable("MOTIF_PANGLOSS_SAMPLES")!);
        var originalProject = Path.Combine(sourceRoot, "mbugwe.fwdata");
        var sourceDigest = TreeDigest(sourceRoot);
        var root = Path.Combine(Path.GetTempPath(), "motif-retirement-mbugwe-retarget-" + Guid.NewGuid().ToString("N"));
        var copiedRoot = Path.Combine(root, "source-copy");
        Directory.CreateDirectory(copiedRoot);
        CopyTree(Path.Combine(sourceRoot, "WritingSystemStore"), Path.Combine(copiedRoot, "WritingSystemStore"));
        var copiedProject = Path.Combine(copiedRoot, "mbugwe.fwdata");
        File.Copy(originalProject, copiedProject);

        try
        {
            using var cache = new FwDataProjectLoader().LoadScratchCache(copiedProject);
            new FwDataProjectLoader().Save(cache);

            var candidates = cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances()
                .SelectMany(entry => entry.AlternateFormsOS.OfType<IMoAffixAllomorph>())
                .Where(IsOrdinaryPrefixOrSuffix)
                .Where(form => form.Guid == MbugweRetiredNj)
                .OrderBy(form => form.Owner?.Guid)
                .ThenBy(form => form.Guid)
                .ToArray();
            Assert.Single(candidates);
            var footprint = AllomorphReferenceFootprintReader.Read(cache, candidates.Select(item => item.Guid));
            var njOwned = footprint.OwnedDependents.Where(item => item.TargetForm == MbugweRetiredNj).ToArray();
            Assert.Contains(njOwned, item => item.DeclaringClass == "MoAffixAllomorph" &&
                item.Field == "MsEnvFeatures" && item.DependentClass == "FsFeatStruc");
            Assert.Contains(njOwned, item => item.DeclaringClass == "FsFeatStruc" &&
                item.Field == "FeatureSpecs" && item.DependentClass == "FsClosedValue");
            var selected = candidates;
            var ownedObjects = string.Join(" | ", footprint.OwnedDependents
                .Where(item => item.TargetForm == MbugweRetiredNj)
                .Select(item => $"{item.DependentClass} {item.DependentObject:D} owned through " +
                    $"{item.DeclaringClass}.{item.Field} by {item.ParentObject:D}"));

            var refusals = new List<string>();
            var completed = false;
            foreach (var retired in selected)
            {
                if (retired.Owner is not ILexEntry entry) continue;
                var incoming = footprint.References.Where(item => item.TargetForm == retired.Guid).ToArray();
                var bundleReferences = incoming.Where(item => item.Kind == "bundle-morph").ToArray();
                if (bundleReferences.Any(item => item.Msa is null))
                {
                    refusals.Add($"{retired.Guid:D}: a bundle has no MSA identity");
                    continue;
                }

                foreach (var replacement in SurvivingForms(entry, retired).Where(form =>
                             form.Guid == MbugweReplacementN &&
                             form.MorphTypeRA?.Guid == retired.MorphTypeRA?.Guid && !form.IsAbstract))
                {
                    var role = Position(retired);
                    if (retired.Form.AvailableWritingSystemIds.Any(ws =>
                            !string.IsNullOrEmpty(retired.Form.get_String(ws)?.Text) &&
                            string.IsNullOrEmpty(replacement.Form.get_String(ws)?.Text)))
                        continue;

                    RetireAllomorphIntent intent;
                    IReadOnlyList<OperationEnvelope> operations;
                    Proposal proposal;
                    try
                    {
                        intent = BuildIntent(cache, entry, retired, replacement, incoming);
                        operations = AllomorphRetargetComposer.Build(cache, intent, () => CanonicalId.Mint());
                        proposal = new Proposal(new Dictionary<string, string> { ["analysis"] = "1.0" },
                            CanonicalId.Mint(), null, operations);
                    }
                    catch (InvalidOperationException exception)
                    {
                        refusals.Add($"{FormText(cache, retired)} ({retired.Guid:D}) → " +
                            $"{FormText(cache, replacement)} ({replacement.Guid:D}): {exception.Message}");
                        continue;
                    }

                    var affectedWordforms = bundleReferences.Where(item => item.Wordform is not null)
                        .Select(item => item.Wordform!.Value).Distinct().Order().ToArray();
                    var semanticDigest = BaselineSemanticDigest.Compute(cache);
                    var sourceBaseline = new BaselineToken(cache.LangProject.Guid.ToString("D"), semanticDigest,
                        BaselineSemanticDigest.ProjectionVersion,
                        DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                        "sha256:" + new string('b', 64));
                    var frozen = FrozenExpectationCapture.Capture(cache, sourceBaseline, affectedWordforms,
                        new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));
                    var dryRoot = Path.Combine(root, "dry-run-" + Guid.NewGuid().ToString("N"));
                    var dryCache = new ScratchCacheFactory().CreateFromFileCopy(copiedProject, dryRoot);
                    using var scratch = DryRunScratch.Adopt(dryCache,
                        "isolated copy of the Mbugwe sample project for an R2 Dry Run",
                        onDisposed: () =>
                        {
                            try { Directory.Delete(dryRoot, recursive: true); }
                            catch (IOException) { }
                            catch (UnauthorizedAccessException) { }
                        });
                    var before = CaptureAffectedReadings(cache, incoming);
                    try
                    {
                        var dryRun = ProposalDryRunner.Run(scratch, proposal);
                        var afterCache = scratch.PeekCache();
                        AssertPreservedReadings(afterCache, before);
                        var translated = RetirementExpectationTranslator.Translate(frozen, sourceBaseline, intent,
                            proposal, dryRun);
                        var translatedCaseById = translated.Translated.Cases.ToDictionary(item => item.CaseId,
                            StringComparer.Ordinal);
                        var translatedRows = new List<(FrozenExpectationCase originalCase,
                            FrozenExpectedReading originalReading, FrozenExpectationMorph originalMorph,
                            FrozenExpectationMorph translatedMorph)>();
                        foreach (var originalCase in translated.Original.Cases)
                        foreach (var originalReading in originalCase.Readings)
                        {
                            var translatedReading = translatedCaseById[originalCase.CaseId].Readings
                                .Single(item => item.ReadingId == originalReading.ReadingId);
                            for (var index = 0; index < originalReading.Morphs.Count; index++)
                            {
                                var originalMorph = originalReading.Morphs[index];
                                var translatedMorph = translatedReading.Morphs[index];
                                if (originalMorph.Form != translatedMorph.Form)
                                    translatedRows.Add((originalCase, originalReading, originalMorph, translatedMorph));
                            }
                        }
                        var approvedReadingCount = translated.Original.Cases.SelectMany(item => item.Readings)
                            .Count(item => item.Opinion == "approved");
                        var outsideSelectionApprovedReadingCount = translated.Original.Cases
                            .Where(item => !item.InSelection).SelectMany(item => item.Readings)
                            .Count(item => item.Opinion == "approved");
                        var disapprovedReadingCount = translated.Original.Cases.SelectMany(item => item.Readings)
                            .Count(item => item.Opinion == "disapproved");
                        var collisionRefusalCount = refusals.Count(item =>
                            item.Contains("collision", StringComparison.OrdinalIgnoreCase));
                        var formEffects = dryRun.ExpectedEffects.Where(item =>
                            item.Field == SnapshotFields.WfiMorphBundleForm && item.Before != item.After).ToArray();
                        var morphEffects = dryRun.ExpectedEffects.Where(item =>
                            item.Field == SnapshotFields.WfiMorphBundleMorph).ToArray();
                        Assert.Equal(bundleReferences.Length, morphEffects.Length);
                        Assert.Equal(bundleReferences.Length, formEffects.Length);
                        Assert.All(formEffects, effect => Assert.NotEqual(effect.Before, effect.After));
                        Assert.All(bundleReferences, reference => Assert.Equal(retired.Guid,
                            cache.ServiceLocator.GetInstance<IWfiMorphBundleRepository>()
                                .GetObject(reference.Bundle!.Value).MorphRA!.Guid));
                        Assert.Contains(retired, entry.AlternateFormsOS);
                        Assert.Equal(sourceDigest, TreeDigest(sourceRoot));
                        output.WriteLine("Grammar=Mbugwe");
                        output.WriteLine($"Retired={FormText(cache, retired)} id={retired.Guid:D}");
                        output.WriteLine($"Replacement={FormText(cache, replacement)} id={replacement.Guid:D}");
                        output.WriteLine($"BundleMorphEffects={morphEffects.Length}");
                        output.WriteLine($"BundleFormTextEffects={formEffects.Length}");
                        foreach (var effect in formEffects)
                        foreach (var writingSystem in effect.Before.Keys.Union(effect.After.Keys).Order(StringComparer.Ordinal))
                        {
                            var beforeText = effect.Before.GetValueOrDefault(writingSystem, "");
                            var afterText = effect.After.GetValueOrDefault(writingSystem, "");
                            if (beforeText != afterText)
                                output.WriteLine($"BundleText={effect.CanonicalId.Value} {writingSystem}: '{beforeText}' -> '{afterText}'");
                        }
                        output.WriteLine($"PreservedAnalyses={before.Count}");
                        output.WriteLine($"PreservedEvaluations={before.Values.Sum(item => item.Evaluations.Length)}");
                        output.WriteLine($"PreservedUserOpinions={before.Values.Count(item => item.UserOpinion == Opinions.approves)} Approved");
                        output.WriteLine($"PreservedParserOpinions={before.Values.Count(item => item.ParserOpinion == Opinions.approves)} Approved");
                        output.WriteLine("OpinionsAndEvaluationMembership=preserved");
                        output.WriteLine($"FrozenSourceBaseline={sourceBaseline.SemanticSnapshotDigest}");
                        output.WriteLine($"FrozenCases={translated.Original.Cases.Count}");
                        output.WriteLine($"AffectedWordforms={affectedWordforms.Length}");
                        output.WriteLine($"FrozenApprovedReadings={approvedReadingCount}");
                        output.WriteLine($"ApprovedReadingsOutsideSelection={outsideSelectionApprovedReadingCount}");
                        output.WriteLine($"FrozenDisapprovedReadings={disapprovedReadingCount}");
                        output.WriteLine($"FrozenUnavailableReasons={translated.Original.Unavailable.Count}");
                        output.WriteLine($"TranslatedFormRoles={translatedRows.Count}");
                        output.WriteLine($"RoleMappingDigest={translated.MappingDigest}");
                        output.WriteLine($"DryRunFootprintDigest={translated.DryRunFootprintDigest}");
                        output.WriteLine($"DryRunEffectDigest={translated.DryRunEffectDigest}");
                        output.WriteLine($"CollisionRefusals={collisionRefusalCount}");
                        output.WriteLine($"Refusals={refusals.Count}");
                        foreach (var row in translatedRows)
                            output.WriteLine($"ExpectationPair={row.originalCase.CaseId} {row.originalReading.ReadingId}: " +
                                $"{row.originalMorph.Form} -> {row.translatedMorph.Form}");
                        foreach (var reason in translated.Original.Unavailable)
                            output.WriteLine($"FrozenUnavailable={reason}");
                        output.WriteLine("Apply=not-run");
                        completed = true;
                        break;
                    }
                    catch (InvalidOperationException exception)
                    {
                        refusals.Add($"{FormText(cache, retired)} ({retired.Guid:D}) → " +
                            $"{FormText(cache, replacement)} ({replacement.Guid:D}): {exception.Message}");
                    }
                }

                if (completed) break;
            }

            Assert.True(completed, "The named Mbugwe nj → n pair did not produce a safe R3 expectation translation. " +
                "Attempts: " + string.Join(" | ", refusals) + ". Owned objects: " + ownedObjects);
            Assert.Equal(sourceDigest, TreeDigest(sourceRoot));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static RetireAllomorphIntent BuildIntent(LcmCache cache, ILexEntry entry,
        IMoAffixAllomorph retired, IMoAffixAllomorph replacement, IReadOnlyList<AllomorphReference> incoming)
    {
        var sourceId = CanonicalId.FromGuid(retired.Guid);
        var entryId = CanonicalId.FromGuid(entry.Guid);
        var destination = Identity(cache, replacement, entryId,
            entry.LexemeFormOA?.Guid == replacement.Guid ? "lexeme" : "alternate");
        var bundleReferences = incoming.Where(item => item.Kind == "bundle-morph").ToArray();
        var roles = bundleReferences.Select(item => (Msa: item.Msa!.Value, item.InflType)).Distinct()
            .Select(item => new AllomorphRoleReplacement(sourceId.Value,
                CanonicalId.FromGuid(item.Msa).Value,
                item.InflType is { } infl ? CanonicalId.FromGuid(infl).Value : null,
                "whole", destination)).ToArray();
        var bundles = bundleReferences.Select(item => new RetirementBundle(
            CanonicalId.FromGuid(item.Bundle!.Value).Value,
            CanonicalId.FromGuid(item.Analysis!.Value).Value,
            CanonicalId.FromGuid(item.Wordform!.Value).Value,
            sourceId.Value,
            CanonicalId.FromGuid(item.Msa!.Value).Value,
            item.InflType is { } infl ? CanonicalId.FromGuid(infl).Value : null,
            "whole")).ToArray();
        var adHoc = incoming.Where(item => item.Kind.StartsWith("adhoc-", StringComparison.Ordinal))
            .Select(item => new AdhocOccurrenceReplacement(
                CanonicalId.FromGuid(item.Rule!.Value).Value, item.Field, item.Ordinal,
                sourceId.Value, destination)).ToArray();
        return new RetireAllomorphIntent(entryId.Value, AllomorphRetirementScope.Affix,
            [Identity(cache, retired, entryId, "alternate")], roles, bundles, adHoc);
    }

    private static AllomorphIdentity Identity(LcmCache cache, IMoAffixAllomorph form,
        CanonicalId entry, string location)
    {
        var alternatives = string.Join('|', form.Form.AvailableWritingSystemIds.Order()
            .Select(ws => cache.WritingSystemFactory.GetStrFromWs(ws) + '=' + form.Form.get_String(ws)?.Text));
        var content = $"{form.Guid:D}|{form.MorphTypeRA?.Guid:D}|{alternatives}";
        var digest = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
        return new AllomorphIdentity(CanonicalId.FromGuid(form.Guid).Value, entry.Value, form.ClassName,
            location, Position(form), "sha256:" + digest, null);
    }

    private static IReadOnlyList<IMoAffixAllomorph> SurvivingForms(ILexEntry entry, IMoAffixAllomorph retired) =>
        entry.AlternateFormsOS.OfType<IMoAffixAllomorph>().Where(item => item.Guid != retired.Guid)
            .Append(entry.LexemeFormOA as IMoAffixAllomorph)
            .Where(item => item is not null)
            .Cast<IMoAffixAllomorph>()
            .OrderBy(item => item.Guid)
            .ToArray();

    private static Dictionary<Guid, ReadingState> CaptureAffectedReadings(
        LcmCache cache, IEnumerable<AllomorphReference> references)
    {
        var wordforms = references.Where(item => item.Wordform is not null).Select(item => item.Wordform!.Value)
            .Distinct().ToArray();
        var result = new Dictionary<Guid, ReadingState>();
        foreach (var wordformId in wordforms)
        {
            var wordform = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().GetObject(wordformId);
            foreach (var analysis in wordform.AnalysesOC)
                result[analysis.Guid] = new ReadingState(
                    analysis.MorphBundlesOS.Select(item => item.Guid).ToArray(),
                    analysis.EvaluationsRC.Select(item => item.Guid).Order().ToArray(),
                    analysis.GetAgentOpinion(cache.LangProject.DefaultUserAgent),
                    analysis.GetAgentOpinion(cache.LangProject.DefaultParserAgent),
                    wordform.Form.VernacularDefaultWritingSystem.Text);
        }
        return result;
    }

    private static void AssertPreservedReadings(LcmCache cache, IReadOnlyDictionary<Guid, ReadingState> before)
    {
        foreach (var (analysisId, expected) in before)
        {
            var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisRepository>().GetObject(analysisId);
            var wordform = (IWfiWordform)analysis.Owner!;
            Assert.Equal(expected.BundleOrder, analysis.MorphBundlesOS.Select(item => item.Guid));
            Assert.Equal(expected.Evaluations, analysis.EvaluationsRC.Select(item => item.Guid).Order());
            Assert.Equal(expected.UserOpinion, analysis.GetAgentOpinion(cache.LangProject.DefaultUserAgent));
            Assert.Equal(expected.ParserOpinion, analysis.GetAgentOpinion(cache.LangProject.DefaultParserAgent));
            Assert.Equal(expected.Surface, wordform.Form.VernacularDefaultWritingSystem.Text);
        }
    }

    private static bool IsOrdinaryPrefixOrSuffix(IMoAffixAllomorph form) => form.MorphTypeRA?.Guid is
        var id && (id == MoMorphTypeTags.kguidMorphPrefix || id == MoMorphTypeTags.kguidMorphSuffix);

    private static string Position(IMoAffixAllomorph form) =>
        form.MorphTypeRA?.Guid == MoMorphTypeTags.kguidMorphPrefix ? "prefix" : "suffix";

    private static string FormText(LcmCache cache, IMoAffixAllomorph form) =>
        form.Form.get_String(cache.DefaultVernWs)?.Text ?? string.Empty;

    private static string NormalizeForm(string form) =>
        form.Trim('-', ' ').Normalize(System.Text.NormalizationForm.FormD);

    private static string TreeDigest(string root)
    {
        var manifest = string.Join('\n', Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => Path.GetRelativePath(root, path) + "|" +
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))));
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(manifest)));
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var directory in Directory.GetDirectories(source))
            CopyTree(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private sealed record ReadingState(Guid[] BundleOrder, Guid[] Evaluations,
        Opinions UserOpinion, Opinions ParserOpinion, string Surface);
}
