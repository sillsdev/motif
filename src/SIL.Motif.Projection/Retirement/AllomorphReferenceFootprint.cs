using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using System.Collections.ObjectModel;
using SIL.LCModel;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Projection.HumanJudgments;

namespace SIL.Motif.Projection.Retirement;

/// <summary>Reads the complete LibLCM reference footprint for a set of forms without changing the cache.</summary>
public static class AllomorphReferenceFootprintReader
{
    private const string MasterModelResource = "SIL.Motif.Projection.LibLcm.MasterLCModel.xml";
    private static readonly Lazy<MasterModelSurface> MasterModel = new(ReadMasterModel);

    /// <summary>
    /// Enumerates every modeled and custom reference slot that can hold one of <paramref name="retiredForms"/>,
    /// the objects owned by those forms, and all readings on affected wordforms.
    /// </summary>
    public static AllomorphReferenceFootprint Read(LcmCache cache, IEnumerable<Guid> retiredForms)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(retiredForms);

        var requestedIds = retiredForms.Distinct().Order().ToArray();
        if (requestedIds.Length == 0) throw new ArgumentException("At least one form identity is required.", nameof(retiredForms));

        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        var targetForms = new Dictionary<int, IMoForm>();
        foreach (var id in requestedIds)
        {
            if (!repository.TryGetObject(id, out var value) || value is not IMoForm form)
                throw new ArgumentException($"Form '{id:D}' is not present in the loaded project.", nameof(retiredForms));
            targetForms.Add(form.Hvo, form);
        }

        var model = MasterModel.Value;
        var metadata = (IFwMetaDataCacheManaged)cache.MetaDataCacheAccessor;
        var unavailable = new SortedSet<string>(StringComparer.Ordinal);
        var fields = ReadFieldSurface(metadata, model, targetForms.Values, unavailable);
        var modelSha256 = Digest(model.Bytes);
        var metadataSha256 = Digest(Encoding.UTF8.GetBytes(string.Join('\n', fields.All
            .OrderBy(field => field.DeclaringClass, StringComparer.Ordinal)
            .ThenBy(field => field.Name, StringComparer.Ordinal)
            .Select(field =>
            {
                var shape = Shape(field.Type);
                return $"{field.DeclaringClass}.{field.Name}|{shape.Kind}|{shape.Cardinality}|{field.DestinationClass}|{field.IsCustom}";
            }))));

        var objects = repository.AllInstances().ToArray();
        var byHvo = objects.ToDictionary(item => item.Hvo);
        var references = ReadIncomingReferences(cache, model, fields, objects, targetForms, unavailable);
        var owned = ReadOwnedDependents(cache, model, fields, byHvo, targetForms, unavailable);
        var affected = ReadAffectedWordforms(cache, references);
        var historical = ReadHistoricalReferences(cache, targetForms.Values, unavailable);
        var surface = fields.Incoming.Select(field => new AllomorphReferenceField(
            field.DeclaringClass, field.Name, field.Kind, field.Cardinality, field.DestinationClass, field.IsCustom))
            .OrderBy(field => field.DeclaringClass, StringComparer.Ordinal)
            .ThenBy(field => field.Name, StringComparer.Ordinal)
            .ToArray();
        var sortedReferences = references.OrderBy(item => item.TargetForm)
            .ThenBy(item => item.DeclaringClass, StringComparer.Ordinal)
            .ThenBy(item => item.Field, StringComparer.Ordinal)
            .ThenBy(item => item.Ordinal)
            .ThenBy(item => item.SourceObject)
            .ToArray();
        var sortedOwned = owned.OrderBy(item => item.TargetForm)
            .ThenBy(item => item.Depth)
            .ThenBy(item => item.ParentObject)
            .ThenBy(item => item.DependentObject)
            .ToArray();
        var sortedAffected = affected.OrderBy(item => item.Wordform).ToArray();
        var unavailableItems = unavailable.ToArray();
        var counts = Count(sortedReferences, sortedOwned, sortedAffected);
        var routeNotes = fields.RouteNotes.OrderBy(item => item.DeclaringClass, StringComparer.Ordinal)
            .ThenBy(item => item.Field, StringComparer.Ordinal).ToArray();
        var payload = JsonSerializer.Serialize(new
        {
            modelSha256,
            metadataSha256,
            routeNotes,
            references = sortedReferences,
            owned = sortedOwned,
            affected = sortedAffected,
            historical,
            unavailable = unavailableItems
        });

        return new AllomorphReferenceFootprint(modelSha256, metadataSha256, Array.AsReadOnly(surface),
            Array.AsReadOnly(sortedReferences), Array.AsReadOnly(sortedOwned), Array.AsReadOnly(sortedAffected),
            historical, Array.AsReadOnly(unavailableItems), counts,
            Digest(Encoding.UTF8.GetBytes(payload)))
        {
            RouteNotes = Array.AsReadOnly(routeNotes)
        };
    }

    /// <summary>Compares the complete census with exact bundle and ad hoc destinations supplied by an author.</summary>
    public static AllomorphReferenceDestinationDiagnostics Diagnose(
        AllomorphReferenceFootprint footprint, RetireAllomorphIntent intent)
    {
        ArgumentNullException.ThrowIfNull(footprint);
        ArgumentNullException.ThrowIfNull(intent);

        var classifications = footprint.References.Select(reference => ClassifyReference(reference, intent, footprint)).ToArray();
        var unresolved = new List<AllomorphReference>();
        foreach (var classification in classifications)
        {
            if (classification.Disposition == "blocking") unresolved.Add(classification.Reference);
        }

        var approvedAnalyses = unresolved.Where(item => item.Kind == "bundle-morph" && item.Opinion == "approved")
            .Select(item => item.Analysis).OfType<Guid>().Distinct().Count();
        var adhocRules = unresolved.Where(item => item.Kind.StartsWith("adhoc-", StringComparison.Ordinal))
            .Select(item => item.SourceObject).Distinct().Count();
        var customReferences = unresolved.Count(item => item.IsCustom);
        var otherReferences = unresolved.Count(item => !item.IsCustom && item.Kind == "other-native");
        var message = $"This form has {unresolved.Count} unresolved reference rows, including {approvedAnalyses} " +
            $"Approved analyses and {adhocRules} ad hoc rules; declare every replacement destination before deleting it.";
        if (unresolved.Count > 0)
            message += " Blocking routes: " + string.Join(" | ", classifications
                .Where(item => item.Disposition == "blocking")
                .Select(item => $"{item.Reference.DeclaringClass}.{item.Reference.Field} from " +
                    $"{item.Reference.SourceClass} '{item.Reference.SourceObject:D}': {item.Reason}"));
        if (footprint.Unavailable.Count > 0)
            message += $" The census also has {footprint.Unavailable.Count} unavailable reference routes: " +
                string.Join(" | ", footprint.Unavailable);

        return new AllomorphReferenceDestinationDiagnostics(Array.AsReadOnly(unresolved.ToArray()), approvedAnalyses, adhocRules,
            customReferences, otherReferences, message)
        {
            Unavailable = footprint.Unavailable,
            Classifications = Array.AsReadOnly(classifications)
        };
    }

    private static AllomorphReferenceClassification ClassifyReference(AllomorphReference reference,
        RetireAllomorphIntent intent, AllomorphReferenceFootprint footprint)
    {
        if (reference.Kind == "bundle-morph" && HasBundleDestination(reference, intent))
            return new AllomorphReferenceClassification(reference, "moved",
                "The exact bundle, analysis, wordform, MSA, inflection type and role have an authored replacement.");
        if ((reference.Kind is "adhoc-first-allomorph" or "adhoc-rest-of-allos" or "adhoc-legacy-allomorphs") &&
            HasAdhocDestination(reference, intent))
            return new AllomorphReferenceClassification(reference, "moved",
                "The exact prohibition field occurrence has an authored replacement.");
        if (reference.Kind == "entry-alternate-form" && intent.Scope == AllomorphRetirementScope.Affix)
            return new AllomorphReferenceClassification(reference, "removed-with-the-graph",
                "Deleting the alternate removes its owning entry collection link in the same Proposal.");
        if (reference.Kind == "owner-backlink" && footprint.OwnedDependents.Any(item =>
                item.TargetForm == reference.TargetForm && item.DependentObject == reference.SourceObject))
            return new AllomorphReferenceClassification(reference, "removed-with-the-graph",
                "The owner backlink belongs to a dependent already bound in the retired owning graph.");
        if (reference.Kind == "self-reference" && reference.SourceObject == reference.TargetForm)
            return new AllomorphReferenceClassification(reference, "removed-with-the-graph",
                "The self-reference is on the form being deleted.");
        if (reference.Kind == "derived-allomorph-view" && footprint.References.Any(item =>
                item.TargetForm == reference.TargetForm && item.Kind == "entry-alternate-form"))
            return new AllomorphReferenceClassification(reference, "removed-with-the-graph",
                "The aggregate is derived from the alternate-form edge removed by the same Proposal.");
        return new AllomorphReferenceClassification(reference, "blocking",
            $"No supported retirement operation accounts for {reference.DeclaringClass}.{reference.Field}.");
    }

    private static bool HasBundleDestination(AllomorphReference reference, RetireAllomorphIntent intent)
    {
        var form = reference.TargetForm;
        if (reference.Bundle is not { } bundle || reference.Analysis is not { } analysis ||
            reference.Wordform is not { } wordform || reference.Msa is not { } msa)
            return false;

        var declaration = intent.Bundles.FirstOrDefault(item => SameId(item.Bundle, bundle) &&
            SameId(item.Analysis, analysis) && SameId(item.Wordform, wordform) &&
            SameId(item.RetiredForm, form) && SameId(item.Msa, msa) &&
            NullableSameId(item.InflType, reference.InflType) && item.ExpansionRole == "whole");
        return declaration is not null && intent.RoleReplacements.Any(item => SameId(item.RetiredForm, form) &&
            SameId(item.Msa, msa) && NullableSameId(item.InflType, reference.InflType) && item.ExpansionRole == "whole");
    }

    private static bool HasAdhocDestination(AllomorphReference reference, RetireAllomorphIntent intent) =>
        reference.Rule is { } rule && reference.TargetForm is { } form && reference.Field is { } field &&
        intent.AdhocReplacements.Any(item => SameId(item.Rule, rule) && item.Field == field &&
            item.Ordinal == reference.Ordinal && SameId(item.RetiredForm, form));

    private static bool SameId(string value, Guid guid) => CanonicalId.TryParse(value, out var id) && id.ToGuid() == guid;

    private static bool NullableSameId(string? value, Guid? guid) => value is null
        ? guid is null
        : guid is { } expected && SameId(value, expected);

    private static ReferenceFieldSurface ReadFieldSurface(IFwMetaDataCacheManaged metadata,
        MasterModelSurface model, IEnumerable<IMoForm> targetForms, ISet<string> unavailable)
    {
        var targetClasses = targetForms.Select(form => form.ClassName).Distinct(StringComparer.Ordinal).ToArray();
        var kernelMetadata = (IFwMetaDataCache)metadata;
        var runtime = metadata.GetFieldIds().Select(flid => new RuntimeField(
            flid,
            metadata.GetOwnClsName(flid),
            metadata.GetFieldName(flid),
            (CellarPropertyType)metadata.GetFieldType(flid),
            metadata.GetDstClsName(flid),
            metadata.IsCustom(flid),
            kernelMetadata.get_IsVirtual(flid))).ToArray();
        var storedRuntime = runtime.Where(field => !field.IsVirtual).ToArray();
        var virtualKeys = runtime.Where(field => field.IsVirtual)
            .Select(field => Key(field.DeclaringClass, field.Name)).ToHashSet(StringComparer.Ordinal);
        var runtimeByKey = storedRuntime.ToDictionary(field => Key(field.DeclaringClass, field.Name), StringComparer.Ordinal);
        var modelByKey = model.Fields.ToDictionary(field => Key(field.DeclaringClass, field.Name), StringComparer.Ordinal);
        var incoming = new List<ReferenceField>();
        var owning = new List<ReferenceField>();
        var routeNotes = new List<AllomorphReferenceRouteNote>();

        foreach (var declared in model.Fields.Where(field => field.Kind is "owning" or "rel"))
        {
            var acceptsForm = targetClasses.Any(target => model.IsSameOrSubclass(target, declared.DestinationClass));
            var ownsForm = declared.Kind == "owning" && model.IsClassRelatedToMoForm(declared.DeclaringClass);
            if (!runtimeByKey.TryGetValue(Key(declared.DeclaringClass, declared.Name), out var field))
            {
                if (!virtualKeys.Contains(Key(declared.DeclaringClass, declared.Name)) && (acceptsForm || ownsForm))
                    unavailable.Add($"MasterLCModel.xml declares {declared.DeclaringClass}.{declared.Name}, but cache metadata omits it.");
                continue;
            }

            if (!TryShape(field.Type, out var kind, out var cardinality) ||
                kind != declared.Kind || cardinality != declared.Cardinality ||
                !string.Equals(field.DestinationClass, declared.DestinationClass, StringComparison.Ordinal))
            {
                if (acceptsForm || ownsForm)
                    unavailable.Add($"MasterLCModel.xml and cache metadata disagree on {declared.DeclaringClass}.{declared.Name}.");
                continue;
            }

            if (kind == "owning")
                owning.Add(ToSurface(field, kind, cardinality));

            if (acceptsForm) incoming.Add(ToSurface(field, kind, cardinality));
        }

        foreach (var field in storedRuntime.Where(field => TryShape(field.Type, out _, out _)))
        {
            if (!model.Classes.ContainsKey(field.DeclaringClass))
            {
                unavailable.Add($"Cache metadata gives reference field {field.DeclaringClass}.{field.Name} an unknown declaring class.");
                continue;
            }
            if (!model.Classes.ContainsKey(field.DestinationClass))
            {
                unavailable.Add($"Cache metadata gives {field.DeclaringClass}.{field.Name} an unknown destination class '{field.DestinationClass}'.");
                continue;
            }
            var acceptsForm = targetClasses.Any(target => model.IsSameOrSubclass(target, field.DestinationClass));
            var ownsForm = (field.Type is CellarPropertyType.OwningAtomic or CellarPropertyType.OwningCollection or CellarPropertyType.OwningSequence) &&
                model.IsClassRelatedToMoForm(field.DeclaringClass);
            if (ownsForm)
                owning.Add(ToSurface(field, Shape(field.Type).Kind, Shape(field.Type).Cardinality));
            if (acceptsForm)
                incoming.Add(ToSurface(field, Shape(field.Type).Kind, Shape(field.Type).Cardinality));
            if ((acceptsForm || ownsForm) && !modelByKey.ContainsKey(Key(field.DeclaringClass, field.Name)))
                routeNotes.Add(ClassifyUnmodeledField(field));
        }

        return new ReferenceFieldSurface(
            incoming.DistinctBy(field => Key(field.DeclaringClass, field.Name)).ToArray(),
            owning.DistinctBy(field => Key(field.DeclaringClass, field.Name)).ToArray(), storedRuntime,
            Array.AsReadOnly(routeNotes.DistinctBy(item => Key(item.DeclaringClass, item.Field))
                .OrderBy(item => item.DeclaringClass, StringComparer.Ordinal)
                .ThenBy(item => item.Field, StringComparer.Ordinal).ToArray()));
    }

    private static AllomorphReferenceRouteNote ClassifyUnmodeledField(RuntimeField field) =>
        (field.DeclaringClass, field.Name) switch
        {
            ("CmObject", "Owner") => new(field.DeclaringClass, field.Name, "removed-with-the-graph",
                "Owner is the inverse of LibLCM owning fields; owned descendants are enumerated from those fields and bind with the retired graph."),
            ("CmObject", "Self") => new(field.DeclaringClass, field.Name, "removed-with-the-graph",
                "Self refers to its own object; the source object is included in the deletion graph."),
            ("LexDb", "AllAllomorphs") or ("LexDb", "AllPossibleAllomorphs") or
                ("LexEntry", "AllAllomorphs") => new(field.DeclaringClass, field.Name, "derived-view",
                "This aggregate is derived from stored entry form fields; each live target row is checked against those edges."),
            _ => new(field.DeclaringClass, field.Name, "enumerated-live-rows",
                "The runtime metadata field is scanned for live target rows; each occurrence without an explicit disposition blocks retirement."),
        };

    private static List<AllomorphReference> ReadIncomingReferences(LcmCache cache, MasterModelSurface model,
        ReferenceFieldSurface fields, IReadOnlyList<ICmObject> objects, IReadOnlyDictionary<int, IMoForm> targetForms,
        ISet<string> unavailable)
    {
        var result = new List<AllomorphReference>();
        foreach (var field in fields.Incoming)
        {
            foreach (var source in objects.Where(item => model.IsSameOrSubclass(item.ClassName, field.DeclaringClass)))
            {
                foreach (var link in ReadLinks(cache, source, field, unavailable))
                {
                    if (!targetForms.TryGetValue(link.Hvo, out var target)) continue;
                    result.Add(BuildReference(cache, source, target, field, link.Ordinal));
                }
            }
        }
        return result;
    }

    private static List<AllomorphOwnedDependent> ReadOwnedDependents(LcmCache cache, MasterModelSurface model,
        ReferenceFieldSurface fields, IReadOnlyDictionary<int, ICmObject> objects,
        IReadOnlyDictionary<int, IMoForm> targetForms, ISet<string> unavailable)
    {
        var result = new List<AllomorphOwnedDependent>();
        foreach (var (hvo, target) in targetForms)
        {
            var visited = new HashSet<int> { hvo };
            var queue = new Queue<(ICmObject Parent, int Depth)>();
            queue.Enqueue((target, 0));
            while (queue.TryDequeue(out var item))
            {
                foreach (var field in fields.Owning.Where(field => model.IsSameOrSubclass(item.Parent.ClassName,
                             field.DeclaringClass)))
                {
                    foreach (var link in ReadLinks(cache, item.Parent, field, unavailable))
                    {
                        if (!objects.TryGetValue(link.Hvo, out var child))
                        {
                            unavailable.Add($"Owned object {link.Hvo} from {field.DeclaringClass}.{field.Name} is absent from the cache repository.");
                            continue;
                        }
                        if (!visited.Add(child.Hvo)) continue;
                        var ordinal = link.Ordinal ?? 0;
                        result.Add(new AllomorphOwnedDependent(target.Guid, item.Parent.Guid, child.Guid,
                            child.ClassName, field.DeclaringClass, field.Name, ordinal, item.Depth + 1, field.IsCustom));
                        queue.Enqueue((child, item.Depth + 1));
                    }
                }
            }
        }
        return result;
    }

    private static IReadOnlyList<ObjectLink> ReadLinks(LcmCache cache, ICmObject source, ReferenceField field,
        ISet<string> unavailable)
    {
        var result = new List<ObjectLink>();
        try
        {
            if (field.Cardinality == "atomic")
            {
                var target = cache.DomainDataByFlid.get_ObjectProp(source.Hvo, field.Id);
                if (target != 0) result.Add(new ObjectLink(target, null));
                return result;
            }

            var count = cache.DomainDataByFlid.get_VecSize(source.Hvo, field.Id);
            for (var index = 0; index < count; index++)
            {
                var target = cache.DomainDataByFlid.get_VecItem(source.Hvo, field.Id, index);
                if (target != 0) result.Add(new ObjectLink(target, field.Cardinality == "seq" ? index : null));
            }
        }
        catch (Exception error)
        {
            unavailable.Add($"Could not read {field.DeclaringClass}.{field.Name} on {source.ClassName} '{source.Guid:D}': {error.GetType().Name}.");
        }
        return result;
    }

    private static AllomorphReference BuildReference(LcmCache cache, ICmObject source, IMoForm target,
        ReferenceField field, int? ordinal)
    {
        var kind = Classify(source, field);
        Guid? analysisId = null;
        Guid? wordformId = null;
        Guid? bundleId = null;
        Guid? msaId = null;
        Guid? inflTypeId = null;
        string? opinion = null;
        Guid? ruleId = null;
        bool? grouped = null;
        bool? enabled = null;

        if (source is IWfiMorphBundle bundle)
        {
            bundleId = bundle.Guid;
            msaId = bundle.MsaRA?.Guid;
            inflTypeId = bundle.InflTypeRA?.Guid;
            if (bundle.Owner is IWfiAnalysis analysis)
            {
                analysisId = analysis.Guid;
                wordformId = analysis.Owner is IWfiWordform wordform ? wordform.Guid : null;
                opinion = Opinion(analysis, cache);
            }
        }
        if (source is IMoAlloAdhocProhib prohibition)
        {
            ruleId = prohibition.Guid;
            grouped = prohibition.Owner?.ClassName == "MoAdhocProhibGr";
            enabled = !prohibition.Disabled;
        }

        return new AllomorphReference(target.Guid, source.Guid, source.ClassName,
            field.DeclaringClass, field.Name, ordinal, kind, field.Kind, field.Cardinality, field.IsCustom,
            bundleId, analysisId, wordformId, msaId, inflTypeId, opinion, ruleId, grouped, enabled);
    }

    private static string Classify(ICmObject source, ReferenceField field)
    {
        if (field.DeclaringClass == "CmObject" && field.Name == "Owner") return "owner-backlink";
        if (field.DeclaringClass == "CmObject" && field.Name == "Self") return "self-reference";
        if ((field.DeclaringClass, field.Name) is
            ("LexDb", "AllAllomorphs") or ("LexDb", "AllPossibleAllomorphs") or ("LexEntry", "AllAllomorphs"))
            return "derived-allomorph-view";
        if (source is IWfiMorphBundle && field.Name == "Morph") return "bundle-morph";
        if (source is IMoAlloAdhocProhib)
            return field.Name switch
            {
                "FirstAllomorph" => "adhoc-first-allomorph",
                "RestOfAllos" => "adhoc-rest-of-allos",
                "Allomorphs" => "adhoc-legacy-allomorphs",
                _ => "other-native"
            };
        if (source is ILexEntry && field.Name == "AlternateForms") return "entry-alternate-form";
        if (source is ILexEntry && field.Name == "LexemeForm") return "entry-lexeme-form";
        return field.IsCustom ? "custom-native" : "other-native";
    }

    private static IReadOnlyList<AllomorphAffectedWordform> ReadAffectedWordforms(
        LcmCache cache, IReadOnlyList<AllomorphReference> references)
    {
        var ids = references.Where(item => item.Kind == "bundle-morph").Select(item => item.Wordform)
            .OfType<Guid>().Distinct().Order().ToArray();
        var result = new List<AllomorphAffectedWordform>();
        foreach (var id in ids)
        {
            if (!cache.ServiceLocator.ObjectRepository.TryGetObject(id, out var value) || value is not IWfiWordform wordform)
                continue;
            var forms = wordform.Form.AvailableWritingSystemIds.Order()
                .Select(ws => new AllomorphWordformAlternative(cache.WritingSystemFactory.GetStrFromWs(ws),
                    wordform.Form.get_String(ws)?.Text ?? string.Empty)).ToArray();
            var analyses = wordform.AnalysesOC.Select(analysis => ReadAffectedAnalysis(analysis, cache))
                .OrderBy(analysis => analysis.Analysis).ToArray();
            result.Add(new AllomorphAffectedWordform(wordform.Guid, Array.AsReadOnly(forms),
                Array.AsReadOnly(analyses), ReadTextUses(cache, wordform)));
        }
        return result;
    }

    private static IReadOnlyList<AllomorphTextUse> ReadTextUses(LcmCache cache, IWfiWordform wordform)
    {
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        return repository.AllInstances().OfType<ISegment>()
            .SelectMany(segment => segment.AnalysesRS.Select((analysis, ordinal) =>
                (Segment: segment, Analysis: analysis, Ordinal: ordinal, Kind: AnalysisKind(analysis))))
            .Where(item => ReferencedWordform(item.Analysis)?.Guid == wordform.Guid)
            .OrderBy(item => item.Segment.Guid)
            .ThenBy(item => item.Ordinal)
            .Select(item => new AllomorphTextUse(item.Segment.Guid, item.Analysis.Guid, item.Kind, item.Ordinal))
            .ToArray();
    }

    private static string AnalysisKind(IAnalysis analysis) => analysis switch
    {
        IWfiWordform => "wordform",
        IWfiAnalysis => "analysis",
        IWfiGloss => "gloss",
        _ => "other-analysis"
    };

    private static IWfiWordform? ReferencedWordform(IAnalysis analysis) => analysis switch
    {
        IWfiWordform wordform => wordform,
        IWfiAnalysis wordAnalysis => wordAnalysis.Owner as IWfiWordform,
        IWfiGloss gloss => (gloss.Owner as IWfiAnalysis)?.Owner as IWfiWordform,
        _ => null
    };

    private static AllomorphAffectedAnalysis ReadAffectedAnalysis(IWfiAnalysis analysis, LcmCache cache)
    {
        var evaluations = analysis.EvaluationsRC.Select(evaluation => new AllomorphHumanEvaluation(
                evaluation.Guid, evaluation.Owner?.Guid, EvaluationOpinion(evaluation)))
            .OrderBy(evaluation => evaluation.Agent).ThenBy(evaluation => evaluation.Opinion, StringComparer.Ordinal)
            .ToArray();
        var morphs = analysis.MorphBundlesOS.Select(bundle =>
        {
            var text = bundle.Form.AvailableWritingSystemIds.Order().Select(ws =>
                new AllomorphWordformAlternative(cache.WritingSystemFactory.GetStrFromWs(ws),
                    bundle.Form.get_String(ws)?.Text ?? string.Empty)).ToArray();
            return new AllomorphAffectedMorph(bundle.Guid, bundle.MorphRA?.Guid, bundle.MsaRA?.Guid,
                bundle.InflTypeRA?.Guid, Array.AsReadOnly(text));
        }).ToArray();
        return new AllomorphAffectedAnalysis(analysis.Guid, Opinion(analysis, cache),
            Array.AsReadOnly(evaluations), Array.AsReadOnly(morphs));
    }

    private static IReadOnlyList<AllomorphHistoricalReference> ReadHistoricalReferences(
        LcmCache cache, IEnumerable<IMoForm> targetForms, ISet<string> unavailable)
    {
        var targets = targetForms.ToDictionary(form => form.Guid);
        var snapshot = HumanJudgmentReader.Read(cache);
        if (snapshot.Capability is HumanJudgmentFieldCapability.Incompatible or HumanJudgmentFieldCapability.Ambiguous)
            unavailable.Add($"Notebook human-judgment field cannot be read: {snapshot.Message}");
        foreach (var item in snapshot.Unavailable)
            unavailable.Add($"Notebook record '{item.RecordId}' has an unreadable human judgment value.");

        var result = new List<AllomorphHistoricalReference>();
        foreach (var stored in snapshot.Judgments)
        {
            foreach (var subject in HistoricalObjects(stored.Judgment))
            {
                if (!CanonicalId.TryParse(subject.Id, out var id) || !targets.ContainsKey(id.ToGuid())) continue;
                result.Add(new AllomorphHistoricalReference(stored.RecordId, stored.Judgment.RevisionId,
                    stored.ContentDigest, stored.Judgment.Body.GetType().Name, subject.Role, subject.Class, subject.Id));
            }
        }
        return Array.AsReadOnly(result.OrderBy(item => item.RecordId, StringComparer.Ordinal)
            .ThenBy(item => item.Role, StringComparer.Ordinal).ToArray());
    }

    private static IEnumerable<HistoricalObject> HistoricalObjects(HumanJudgment judgment)
    {
        if (judgment.Body is DispositionJudgment disposition)
        {
            switch (disposition.Subject)
            {
                case ObjectJudgmentSubject value:
                    yield return new("subject", value.Object.Class, value.Object.Id);
                    break;
                case EdgeJudgmentSubject edge:
                    yield return new("edge-owner", edge.Owner.Class, edge.Owner.Id);
                    yield return new("edge-from", edge.From.Class, edge.From.Id);
                    yield return new("edge-to", edge.To.Class, edge.To.Id);
                    break;
                case GroupJudgmentSubject group:
                    foreach (var member in group.Members)
                        yield return new("group-member", member.Class, member.Id);
                    break;
            }
            yield break;
        }

        if (judgment.Body is not ReviewedNegativeJudgment negative) yield break;
        if (negative.WordformId is { } wordform)
            yield return new("negative-wordform", "WfiWordform", wordform);
        if (negative.AnalysisId is { } analysis)
            yield return new("negative-analysis", "WfiAnalysis", analysis);
        if (negative.Target is ReadingNegativeTarget reading)
            for (var ordinal = 0; ordinal < reading.Morphs.Count; ordinal++)
                if (reading.Morphs[ordinal].Identity.Form is { } form)
                    yield return new($"negative-morph-form:{ordinal}", "MoForm", form);
    }

    private static string Opinion(IWfiAnalysis analysis, LcmCache cache) =>
        analysis.GetAgentOpinion(cache.LangProject.DefaultUserAgent) switch
        {
            Opinions.approves => "approved",
            Opinions.disapproves => "disapproved",
            _ => "unknown"
        };

    private static string EvaluationOpinion(ICmAgentEvaluation evaluation) => evaluation.Owner switch
    {
        ICmAgent agent when agent.ApprovesOA?.Guid == evaluation.Guid => "approved",
        ICmAgent agent when agent.DisapprovesOA?.Guid == evaluation.Guid => "disapproved",
        _ => "unknown"
    };

    private static AllomorphReferenceFootprintCounts Count(
        IReadOnlyList<AllomorphReference> references, IReadOnlyList<AllomorphOwnedDependent> owned,
        IReadOnlyList<AllomorphAffectedWordform> affected)
    {
        var bundles = references.Where(item => item.Kind == "bundle-morph").ToArray();
        var adhoc = references.Where(item => item.Kind.StartsWith("adhoc-", StringComparison.Ordinal)).ToArray();
        var byKind = references.GroupBy(item => item.Kind, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        return new AllomorphReferenceFootprintCounts(
            bundles.Count(item => item.Opinion == "approved"),
            bundles.Count(item => item.Opinion == "disapproved"),
            bundles.Count(item => item.Opinion == "unknown"),
            bundles.Where(item => item.Opinion == "approved").Select(item => item.Analysis).OfType<Guid>().Distinct().Count(),
            bundles.Where(item => item.Opinion == "disapproved").Select(item => item.Analysis).OfType<Guid>().Distinct().Count(),
            bundles.Where(item => item.Opinion == "unknown").Select(item => item.Analysis).OfType<Guid>().Distinct().Count(),
            bundles.Select(item => item.SourceObject).Distinct().Count(),
            bundles.Select(item => item.Wordform).OfType<Guid>().Distinct().Count(),
            affected.Sum(item => item.Analyses.Count(analysis => analysis.Opinion == "approved")),
            adhoc.Select(item => item.SourceObject).Distinct().Count(),
            adhoc.Length,
            references.Count(item => item.IsCustom),
            references.Count(item => item.Kind == "other-native"),
            references.Count(item => item.Kind == "entry-alternate-form"),
            owned.Count,
            new ReadOnlyDictionary<string, int>(byKind));
    }

    private static ReferenceField ToSurface(RuntimeField field, string kind, string cardinality) =>
        new(field.Id, field.DeclaringClass, field.Name, kind, cardinality, field.DestinationClass, field.IsCustom);

    private static bool TryShape(CellarPropertyType type, out string kind, out string cardinality)
    {
        var shape = Shape(type);
        kind = shape.Kind;
        cardinality = shape.Cardinality;
        return kind.Length > 0;
    }

    private static (string Kind, string Cardinality) Shape(CellarPropertyType type)
    {
        if (type == CellarPropertyType.OwningAtomic || type == CellarPropertyType.OwningAtom)
            return ("owning", "atomic");
        if (type == CellarPropertyType.OwningCollection) return ("owning", "col");
        if (type == CellarPropertyType.OwningSequence) return ("owning", "seq");
        if (type == CellarPropertyType.ReferenceAtomic || type == CellarPropertyType.ReferenceAtom)
            return ("rel", "atomic");
        if (type == CellarPropertyType.ReferenceCollection) return ("rel", "col");
        if (type == CellarPropertyType.ReferenceSequence) return ("rel", "seq");
        return (string.Empty, string.Empty);
    }

    private static string Key(string declaringClass, string fieldName) => declaringClass + "." + fieldName;

    private static string Digest(byte[] content) => "sha256:" + Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static MasterModelSurface ReadMasterModel()
    {
        using var stream = typeof(AllomorphReferenceFootprintReader).Assembly.GetManifestResourceStream(MasterModelResource)
            ?? throw new InvalidOperationException("The pinned LibLCM model resource is missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        buffer.Position = 0;
        var document = XDocument.Load(buffer);
        var classes = document.Descendants("class").Select(element => new ModelClass(
            RequiredAttribute(element, "id"), (string?)element.Attribute("base")))
            .ToDictionary(item => item.Name, StringComparer.Ordinal);
        var fields = document.Descendants("class").SelectMany(element =>
        {
            var owner = RequiredAttribute(element, "id");
            var props = element.Element("props");
            return props is null ? [] : props.Elements().Where(field => field.Name == "owning" || field.Name == "rel")
                .Select(field => new ModelField(owner, RequiredAttribute(field, "id"), field.Name.LocalName,
                    RequiredAttribute(field, "sig"), RequiredAttribute(field, "card")));
        }).ToArray();
        return new MasterModelSurface(bytes, classes, fields);
    }

    private static string RequiredAttribute(XElement element, string name) =>
        (string?)element.Attribute(name) ?? throw new InvalidOperationException(
            $"The pinned LibLCM model has a {element.Name} element without '{name}'.");

    private sealed record ModelClass(string Name, string? BaseClass);
    private sealed record ModelField(string DeclaringClass, string Name, string Kind, string DestinationClass, string Cardinality);
    private sealed record RuntimeField(int Id, string DeclaringClass, string Name, CellarPropertyType Type,
        string DestinationClass, bool IsCustom, bool IsVirtual);
    private sealed record ReferenceField(int Id, string DeclaringClass, string Name, string Kind,
        string Cardinality, string DestinationClass, bool IsCustom);
    private sealed record ReferenceFieldSurface(IReadOnlyList<ReferenceField> Incoming,
        IReadOnlyList<ReferenceField> Owning, IReadOnlyList<RuntimeField> All,
        IReadOnlyList<AllomorphReferenceRouteNote> RouteNotes);
    private sealed record ObjectLink(int Hvo, int? Ordinal);
    private sealed record HistoricalObject(string Role, string Class, string Id);

    private sealed record MasterModelSurface(byte[] Bytes, IReadOnlyDictionary<string, ModelClass> Classes,
        IReadOnlyList<ModelField> Fields)
    {
        public bool IsSameOrSubclass(string className, string expectedBase)
        {
            string? current = className;
            while (current is not null)
            {
                if (string.Equals(current, expectedBase, StringComparison.Ordinal)) return true;
                if (!Classes.TryGetValue(current, out var declaration)) return false;
                current = declaration.BaseClass;
            }
            return false;
        }

        public bool IsClassRelatedToMoForm(string className) =>
            IsSameOrSubclass(className, "MoForm") || IsSameOrSubclass("MoForm", className);
    }
}

/// <summary>One LibLCM reference field that the census checked for incoming references to MoForm.</summary>
public sealed record AllomorphReferenceField(string DeclaringClass, string Name, string Kind,
    string Cardinality, string DestinationClass, bool IsCustom);

/// <summary>A live incoming reference classified by its authored replacement or deletion effect.</summary>
public sealed record AllomorphReferenceClassification(AllomorphReference Reference, string Disposition,
    string Reason);

/// <summary>A reason for including or interpreting a LibLCM field in the incoming-reference census.</summary>
public sealed record AllomorphReferenceRouteNote(string DeclaringClass, string Field, string Disposition,
    string Reason);

/// <summary>One exact incoming reference occurrence, including its LibLCM field and semantic role.</summary>
public sealed record AllomorphReference(Guid TargetForm, Guid SourceObject, string SourceClass,
    string DeclaringClass, string Field, int? Ordinal, string Kind, string FieldKind, string Cardinality,
    bool IsCustom, Guid? Bundle, Guid? Analysis, Guid? Wordform, Guid? Msa, Guid? InflType,
    string? Opinion, Guid? Rule, bool? Grouped, bool? Enabled);

/// <summary>One object that LibLCM owns under a retired form and would remove with it.</summary>
public sealed record AllomorphOwnedDependent(Guid TargetForm, Guid ParentObject, Guid DependentObject,
    string DependentClass, string DeclaringClass, string Field, int Ordinal, int Depth, bool IsCustom);

/// <summary>All wordform surface cases and analyses associated with an incoming bundle reference.</summary>
public sealed record AllomorphAffectedWordform(Guid Wordform,
    IReadOnlyList<AllomorphWordformAlternative> Forms, IReadOnlyList<AllomorphAffectedAnalysis> Analyses,
    IReadOnlyList<AllomorphTextUse> TextUses);

/// <summary>A Text segment's ordered reference to a wordform or one of its readings.</summary>
public sealed record AllomorphTextUse(Guid Segment, Guid ReferencedObject, string Kind, int Ordinal);

/// <summary>One human evaluation on an affected analysis, with its evaluator identity.</summary>
public sealed record AllomorphHumanEvaluation(Guid Evaluation, Guid? Agent, string Opinion);

/// <summary>One full analysis on an affected wordform, including unchanged morphology and evaluation membership.</summary>
public sealed record AllomorphAffectedAnalysis(Guid Analysis, string Opinion,
    IReadOnlyList<AllomorphHumanEvaluation> Evaluations, IReadOnlyList<AllomorphAffectedMorph> Morphs);

/// <summary>One ordered bundle in an affected analysis, with its original text alternatives.</summary>
public sealed record AllomorphAffectedMorph(Guid Bundle, Guid? Form, Guid? Msa, Guid? InflType,
    IReadOnlyList<AllomorphWordformAlternative> Text);

/// <summary>One text value in a writing system.</summary>
public sealed record AllomorphWordformAlternative(string WritingSystem, string Text);

/// <summary>A durable Notebook identity mention retained as history rather than a LibLCM reference.</summary>
public sealed record AllomorphHistoricalReference(string RecordId, string RevisionId, string ContentDigest,
    string JudgmentKind, string Role, string Class, string Id);

/// <summary>The complete reference and affected-reading evidence for one requested form set.</summary>
public sealed record AllomorphReferenceFootprint(string MasterModelSha256, string CacheMetadataSha256,
    IReadOnlyList<AllomorphReferenceField> ReferenceFields, IReadOnlyList<AllomorphReference> References,
    IReadOnlyList<AllomorphOwnedDependent> OwnedDependents, IReadOnlyList<AllomorphAffectedWordform> AffectedWordforms,
    IReadOnlyList<AllomorphHistoricalReference> HistoricalReferences, IReadOnlyList<string> Unavailable,
    AllomorphReferenceFootprintCounts Counts, string Digest)
{
    /// <summary>Reasons for interpreting inverse or derived model surfaces during the reference census.</summary>
    public IReadOnlyList<AllomorphReferenceRouteNote> RouteNotes { get; init; } = [];
}

/// <summary>Exact distinct reference and affected-reading counts derived from the footprint rows.</summary>
public sealed record AllomorphReferenceFootprintCounts(int ApprovedBundles, int DisapprovedBundles, int UnknownBundles,
    int ApprovedAnalyses, int DisapprovedAnalyses, int UnknownAnalyses, int Bundles, int AffectedWordforms,
    int ApprovedReadings, int AdhocRules, int AdhocOccurrences, int CustomReferences, int OtherNativeReferences,
    int AlternateFormOwnershipReferences, int OwnedDependents, IReadOnlyDictionary<string, int> ByReferenceKind);

/// <summary>Unresolved destinations and exact counts that explain why a retirement cannot proceed.</summary>
public sealed record AllomorphReferenceDestinationDiagnostics(IReadOnlyList<AllomorphReference> UnresolvedReferences,
    int UnresolvedApprovedAnalyses, int UnresolvedAdhocRules, int UnsupportedCustomReferences,
    int UnsupportedOtherReferences, string Message)
{
    /// <summary>Unavailable census routes prevent the caller from treating the listed rows as complete.</summary>
    public IReadOnlyList<string> Unavailable { get; init; } = [];

    /// <summary>One disposition and reason for every live incoming reference in the census.</summary>
    public IReadOnlyList<AllomorphReferenceClassification> Classifications { get; init; } = [];

    /// <summary>True only when every route was available and every reference has a declared destination.</summary>
    public bool IsComplete => Unavailable.Count == 0 && UnresolvedReferences.Count == 0;
}
