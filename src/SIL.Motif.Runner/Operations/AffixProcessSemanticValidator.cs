using System;
using System.Collections.Generic;
using System.Linq;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;

namespace SIL.Motif.Runner.Operations;

internal static class AffixProcessSemanticValidator
{
    internal static void ValidateProposal(LcmCache cache, IReadOnlyList<OperationEnvelope> operations)
    {
        foreach (var process in FindAffectedProcesses(cache, operations)) Validate(cache, process);
    }

    internal static IReadOnlyList<IMoAffixProcess> FindAffectedProcesses(LcmCache cache,
        IReadOnlyList<OperationEnvelope> operations)
    {
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        var processes = new Dictionary<Guid, IMoAffixProcess>();
        foreach (var operation in operations.Where(ChangesProcessSemantics))
        {
            if (operation.Target is { } target && repository.IsValidObjectId(target.ToGuid()))
                Include(repository.GetObject(target.ToGuid()), ChangesSharedEntryMetadata(operation));
            if (operation.EntityId is { } entity && repository.IsValidObjectId(entity.ToGuid()))
                Include(repository.GetObject(entity.ToGuid()), ChangesSharedEntryMetadata(operation));
        }
        return processes.Values.ToArray();

        void Include(ICmObject value, bool includeEntryProcesses)
        {
            for (ICmObject? current = value; current is not null; current = current.Owner)
            {
                if (current is IMoAffixProcess process)
                {
                    processes[process.Guid] = process;
                    return;
                }
                if (includeEntryProcesses && current is ILexEntry entry)
                {
                    if (entry.LexemeFormOA is IMoAffixProcess lexeme) processes[lexeme.Guid] = lexeme;
                    foreach (var alternate in entry.AlternateFormsOS.OfType<IMoAffixProcess>())
                        processes[alternate.Guid] = alternate;
                    return;
                }
            }
        }
    }

    private static bool ChangesSharedEntryMetadata(OperationEnvelope operation) =>
        operation.Kind is LexEntryMorphoSyntaxAnalysesOperationKinds.Create or
        LexSenseMorphoSyntaxAnalysisOperationKinds.SetMorphoSyntaxAnalysis or
        LexSenseMorphoSyntaxAnalysisOperationKinds.ClearMorphoSyntaxAnalysis ||
        operation.Kind.StartsWith("grammar/moInflAffMsa/", StringComparison.Ordinal) ||
        operation.Kind.StartsWith("grammar/moDerivAffMsa/", StringComparison.Ordinal) ||
        operation.Kind.StartsWith("grammar/moUnclassifiedAffixMsa/", StringComparison.Ordinal);

    private static bool ChangesProcessSemantics(OperationEnvelope operation) =>
        operation.Kind == LexEntryLexemeFormOperationKinds.CreateLexemeForm &&
        operation.After is { ValueKind: System.Text.Json.JsonValueKind.Object } after &&
        after.TryGetProperty("class", out var concreteClass) &&
        concreteClass.ValueKind == System.Text.Json.JsonValueKind.String && concreteClass.GetString() == "MoAffixProcess" ||
        ChangesSharedEntryMetadata(operation) ||
        operation.Kind.StartsWith("grammar/moAffixProcess/", StringComparison.Ordinal) ||
        operation.Kind.StartsWith("grammar/moForm/", StringComparison.Ordinal) ||
        operation.Kind.StartsWith("grammar/moAffixForm/", StringComparison.Ordinal) ||
        operation.Kind.StartsWith("grammar/moCopyFromInput/", StringComparison.Ordinal) ||
        operation.Kind.StartsWith("grammar/moInsertPhones/", StringComparison.Ordinal) ||
        operation.Kind.StartsWith("grammar/phSimpleContext", StringComparison.Ordinal) ||
        operation.Kind.StartsWith("grammar/phSequenceContext/", StringComparison.Ordinal) ||
        operation.Kind.StartsWith("grammar/phIterationContext/", StringComparison.Ordinal);

    internal static void Validate(LcmCache cache, IMoAffixProcess process)
    {
        if (process.Owner is not ILexEntry entry || !cache.LangProject.LexDbOA.Entries.Contains(entry) ||
            (entry.LexemeFormOA?.Guid != process.Guid && !entry.AlternateFormsOS.Contains(process)))
            throw new InvalidOperationException("An affix process must belong to an entry in this project's lexicon.");
        if (process.MorphTypeRA is not { } morphType)
            throw new InvalidOperationException("An affix process needs a morph type.");
        AffixProcessConstruction.RequireAffixMorphType(morphType);
        if (process.IsAbstract)
            throw new InvalidOperationException("An authored affix process must be concrete.");
        if (process.InputOS.Count is < 1 or > 16 || process.OutputOS.Count is < 1 or > 256)
            throw new InvalidOperationException("An affix process needs 1–16 flat inputs and 1–256 expanded output mappings.");
        var copies = new Dictionary<Guid, int>();
        foreach (var input in process.InputOS)
        {
            if (input.Owner?.Guid != process.Guid)
                throw new InvalidOperationException("Every process input must belong to its process.");
            copies.Add(input.Guid, 0);
            switch (input)
            {
                case IPhVariable when input.Guid == process.InputOS.Last().Guid:
                    break;
                case IPhSimpleContextSeg segment when segment.FeatureStructureRA is { } phone:
                    RequirePhoneme(cache, CanonicalId.FromGuid(phone.Guid));
                    break;
                case IPhSimpleContextNC context when context.FeatureStructureRA is { } naturalClass:
                    if (context.PlusConstrRS.Count != 0 || context.MinusConstrRS.Count != 0)
                        throw new InvalidOperationException("Affix input classes cannot carry alpha feature constraints.");
                    RequireNaturalClass(cache, CanonicalId.FromGuid(naturalClass.Guid));
                    break;
                default:
                    throw new InvalidOperationException("Affix input must be a complete flat segment, class, or last remaining-stem variable.");
            }
        }
        var inserted = false;
        foreach (var output in process.OutputOS)
        {
            if (output.Owner?.Guid != process.Guid)
                throw new InvalidOperationException("Every output mapping must belong to its process.");
            switch (output)
            {
                case IMoCopyFromInput copy when copy.ContentRA is { } part && copies.ContainsKey(part.Guid) &&
                    part.Owner?.Guid == process.Guid:
                    copies[part.Guid]++;
                    break;
                case IMoInsertPhones insert when insert.ContentRS.Count == 1 && insert.ContentRS[0] is IPhPhoneme phone:
                    RequirePhoneme(cache, CanonicalId.FromGuid(phone.Guid));
                    inserted = true;
                    break;
                default:
                    throw new InvalidOperationException("Affix output must copy a top-level input of the same process or insert one phoneme.");
            }
        }
        if (process.InputOS.Last() is IPhVariable lastVariable && copies[lastVariable.Guid] == 0)
            throw new InvalidOperationException("The remaining-stem input must survive at least once; omitting it deletes the stem.");
        if (copies.Values.All(count => count == 0))
            throw new InvalidOperationException("An output that copies no input deletes the whole stem.");
        if (!inserted && copies.Values.All(count => count == 1))
            throw new InvalidOperationException("An identity-only realization is not an affix process.");
        if (entry.MorphoSyntaxAnalysesOC.Count == 0 || entry.SensesOS.Count == 0)
            throw new InvalidOperationException("A process entry needs affix grammatical info and a sense.");
        foreach (var msa in entry.MorphoSyntaxAnalysesOC)
        {
            switch (msa)
            {
                case IMoInflAffMsa inflectional when inflectional.PartOfSpeechRA is { } category:
                    RequireCategory(cache, category);
                    foreach (var slot in inflectional.SlotsRC)
                    {
                        var owner = slot.Owner as IPartOfSpeech;
                        var current = category;
                        while (current is not null && current.Guid != owner?.Guid) current = current.Owner as IPartOfSpeech;
                        if (current is null) throw new InvalidOperationException("An affix slot must belong to its category or an ancestor.");
                    }
                    break;
                case IMoDerivAffMsa derivational when derivational.FromPartOfSpeechRA is { } from &&
                    derivational.ToPartOfSpeechRA is { } to:
                    RequireCategory(cache, from);
                    RequireCategory(cache, to);
                    break;
                case IMoUnclassifiedAffixMsa unclassified when unclassified.PartOfSpeechRA is { } category:
                    RequireCategory(cache, category);
                    break;
                default:
                    throw new InvalidOperationException("A process entry needs complete affix grammatical info, never a stem msa.");
            }
        }
        foreach (var sense in entry.SensesOS)
            if (sense.MorphoSyntaxAnalysisRA is not { } msa || !entry.MorphoSyntaxAnalysesOC.Contains(msa))
                throw new InvalidOperationException("A process sense must reference grammatical info of its own entry.");
    }

    internal static IPhPhoneme RequirePhoneme(LcmCache cache, CanonicalId id)
    {
        var phone = ReferenceFieldLowering.Resolve<IPhPhoneme>(cache, id, "affix process");
        var firstSet = cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.FirstOrDefault();
        if (firstSet is null || !firstSet.PhonemesOC.Contains(phone) || phone.Owner?.Guid != firstSet.Guid)
            throw new InvalidOperationException("An affix phoneme must belong to the project's first phoneme set.");
        if (phone.CodesOS.Count == 0 || string.IsNullOrWhiteSpace(phone.CodesOS[0].Representation.get_String(cache.DefaultVernWs)?.Text))
            throw new InvalidOperationException("An affix phoneme needs a usable first vernacular code.");
        return phone;
    }

    internal static IPhNaturalClass RequireNaturalClass(LcmCache cache, CanonicalId id)
    {
        var naturalClass = ReferenceFieldLowering.Resolve<IPhNaturalClass>(cache, id, "affix process");
        var data = cache.LangProject.PhonologicalDataOA;
        if (!data.NaturalClassesOS.Contains(naturalClass) || naturalClass.Owner?.Guid != data.Guid)
            throw new InvalidOperationException("An affix natural class must belong to this project's phonological data.");
        switch (naturalClass)
        {
            case IPhNCSegments segments when segments.SegmentsRC.Count > 0:
                foreach (var phone in segments.SegmentsRC) RequirePhoneme(cache, CanonicalId.FromGuid(phone.Guid));
                break;
            case IPhNCFeatures features when features.FeaturesOA is { } structure && structure.FeatureSpecsOC.Count > 0:
                var seen = new HashSet<Guid>();
                foreach (var spec in structure.FeatureSpecsOC)
                {
                    if (spec is not IFsClosedValue closed || closed.FeatureRA is not IFsClosedFeature feature ||
                        closed.ValueRA is not { } value || !cache.LangProject.PhFeatureSystemOA.FeaturesOC.Contains(feature) ||
                        !feature.ValuesOC.Contains(value) || !seen.Add(feature.Guid))
                        throw new InvalidOperationException("An affix feature class needs distinct closed phonological features and their values.");
                }
                break;
            default:
                throw new InvalidOperationException("An affix natural class must have nonempty segments or usable closed features.");
        }
        return naturalClass;
    }

    internal static void RequireCategory(LcmCache cache, IPartOfSpeech category)
    {
        if (!cache.LangProject.PartsOfSpeechOA.ReallyReallyAllPossibilities.Contains(category))
            throw new InvalidOperationException("An affix category must belong to this project's parts-of-speech list.");
    }
}
