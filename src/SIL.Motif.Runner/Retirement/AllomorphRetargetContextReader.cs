using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Snapshotting;

namespace SIL.Motif.Runner.Retirement;

internal static class AllomorphRetargetContextReader
{
    public static ExpectedEffect BundleEffect(
        LcmCache cache, CanonicalId target, IWfiMorphBundle bundle,
        IMoAffixAllomorph source, IMoAffixAllomorph replacement)
    {
        if (bundle.Owner is not IWfiAnalysis analysis || analysis.Owner is not IWfiWordform wordform)
            throw new InvalidOperationException("A bundle retarget requires a bundle in an analysis of a wordform.");
        var values = new
        {
            source = FormState(cache, source),
            replacement = FormState(cache, replacement),
            bundle = BundleState(cache, bundle),
            wordform = WordformState(cache, wordform),
            textUses = ReadTextUses(cache, wordform)
        };
        return Effect(target, values);
    }

    public static ExpectedEffect AdhocEffect(
        LcmCache cache, CanonicalId target, IMoAlloAdhocProhib rule, AdhocRetargetPayload payload)
    {
        var replacements = payload.Fields.SelectMany(field =>
            field.Before.Zip(field.After, (before, after) => (before, after)))
            .Where(pair => pair.before != pair.after)
            .OrderBy(pair => pair.before.Value, StringComparer.Ordinal)
            .ThenBy(pair => pair.after.Value, StringComparer.Ordinal)
            .Select(pair => new
            {
                source = FormState(cache, BundleMorphHandler.ResolveAffix(cache, pair.before,
                    AllomorphRetargetOperationKinds.RetargetAdhocReferences)),
                replacement = FormState(cache, BundleMorphHandler.ResolveAffix(cache, pair.after,
                    AllomorphRetargetOperationKinds.RetargetAdhocReferences))
            })
            .ToArray();
        return Effect(target, new
        {
            rule = rule.Guid.ToString("D"),
            owner = rule.Owner?.Guid.ToString("D"),
            rule.Disabled,
            replacements
        });
    }

    private static ExpectedEffect Effect(CanonicalId target, object state)
    {
        var digest = "sha256:" + Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(state)))).ToLowerInvariant();
        var value = new Dictionary<string, string>(StringComparer.Ordinal) { ["digest"] = digest };
        return new ExpectedEffect(target, SnapshotFields.AllomorphRetargetContext, value, value);
    }

    private static object FormState(LcmCache cache, IMoAffixAllomorph form)
    {
        var entry = form.Owner as ILexEntry;
        return new
        {
            id = form.Guid.ToString("D"),
            owner = form.Owner?.Guid.ToString("D"),
            location = entry is null ? null : entry.LexemeFormOA?.Guid == form.Guid
                ? "lexeme"
                : entry.AlternateFormsOS.Any(item => item.Guid == form.Guid) ? "alternate" : "other",
            morphType = form.MorphTypeRA?.Guid.ToString("D"),
            form.IsAbstract,
            alternatives = Alternatives(cache, form.Form),
            featureStructure = form.MsEnvFeaturesOA?.Guid.ToString("D"),
            phoneEnvironments = form.PhoneEnvRC.Select(item => item.Guid.ToString("D"))
                .Order(StringComparer.Ordinal).ToArray(),
            positions = form.PositionRS.Select(item => item.Guid.ToString("D")).ToArray(),
            inflectionClasses = form.InflectionClassesRC.Select(item => item.Guid.ToString("D"))
                .Order(StringComparer.Ordinal).ToArray(),
            entryLexeme = entry?.LexemeFormOA?.Guid.ToString("D"),
            entryAlternates = entry?.AlternateFormsOS.Select(item => item.Guid.ToString("D")).ToArray()
        };
    }

    private static object BundleState(LcmCache cache, IWfiMorphBundle bundle) => new
    {
        id = bundle.Guid.ToString("D"),
        morph = bundle.MorphRA?.Guid.ToString("D"),
        msa = bundle.MsaRA?.Guid.ToString("D"),
        inflType = bundle.InflTypeRA?.Guid.ToString("D"),
        sense = bundle.SenseRA?.Guid.ToString("D"),
        alternatives = Alternatives(cache, bundle.Form)
    };

    private static object AnalysisState(LcmCache cache, IWfiAnalysis analysis) => new
    {
        id = analysis.Guid.ToString("D"),
        category = analysis.CategoryRA?.Guid.ToString("D"),
        evaluations = analysis.EvaluationsRC.Select(evaluation => new
            {
                id = evaluation.Guid.ToString("D"),
                agent = evaluation.Owner?.Guid.ToString("D"),
                opinion = evaluation.Owner switch
                {
                    ICmAgent agent when agent.ApprovesOA?.Guid == evaluation.Guid => "approved",
                    ICmAgent agent when agent.DisapprovesOA?.Guid == evaluation.Guid => "disapproved",
                    _ => "unknown"
                }
            })
            .OrderBy(item => item.id, StringComparer.Ordinal).ToArray(),
        bundles = analysis.MorphBundlesOS.Select(bundle => BundleState(cache, bundle)).ToArray()
    };

    private static object WordformState(LcmCache cache, IWfiWordform wordform) => new
    {
        id = wordform.Guid.ToString("D"),
        alternatives = Alternatives(cache, wordform.Form),
        analyses = wordform.AnalysesOC.OrderBy(item => item.Guid)
            .Select(item => AnalysisState(cache, item)).ToArray()
    };

    private static object[] ReadTextUses(LcmCache cache, IWfiWordform wordform)
    {
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        return repository.AllInstances().OfType<ISegment>()
            .Where(segment => segment.AnalysesRS.Any(item => ReferencedWordform(item)?.Guid == wordform.Guid))
            .OrderBy(segment => segment.Guid)
            .Select(segment => (object)new
            {
                id = segment.Guid.ToString("D"),
                analyses = segment.AnalysesRS.Select(item => item.Guid.ToString("D")).ToArray()
            })
            .ToArray();
    }

    private static IWfiWordform? ReferencedWordform(IAnalysis analysis) => analysis switch
    {
        IWfiWordform wordform => wordform,
        IWfiAnalysis wordAnalysis => wordAnalysis.Owner as IWfiWordform,
        IWfiGloss gloss => (gloss.Owner as IWfiAnalysis)?.Owner as IWfiWordform,
        _ => null
    };

    private static string[] Alternatives(LcmCache cache, IMultiAccessorBase value) =>
        value.AvailableWritingSystemIds
            .Select(ws => (Tag: cache.WritingSystemFactory.GetStrFromWs(ws),
                Text: value.get_String(ws)?.Text ?? string.Empty))
            .OrderBy(item => item.Tag, StringComparer.Ordinal)
            .Select(item => item.Tag + "=" + item.Text)
            .ToArray();
}
