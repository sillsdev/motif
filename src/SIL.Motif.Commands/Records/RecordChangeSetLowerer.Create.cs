using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Composers;
using SIL.TextEditing.Markdown.Records;
using SIL.TextEditing.Project;

namespace SIL.Motif.Commands.Records;

public sealed partial class RecordChangeSetLowerer
{
    private const string EntryKind = "lexicon/entry";
    private const string MainEntryPath = "InsertEntryDlg.CreateNewEntryInternal, ILexEntryFactory.Create";
    private const string VariantPath = "LinkVariantToEntryOrSense, ILexEntry.CreateVariantEntryAndBackRef";

    /// <summary>Lowers a new entry as InsertEntryDlg and CreateVariantEntryAndBackRef do.</summary>
    private RecordLowering LowerCreatedEntry(LcmCache cache, ProjectRecordPlan plan)
    {
        var run = new Run(schema, catalog, cache, plan.RecordGuid!.Value.ToString("D"),
            new ProjectRecordExporter(cache, schema).CreateReadOptions().Markdown) { Changes = plan.ChangeSet.Changes };
        return run.LowerEntry(plan);
    }

    private sealed partial class Run
    {
        private RecordLowering Whole(ProjectRecordPlan plan, string part, string path, string? detail = null)
        {
            var set = plan.ChangeSet;
            var why = set.Refusals.Where(item => !item.WholeFile).Select(item => item.Kind + " on " + item.Field + " (" + item.Reason + ")").Distinct().ToList();
            var reason = $"A new {plan.ChangeSet.Changes.FirstOrDefault()?.ObjectType ?? "LexEntry"} needs {part} as FieldWorks creates it ({path}). Add {part} or refer to an existing one." +
                (detail is null ? "" : " " + detail) +
                (why.Count == 0 ? "" : " The import also declined: " + string.Join(", ", why.Take(3)) + ".");
            return new([], [], set.Refusals, [], null,
                new RecordLoweringRefusal(RefusalCodes.RecordImportCreateShape, plan.ChangeSet.Changes.FirstOrDefault()?.ObjectType ?? "LexEntry",
                    "", plan.RecordGuid, reason));
        }

        private static string? Text(RecordValue? value) => value is RecordScalar { IsEmpty: false } scalar ? scalar.Text : null;

        private static Guid? Target(RecordObject owner, string key) => (owner[key] as RecordScalar)?.Target;

        private static List<Guid> Targets(RecordObject owner, string key) => owner[key] is RecordList list
            ? list.Items.OfType<RecordScalar>().Select(item => item.Target).OfType<Guid>().ToList() : [];

        private static List<RecordObject> Objects(RecordObject owner, string key) =>
            owner[key] is RecordList list ? list.Items.OfType<RecordObject>().ToList() : owner[key] is RecordObject one ? [one] : [];

        /// <summary>Lowers the plan of a new lexicon entry.</summary>
        internal RecordLowering LowerEntry(ProjectRecordPlan plan)
        {
            var record = plan.RecordGuid!.Value;
            int Find(string field, Func<RecordObject, bool>? where = null)
            {
                for (var index = 0; index < Changes.Count; index++)
                    if (Changes[index] is { Kind: RecordChangeKind.AddObject, Subtree: { } subtree } change && change.ObjectGuid == record &&
                        change.Field == field && (where?.Invoke(subtree) ?? true))
                        return index;
                return -1;
            }
            List<int> All(string field) => Enumerable.Range(0, Changes.Count).Where(index => Changes[index] is { Kind: RecordChangeKind.AddObject } change &&
                change.ObjectGuid == record && change.Field == field && change.Subtree is not null).ToList();

            var formIndex = Find("lexeme-form");
            var senseIndexes = All("senses");
            var msaIndexes = All("morpho-syntax-analyses");
            var refIndexes = All("entry-refs");
            var variantRefs = refIndexes.Where(index => RefTypeOf(Changes[index].Subtree!) == LexEntryRefTags.krtVariant).ToList();
            var complexRefs = refIndexes.Where(index => RefTypeOf(Changes[index].Subtree!) == LexEntryRefTags.krtComplexForm).ToList();

            if (formIndex < 0 || Target(Changes[formIndex].Subtree!, "morph-type") is null)
                return Whole(plan, "a lexeme form with a morph type", MainEntryPath);
            if (refIndexes.Count > variantRefs.Count)
                return complexRefs.Count > 0
                    ? new([], [], plan.ChangeSet.Refusals, [], null, new RecordLoweringRefusal(RefusalCodes.RecordImportCreateKindNotCreatable,
                        "LexEntry", "entry-refs", record, "A complex form cannot be created from a record file yet: no operation creates a complex-form link."))
                    : Whole(plan, "a variant or complex-form link of a known type", VariantPath);
            var variant = variantRefs.Count > 0;
            if (variant && (senseIndexes.Count > 0 || msaIndexes.Count > 0))
                return Whole(plan, "no sense or grammatical info (a variant has none)", VariantPath,
                    "Give the main entry the sense, and keep this entry to a lexeme form and its variant link.");
            if (variant && variantRefs.Count != 1)
                return Whole(plan, "exactly one variant link", VariantPath);
            if (!variant && senseIndexes.Count == 0)
                return Whole(plan, "a sense with a gloss and grammatical info, or a variant link to its main entry or sense", MainEntryPath);

            var form = Changes[formIndex].Subtree!;
            var vernacular = cache.ServiceLocator.WritingSystems.VernacularWritingSystems.Select(item => item.Id).ToList();
            var alternatives = form["form"] as RecordAlternatives;
            var chosen = alternatives?.Items.FirstOrDefault(item => !item.Value.IsEmpty && vernacular.Contains(item.Key));
            var formWs = chosen?.Key is { } given ? given : cache.ServiceLocator.WritingSystems.DefaultVernacularWritingSystem.Id;
            var formText = chosen?.Value.Text ?? "";
            var morph = Target(form, "morph-type")!.Value;
            string formClass;
            try
            {
                formClass = AuthorLexicalEntryComposer.FormClassName(cache, CanonicalId.FromGuid(morph));
            }
            catch (InvalidOperationException exception)
            {
                return Whole(plan, "a lexeme form with a morph type FieldWorks can make a form for", MainEntryPath, exception.Message);
            }
            if (form.Class is { } declared && declared != formClass)
                return Whole(plan, "a lexeme form whose class matches its morph type", MainEntryPath,
                    $"The form is a {declared}, and the morph type gives a {formClass}.");
            var environments = Targets(form, "phone-env").Select(item => CanonicalId.FromGuid(item)).ToList();
            var inflectionClasses = Targets(form, "inflection-classes").Select(item => CanonicalId.FromGuid(item)).ToList();
            var created = new List<OperationEnvelope>();
            var counter = 0;
            CanonicalId Mint() => Id("compose:" + counter++);
            var formId = form.Guid is { } formGuid ? CanonicalId.FromGuid(formGuid) : Id("create:form");
            var entryId = CanonicalId.FromGuid(record);
            var required = new HashSet<int> { formIndex };
            var consumed = new Dictionary<int, (CanonicalId Entity, Func<string, string?, bool> Written)>
            {
                [formIndex] = (formId, (key, item) => key is "morph-type" or "phone-env" or "inflection-classes" || key == "form" && item == formWs),
            };
            var skeletonClasses = new Dictionary<Guid, string> { [record] = Changes[formIndex].ObjectType ?? "LexEntry" };

            try
            {
                if (variant)
                {
                    var refIndex = variantRefs[0];
                    var reference = Changes[refIndex].Subtree!;
                    var types = Targets(reference, "variant-entry-types");
                    var components = Targets(reference, "component-lexemes");
                    if (types.Count == 0)
                        return Whole(plan, "a variant type", VariantPath);
                    if (components.Count != 1)
                        return Whole(plan, "one component entry or sense for the variant", VariantPath);
                    if (components[0] == record || All("senses").Any(index => Changes[index].Subtree!.Guid == components[0]))
                        return Whole(plan, "a component that is not the entry itself or one of its own senses", VariantPath);
                    var refId = reference.Guid is { } refGuid ? CanonicalId.FromGuid(refGuid) : Id("create:ref");
                    var hide = (reference["hide-minor-entry"] as RecordScalar)?.AsBoolean() ?? false;
                    var intent = new AuthorVariantEntryIntent(CanonicalId.FromGuid(components[0]), CanonicalId.FromGuid(types[0]), formText, formWs,
                        CanonicalId.FromGuid(morph), environments.Count == 0 ? null : environments, hide);
                    created.AddRange(AuthorVariantEntryComposer.Build(cache, intent, Mint, new NewEntryIds(entryId, formId, VariantRef: refId)));
                    required.Add(refIndex);
                    var first = types[0];
                    consumed[refIndex] = (refId, (key, item) => key is "ref-type" or "hide-minor-entry" or "component-lexemes" ||
                        key == "variant-entry-types" && item == first.ToString("N"));
                    skeletonClasses[refId.ToGuid()] = "LexEntryRef";
                }
                else
                {
                    var senseIndex = senseIndexes[0];
                    var sense = Changes[senseIndex].Subtree!;
                    var analysis = cache.ServiceLocator.WritingSystems.AnalysisWritingSystems.Select(item => item.Id).ToList();
                    var gloss = (sense["gloss"] as RecordAlternatives)?.Items.FirstOrDefault(item => !item.Value.IsEmpty && analysis.Contains(item.Key));
                    if (gloss is null || gloss.Value.Value.IsEmpty)
                        return Whole(plan, "a sense with a gloss", MainEntryPath);
                    var msaTarget = Target(sense, "morpho-syntax-analysis");
                    var msaIndex = msaTarget is null ? -1 : msaIndexes.FirstOrDefault(index => Changes[index].Subtree!.Guid == msaTarget, -1);
                    if (msaIndex < 0)
                        return Whole(plan, "a sense with grammatical info that is one of the entry's morpho-syntax-analyses", MainEntryPath);
                    var msa = Changes[msaIndex].Subtree!;
                    var (msaIntent, writtenByMsa) = MsaOf(msa, out var why);
                    if (msaIntent is null)
                        return Whole(plan, "grammatical info with a category", MainEntryPath, why);
                    var ids = new NewEntryIds(entryId, formId, CanonicalId.FromGuid(msa.Guid!.Value),
                        sense.Guid is { } senseGuid ? CanonicalId.FromGuid(senseGuid) : Id("create:sense"));
                    var intent = new AuthorLexicalEntryIntent(formText, formWs, CanonicalId.FromGuid(morph), gloss.Value.Value.Text, gloss.Value.Key,
                        msaIntent, null, inflectionClasses.Count == 0 ? null : inflectionClasses, environments.Count == 0 ? null : environments);
                    created.AddRange(AuthorLexicalEntryComposer.Build(cache, intent, ids, Mint));
                    var glossWs = gloss.Value.Key;
                    required.UnionWith([senseIndex, msaIndex]);
                    consumed[senseIndex] = (ids.Sense!.Value, (key, item) => key == "morpho-syntax-analysis" || key == "gloss" && item == glossWs);
                    consumed[msaIndex] = (ids.Msa!.Value, writtenByMsa!);
                    skeletonClasses[ids.Sense!.Value.ToGuid()] = "LexSense";
                    skeletonClasses[ids.Msa!.Value.ToGuid()] = msa.Class ?? "";
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or ContractParseException or ArgumentException or KeyNotFoundException)
            {
                return Whole(plan, "a record FieldWorks can make", variant ? VariantPath : MainEntryPath, exception.Message);
            }

            skeletonClasses[formId.ToGuid()] = formClass;
            foreach (var (guid, name) in skeletonClasses.Where(pair => pair.Value.Length > 0))
            {
                _created.Add(guid);
                _classes[guid] = name;
            }
            _created.Add(record);
            foreach (var operation in created)
            {
                Operations.Add(operation);
                OperationChanges.Add(formIndex);
            }
            for (var index = 0; index < Changes.Count; index++)
            {
                if (consumed.TryGetValue(index, out var skeleton))
                    LowerSkeletonContent(index, Changes[index], skeleton.Entity, skeleton.Written, index == formIndex ? formClass : null);
                else
                    Lower(index, Changes[index]);
            }
            Depend();
            return new(Operations, Refusals, plan.ChangeSet.Refusals, OperationChanges, RefusedCodes, null, required);
        }

        private static int? RefTypeOf(RecordObject reference) =>
            (reference["ref-type"] as RecordScalar)?.AsInteger() is { } value ? (int)value : null;

        /// <summary>The sense's grammatical info as the composer takes it.</summary>
        private (LexicalEntryMsa? Msa, Func<string, string?, bool>? Written) MsaOf(RecordObject msa, out string? why)
        {
            why = null;
            LexicalEntryMsaKind kind;
            switch (msa.Class)
            {
                case "MoStemMsa": kind = LexicalEntryMsaKind.Stem; break;
                case "MoInflAffMsa": kind = LexicalEntryMsaKind.Inflectional; break;
                case "MoDerivAffMsa": kind = LexicalEntryMsaKind.Derivational; break;
                case "MoUnclassifiedAffixMsa": kind = LexicalEntryMsaKind.Unclassified; break;
                default:
                    why = $"A {msa.Class ?? "grammatical info of no class"} is not one the composer makes.";
                    return (null, null);
            }
            var categoryKey = kind == LexicalEntryMsaKind.Derivational ? "from-part-of-speech" : "part-of-speech";
            if (Target(msa, categoryKey) is not { } category)
                return (null, null);
            if (kind == LexicalEntryMsaKind.Derivational && Target(msa, "to-part-of-speech") is not { })
            {
                why = "A derivational affix needs the category it makes.";
                return (null, null);
            }
            var slots = kind == LexicalEntryMsaKind.Inflectional ? Targets(msa, "slots").Select(item => CanonicalId.FromGuid(item)).ToList() : null;
            var inflectionClass = kind == LexicalEntryMsaKind.Stem && Target(msa, "inflection-class") is { } held ? CanonicalId.FromGuid(held) : (CanonicalId?)null;
            var features = kind == LexicalEntryMsaKind.Stem ? Targets(msa, "prod-restrict").Select(item => CanonicalId.FromGuid(item)).ToList() : null;
            var intent = new LexicalEntryMsa(kind, CanonicalId.FromGuid(category),
                kind == LexicalEntryMsaKind.Derivational ? CanonicalId.FromGuid(Target(msa, "to-part-of-speech")!.Value) : null,
                slots is { Count: > 0 } ? slots : null, inflectionClass, features is { Count: > 0 } ? features : null);
            Func<string, string?, bool> written = (key, _) => kind switch
            {
                LexicalEntryMsaKind.Stem => key is "part-of-speech" or "inflection-class" or "prod-restrict",
                LexicalEntryMsaKind.Inflectional => key is "part-of-speech" or "slots",
                LexicalEntryMsaKind.Derivational => key is "from-part-of-speech" or "to-part-of-speech",
                _ => key == "part-of-speech",
            };
            return (intent, written);
        }

        /// <summary>Lowers the rest of a composer-made object, declining a field alone.</summary>
        private void LowerSkeletonContent(int index, RecordChange change, CanonicalId entity, Func<string, string?, bool> written, string? knownClass)
        {
            var subtree = change.Subtree!;
            var type = knownClass ?? subtree.Class ?? ChildOf(change.ObjectType!, change.Field)!;
            foreach (var (name, value) in subtree.Fields)
            {
                _pending = [];
                var role = $"{index}:{change.Kind}:{name}";
                try
                {
                    LowerContent(change, type, name, value, entity, role, written);
                    Operations.AddRange(_pending);
                    OperationChanges.AddRange(_pending.Select(_ => index));
                }
                catch (Stop stop)
                {
                    Refusals.Add(stop.Refusal);
                }
            }
        }

        /// <summary>Makes each operation wait for the creates of objects it writes or names.</summary>
        private void Depend()
        {
            var creators = new Dictionary<CanonicalId, CanonicalId>();
            foreach (var operation in Operations.Where(item => item.EntityId is not null))
                creators[operation.EntityId!.Value] = operation.OperationId;
            for (var index = 0; index < Operations.Count; index++)
            {
                var operation = Operations[index];
                var needed = new List<CanonicalId>();
                if (operation.Target is { } target) needed.Add(target);
                if (operation.After is { ValueKind: JsonValueKind.Object } after)
                    foreach (var name in new[] { "ref", "member" })
                        if (after.TryGetProperty(name, out var named) && named.ValueKind == JsonValueKind.String &&
                            CanonicalId.TryParse(named.GetString(), out var id))
                            needed.Add(id);
                var have = operation.DependsOn.Select(item => item.OperationId).ToHashSet();
                var added = needed.Where(item => creators.TryGetValue(item, out var creator) && creator != operation.OperationId && have.Add(creator))
                    .Select(item => new OperationDependency(creators[item])).ToList();
                if (added.Count == 0) continue;
                Operations[index] = new OperationEnvelope(operation.OperationId, operation.Kind, operation.EntityId, operation.Target,
                    operation.After, operation.Placement, operation.DependsOn.Concat(added).ToArray(), rationale: operation.Rationale);
            }
        }
    }
}
