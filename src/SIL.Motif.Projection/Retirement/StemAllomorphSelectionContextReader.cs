using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Runner.Retirement;

namespace SIL.Motif.Projection.Retirement;

/// <summary>Reads the selection and expansion evidence surrounding ordinary stem allomorphs.</summary>
public static class StemAllomorphSelectionContextReader
{
    /// <summary>Captures stem identity, selection gates, expansion links, and incoming reference evidence.</summary>
    public static StemAllomorphSelectionContext Read(LcmCache cache, IEnumerable<Guid> stemForms)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(stemForms);

        var ids = stemForms.Distinct().Order().ToArray();
        if (ids.Length == 0) throw new ArgumentException("At least one stem form identity is required.", nameof(stemForms));

        var evidence = new List<string>();
        var unavailable = new SortedSet<string>(StringComparer.Ordinal);
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        var forms = new List<IMoStemAllomorph>();
        foreach (var id in ids)
        {
            if (!repository.TryGetObject(id, out var value) || value is not IMoStemAllomorph form)
            {
                unavailable.Add($"Form '{id:D}' is absent or is not a MoStemAllomorph; stem selection context is unavailable.");
                Add(evidence, "MoStemAllomorph", id, "identity", value?.ClassName ?? "missing");
                continue;
            }
            forms.Add(form);
            AddForm(cache, form, evidence);
        }

        foreach (var entry in forms.Select(form => form.Owner as ILexEntry).OfType<ILexEntry>()
                     .DistinctBy(entry => entry.Guid).OrderBy(entry => entry.Guid))
            AddEntryContext(cache, entry, evidence, unavailable);

        foreach (var environment in forms.SelectMany(form => form.PhoneEnvRC)
                     .DistinctBy(item => item.Guid).OrderBy(item => item.Guid))
            AddEnvironment(cache, environment, evidence, unavailable);

        var stemNames = forms.Select(form => form.StemNameRA).OfType<IMoStemName>()
            .DistinctBy(item => item.Guid).OrderBy(item => item.Guid).ToArray();
        foreach (var name in stemNames) AddStemNameContext(cache, name, evidence, unavailable);

        var nameIds = stemNames.Select(item => item.Guid).ToHashSet();
        foreach (var msa in repository.AllInstances().OfType<IMoDerivAffMsa>()
                     .Where(item => item.FromStemNameRA is { } name && nameIds.Contains(name.Guid))
                     .OrderBy(item => item.Guid))
            AddDerivationalMsa(cache, msa, evidence, unavailable);

        foreach (var form in forms)
        {
            foreach (var derivation in repository.AllInstances().OfType<IMoDeriv>()
                         .Where(item => item.StemFormRA?.Guid == form.Guid).OrderBy(item => item.Guid))
                AddDerivation(cache, derivation, evidence, unavailable);
            foreach (var compound in repository.AllInstances().OfType<IMoCompoundRuleApp>()
                         .Where(item => item.LeftFormRA?.Guid == form.Guid || item.RightFormRA?.Guid == form.Guid)
                         .OrderBy(item => item.Guid))
                AddCompound(cache, compound, evidence);
        }

        if (forms.Count > 0)
        {
            var footprint = AllomorphReferenceFootprintReader.Read(cache, forms.Select(item => item.Guid));
            unavailable.UnionWith(footprint.Unavailable);
            foreach (var route in footprint.RouteNotes)
                unavailable.Add($"Stem context has no independent route model for unmodeled reference field " +
                    $"{route.DeclaringClass}.{route.Field}: {route.Reason}");
            AddFootprint(footprint, evidence);
        }
        var digest = StemRetirementContextDigestReader.ComputeDigest(evidence, unavailable);
        return new StemAllomorphSelectionContext(Array.AsReadOnly(ids), Array.AsReadOnly(evidence
                .Order(StringComparer.Ordinal).ToArray()), Array.AsReadOnly(unavailable.ToArray()), digest);
    }

    private static void AddEntryContext(LcmCache cache, ILexEntry entry, ICollection<string> evidence,
        ISet<string> unavailable)
    {
        Add(evidence, entry.ClassName, entry.Guid, "identity", entry.Owner?.Guid.ToString("D") ?? "null");
        Add(evidence, entry.ClassName, entry.Guid, "lexeme-form", entry.LexemeFormOA?.Guid.ToString("D") ?? "null");
        AddForms(cache, "alternate-form", entry.AlternateFormsOS, evidence);

        foreach (var msa in entry.MorphoSyntaxAnalysesOC.OrderBy(item => item.Guid))
        {
            if (msa is IMoStemMsa stemMsa)
                AddStemMsa(cache, stemMsa, evidence, unavailable);
            else
                Add(evidence, msa.ClassName, msa.Guid, "owner", entry.Guid.ToString("D"));
        }

        foreach (var sense in entry.SensesOS.OrderBy(item => item.Guid))
            Add(evidence, sense.ClassName, sense.Guid, "morphosyntax-analysis",
                sense.MorphoSyntaxAnalysisRA?.Guid.ToString("D") ?? "null");

        foreach (var reference in entry.EntryRefsOS.OrderBy(item => item.Guid))
            AddEntryReference(cache, reference, evidence);

        var relatedIdentities = entry.SensesOS.Select(item => item.Guid).Append(entry.Guid).ToHashSet();
        foreach (var reference in cache.ServiceLocator.GetInstance<ILexEntryRefRepository>().AllInstances()
                     .Where(item => item.ComponentLexemesRS.Concat(item.PrimaryLexemesRS)
                         .Any(component => relatedIdentities.Contains(component.Guid)))
                     .OrderBy(item => item.Guid))
            AddEntryReference(cache, reference, evidence);
    }

    private static void AddForms(LcmCache cache, string field, IEnumerable<IMoForm> forms, ICollection<string> evidence)
    {
        var ordinal = 0;
        foreach (var form in forms)
        {
            Add(evidence, form.Owner?.ClassName ?? "MoForm", form.Owner?.Guid ?? Guid.Empty,
                field + ":" + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture),
                form.Guid.ToString("D"));
            AddForm(cache, form, evidence);
            ordinal++;
        }
    }

    private static void AddForm(LcmCache cache, IMoForm form, ICollection<string> evidence)
    {
        Add(evidence, form.ClassName, form.Guid, "owner", form.Owner?.Guid.ToString("D") ?? "null");
        Add(evidence, form.ClassName, form.Guid, "morph-type", form.MorphTypeRA?.Guid.ToString("D") ?? "null");
        Add(evidence, form.ClassName, form.Guid, "abstract", form.IsAbstract ? "true" : "false");
        AddAlternatives(cache, form.Form, form.ClassName, form.Guid, "form", evidence);
        if (form is not IMoStemAllomorph stem) return;

        Add(evidence, form.ClassName, form.Guid, "stem-name", stem.StemNameRA?.Guid.ToString("D") ?? "null");
        AddSet("phone-environment", stem.PhoneEnvRC, form.ClassName, form.Guid, evidence);
    }

    private static void AddStemNameContext(LcmCache cache, IMoStemName name, ICollection<string> evidence,
        ISet<string> unavailable, bool includeOwnerContext = true, ISet<Guid>? visited = null)
    {
        visited ??= new HashSet<Guid>();
        if (!visited.Add(name.Guid))
        {
            unavailable.Add($"Stem-name defaults for '{name.Guid:D}' contain a cycle.");
            return;
        }
        Add(evidence, name.ClassName, name.Guid, "owner", name.Owner?.Guid.ToString("D") ?? "null");
        AddAlternatives(cache, name.Name, name.ClassName, name.Guid, "name", evidence);
        AddAlternatives(cache, name.Abbreviation, name.ClassName, name.Guid, "abbreviation", evidence);
        Add(evidence, name.ClassName, name.Guid, "default-stem", name.DefaultStemRA?.Guid.ToString("D") ?? "null");
        if (name.DefaultStemRA is { } defaultStem)
            AddStemNameContext(cache, defaultStem, evidence, unavailable, includeOwnerContext: false, visited: visited);
        Add(evidence, name.ClassName, name.Guid, "default-affix", name.DefaultAffixRA?.Guid.ToString("D") ?? "null");
        if (name.DefaultAffixRA is { } defaultAffix) AddInflAffixMsa(cache, defaultAffix, evidence, unavailable);
        var regionIndex = 0;
        foreach (var region in name.RegionsOC.OrderBy(item => item.Guid))
        {
            Add(evidence, name.ClassName, name.Guid, "region:" + regionIndex++, region.Guid.ToString("D"));
            AddFeatureStructure(cache, region, evidence, unavailable, new HashSet<Guid>());
        }

        if (includeOwnerContext && name.Owner is IPartOfSpeech owner)
        {
            var orderedNames = owner.StemNamesOC.Select(item => item.Guid.ToString("D")).ToArray();
            Add(evidence, owner.ClassName, owner.Guid, "stem-name-selection-order", string.Join(',', orderedNames));
            AddPartOfSpeechAncestry(cache, owner, evidence, unavailable);
        }
        else if (name.Owner is IMoInflClass ownerClass)
        {
            Add(evidence, ownerClass.ClassName, ownerClass.Guid, "stem-name-selection-order",
                string.Join(',', ownerClass.StemNamesOC.Select(item => item.Guid.ToString("D"))));
        }

        foreach (var sibling in cache.ServiceLocator.GetInstance<IMoStemAllomorphRepository>().AllInstances()
                     .Where(form => form.StemNameRA?.Guid == name.Guid).OrderBy(form => form.Owner?.Guid)
                     .ThenBy(form => form.Guid))
            AddForm(cache, sibling, evidence);
    }

    private static void AddStemMsa(LcmCache cache, IMoStemMsa msa, ICollection<string> evidence,
        ISet<string> unavailable)
    {
        Add(evidence, msa.ClassName, msa.Guid, "owner", msa.Owner?.Guid.ToString("D") ?? "null");
        Add(evidence, msa.ClassName, msa.Guid, "part-of-speech", msa.PartOfSpeechRA?.Guid.ToString("D") ?? "null");
        Add(evidence, msa.ClassName, msa.Guid, "inflection-class", msa.InflectionClassRA?.Guid.ToString("D") ?? "null");
        Add(evidence, msa.ClassName, msa.Guid, "stratum", msa.StratumRA?.Guid.ToString("D") ?? "null");
        if (msa.InflectionClassRA is { } inflectionClass)
            AddInflectionClass(cache, inflectionClass, evidence, unavailable, new HashSet<Guid>());
        if (msa.StratumRA is { } stratum) AddStratum(cache, stratum, evidence, unavailable);
        AddSet("productive-restriction", msa.ProdRestrictRC, msa.ClassName, msa.Guid, evidence);
        AddSet("from-part-of-speech", msa.FromPartsOfSpeechRC, msa.ClassName, msa.Guid, evidence);
        AddSet("slot", msa.SlotsRC, msa.ClassName, msa.Guid, evidence);
        AddFeatureStructure(cache, msa.MsFeaturesOA, evidence, unavailable, new HashSet<Guid>());
        if (msa.PartOfSpeechRA is { } pos) AddPartOfSpeechAncestry(cache, pos, evidence, unavailable);
    }

    private static void AddInflAffixMsa(LcmCache cache, IMoInflAffMsa msa, ICollection<string> evidence,
        ISet<string> unavailable)
    {
        Add(evidence, msa.ClassName, msa.Guid, "owner", msa.Owner?.Guid.ToString("D") ?? "null");
        Add(evidence, msa.ClassName, msa.Guid, "affix-category", msa.AffixCategoryRA?.Guid.ToString("D") ?? "null");
        Add(evidence, msa.ClassName, msa.Guid, "part-of-speech", msa.PartOfSpeechRA?.Guid.ToString("D") ?? "null");
        AddSet("slot", msa.SlotsRC, msa.ClassName, msa.Guid, evidence);
        AddSet("from-productivity-restriction", msa.FromProdRestrictRC, msa.ClassName, msa.Guid, evidence);
        AddFeatureStructure(cache, msa.InflFeatsOA, evidence, unavailable, new HashSet<Guid>());
        if (msa.PartOfSpeechRA is { } pos) AddPartOfSpeechAncestry(cache, pos, evidence, unavailable);
    }

    private static void AddPartOfSpeechAncestry(LcmCache cache, IPartOfSpeech pos,
        ICollection<string> evidence, ISet<string> unavailable)
    {
        var visited = new HashSet<Guid>();
        for (IPartOfSpeech? current = pos; current is not null; current = current.Owner as IPartOfSpeech)
        {
            if (!visited.Add(current.Guid))
            {
                unavailable.Add($"PartOfSpeech ancestry for '{pos.Guid:D}' contains a cycle at '{current.Guid:D}'.");
                break;
            }
            Add(evidence, current.ClassName, current.Guid, "parent", current.Owner?.Guid.ToString("D") ?? "null");
            AddAlternatives(cache, current.Name, current.ClassName, current.Guid, "name", evidence);
            AddAlternatives(cache, current.Abbreviation, current.ClassName, current.Guid, "abbreviation", evidence);
            Add(evidence, current.ClassName, current.Guid, "requires-inflection", current.RequiresInflection ? "true" : "false");
            Add(evidence, current.ClassName, current.Guid, "default-inflection-class",
                current.DefaultInflectionClassRA?.Guid.ToString("D") ?? "null");
            if (current.DefaultInflectionClassRA is { } defaultClass)
                AddInflectionClass(cache, defaultClass, evidence, unavailable, new HashSet<Guid>());
            AddFeatureStructure(cache, current.DefaultFeaturesOA, evidence, unavailable, new HashSet<Guid>());
            AddFeatureStructure(cache, current.InherFeatValOA, evidence, unavailable, new HashSet<Guid>());
            Add(evidence, current.ClassName, current.Guid, "stem-name-order",
                string.Join(',', current.StemNamesOC.Select(item => item.Guid.ToString("D"))));
            foreach (var name in current.StemNamesOC)
                AddStemNameContext(cache, name, evidence, unavailable, includeOwnerContext: false);
            AddSet("inflection-class", current.InflectionClassesOC, current.ClassName, current.Guid, evidence);
            foreach (var inflectionClass in current.InflectionClassesOC)
                AddInflectionClass(cache, inflectionClass, evidence, unavailable, new HashSet<Guid>());
            AddSet("bearable-feature", current.BearableFeaturesRC, current.ClassName, current.Guid, evidence);
            foreach (var feature in current.BearableFeaturesRC)
                AddFeatureDefinition(cache, feature, evidence, unavailable, new HashSet<Guid>());
            AddSet("inflectable-feature", current.InflectableFeatsRC, current.ClassName, current.Guid, evidence);
            foreach (var feature in current.InflectableFeatsRC)
                AddFeatureDefinition(cache, feature, evidence, unavailable, new HashSet<Guid>());
        }
    }

    private static void AddInflectionClass(LcmCache cache, IMoInflClass inflectionClass,
        ICollection<string> evidence, ISet<string> unavailable, ISet<Guid> visited)
    {
        if (!visited.Add(inflectionClass.Guid))
        {
            unavailable.Add($"Inflection-class ancestry for '{inflectionClass.Guid:D}' contains a cycle.");
            return;
        }
        Add(evidence, inflectionClass.ClassName, inflectionClass.Guid, "owner",
            inflectionClass.Owner?.Guid.ToString("D") ?? "null");
        AddAlternatives(cache, inflectionClass.Name, inflectionClass.ClassName, inflectionClass.Guid, "name", evidence);
        AddAlternatives(cache, inflectionClass.Abbreviation, inflectionClass.ClassName, inflectionClass.Guid,
            "abbreviation", evidence);
        AddAlternatives(cache, inflectionClass.Description, inflectionClass.ClassName, inflectionClass.Guid,
            "description", evidence);
        Add(evidence, inflectionClass.ClassName, inflectionClass.Guid, "stem-name-order",
            string.Join(',', inflectionClass.StemNamesOC.Select(item => item.Guid.ToString("D"))));
        foreach (var name in inflectionClass.StemNamesOC) AddStemNameContext(cache, name, evidence, unavailable);
        AddSet("subclass", inflectionClass.SubclassesOC, inflectionClass.ClassName, inflectionClass.Guid, evidence);
        foreach (var referenceForm in inflectionClass.ReferenceFormsOC.OrderBy(item => item.Guid))
            AddFeatureStructure(cache, referenceForm, evidence, unavailable, new HashSet<Guid>());
        if (inflectionClass.Owner is IMoInflClass parentClass)
            AddInflectionClass(cache, parentClass, evidence, unavailable, visited);
        else if (inflectionClass.Owner is IPartOfSpeech ownerPos)
        {
            Add(evidence, ownerPos.ClassName, ownerPos.Guid, "owner", ownerPos.Owner?.Guid.ToString("D") ?? "null");
            Add(evidence, ownerPos.ClassName, ownerPos.Guid, "default-inflection-class",
                ownerPos.DefaultInflectionClassRA?.Guid.ToString("D") ?? "null");
            if (ownerPos.DefaultInflectionClassRA is { } defaultClass &&
                defaultClass.Guid != inflectionClass.Guid && !visited.Contains(defaultClass.Guid))
                AddInflectionClass(cache, defaultClass, evidence, unavailable, visited);
            Add(evidence, ownerPos.ClassName, ownerPos.Guid, "stem-name-order",
                string.Join(',', ownerPos.StemNamesOC.Select(item => item.Guid.ToString("D"))));
            foreach (var name in ownerPos.StemNamesOC)
                AddStemNameContext(cache, name, evidence, unavailable, includeOwnerContext: false);
            AddFeatureStructure(cache, ownerPos.DefaultFeaturesOA, evidence, unavailable, new HashSet<Guid>());
            AddFeatureStructure(cache, ownerPos.InherFeatValOA, evidence, unavailable, new HashSet<Guid>());
        }
    }

    private static void AddFeatureStructure(LcmCache cache, IFsFeatStruc? structure, ICollection<string> evidence,
        ISet<string> unavailable, ISet<Guid> visited)
    {
        if (structure is null) return;
        if (!visited.Add(structure.Guid)) return;
        Add(evidence, structure.ClassName, structure.Guid, "owner", structure.Owner?.Guid.ToString("D") ?? "null");
        Add(evidence, structure.ClassName, structure.Guid, "type", structure.TypeRA?.Guid.ToString("D") ?? "null");
        foreach (var specification in structure.FeatureSpecsOC.OrderBy(item => item.Guid))
            AddFeatureSpecification(cache, specification, evidence, unavailable, visited);
        foreach (var disjunction in structure.FeatureDisjunctionsOC.OrderBy(item => item.Guid))
        {
            Add(evidence, disjunction.ClassName, disjunction.Guid, "owner", structure.Guid.ToString("D"));
            foreach (var member in disjunction.ContentsOC.OrderBy(item => item.Guid))
                AddFeatureStructure(cache, member, evidence, unavailable, visited);
        }
    }

    private static void AddFeatureSpecification(LcmCache cache, IFsFeatureSpecification? specification,
        ICollection<string> evidence,
        ISet<string> unavailable, ISet<Guid> visited)
    {
        if (specification is null) return;
        if (!visited.Add(specification.Guid)) return;
        Add(evidence, specification.ClassName, specification.Guid, "feature",
            specification.FeatureRA?.Guid.ToString("D") ?? "null");
        if (specification.FeatureRA is { } feature)
            AddFeatureDefinition(cache, feature, evidence, unavailable, visited);
        switch (specification)
        {
            case IFsClosedValue closed:
                Add(evidence, closed.ClassName, closed.Guid, "value", closed.ValueRA?.Guid.ToString("D") ?? "null");
                if (closed.ValueRA is { } closedValue) Add(evidence, closedValue.ClassName, closedValue.Guid,
                    "owner", closedValue.Owner?.Guid.ToString("D") ?? "null");
                break;
            case IFsNegatedValue negated:
                Add(evidence, negated.ClassName, negated.Guid, "value", negated.ValueRA?.Guid.ToString("D") ?? "null");
                break;
            case IFsDisjunctiveValue disjunctive:
                AddSet("value", disjunctive.ValueRC, disjunctive.ClassName, disjunctive.Guid, evidence);
                break;
            case IFsComplexValue complex:
                AddFeatureStructure(cache, complex.ValueOA as IFsFeatStruc, evidence, unavailable, visited);
                break;
            case IFsSharedValue shared:
                Add(evidence, shared.ClassName, shared.Guid, "value", shared.ValueRA?.Guid.ToString("D") ?? "null");
                break;
            case IFsOpenValue:
                unavailable.Add($"Open feature value '{specification.Guid:D}' has no stem-gate interpretation.");
                break;
            default:
                unavailable.Add($"Feature specification '{specification.Guid:D}' has unsupported class '{specification.ClassName}'.");
                break;
        }
    }

    private static void AddFeatureDefinition(LcmCache cache, IFsFeatDefn feature, ICollection<string> evidence,
        ISet<string> unavailable, ISet<Guid> visited)
    {
        if (!visited.Add(feature.Guid)) return;
        Add(evidence, feature.ClassName, feature.Guid, "owner", feature.Owner?.Guid.ToString("D") ?? "null");
        AddAlternatives(cache, feature.Name, feature.ClassName, feature.Guid, "name", evidence);
        AddAlternatives(cache, feature.Abbreviation, feature.ClassName, feature.Guid, "abbreviation", evidence);
        AddAlternatives(cache, feature.Description, feature.ClassName, feature.Guid, "description", evidence);
        switch (feature)
        {
            case IFsClosedFeature closed:
                AddSet("value-definition", closed.ValuesOC, closed.ClassName, closed.Guid, evidence);
                foreach (var value in closed.ValuesOC.OrderBy(item => item.Guid))
                {
                    Add(evidence, value.ClassName, value.Guid, "owner", value.Owner?.Guid.ToString("D") ?? "null");
                    AddAlternatives(cache, value.Name, value.ClassName, value.Guid, "name", evidence);
                    AddAlternatives(cache, value.Abbreviation, value.ClassName, value.Guid, "abbreviation", evidence);
                }
                break;
            case IFsComplexFeature complex:
                Add(evidence, complex.ClassName, complex.Guid, "structure-type", complex.TypeRA?.Guid.ToString("D") ?? "null");
                break;
            case IFsOpenFeature open:
                Add(evidence, open.ClassName, open.Guid, "writing-system", open.WritingSystem);
                Add(evidence, open.ClassName, open.Guid, "writing-system-selector", open.WsSelector.ToString(System.Globalization.CultureInfo.InvariantCulture));
                break;
            default:
                unavailable.Add($"Feature definition '{feature.Guid:D}' has unsupported class '{feature.ClassName}'.");
                break;
        }
        AddFeatureSpecification(cache, feature.DefaultOA, evidence, unavailable, visited);
    }

    private static void AddEnvironment(LcmCache cache, IPhEnvironment environment, ICollection<string> evidence,
        ISet<string> unavailable)
    {
        Add(evidence, environment.ClassName, environment.Guid, "owner", environment.Owner?.Guid.ToString("D") ?? "null");
        if (environment.StringRepresentation is { } representation)
            Add(evidence, environment.ClassName, environment.Guid, "representation", representation.Text);
        else
            unavailable.Add($"PhEnvironment '{environment.Guid:D}' has no StringRepresentation.");
        AddPhonologicalContext(cache, environment.LeftContextRA, "left-context", environment, evidence, unavailable, new HashSet<Guid>());
        AddPhonologicalContext(cache, environment.RightContextRA, "right-context", environment, evidence, unavailable, new HashSet<Guid>());
    }

    private static void AddPhonologicalContext(LcmCache cache, IPhPhonContext? context, string role,
        IPhEnvironment environment, ICollection<string> evidence, ISet<string> unavailable, ISet<Guid> visited)
    {
        if (context is null)
        {
            Add(evidence, environment.ClassName, environment.Guid, role, "null");
            return;
        }
        if (!visited.Add(context.Guid))
        {
            unavailable.Add($"Phonological context for environment '{environment.Guid:D}' contains a cycle at '{context.Guid:D}'.");
            return;
        }
        Add(evidence, context.ClassName, context.Guid, "owner", context.Owner?.Guid.ToString("D") ?? "null");
        switch (context)
        {
            case IPhSequenceContext sequence:
                for (var index = 0; index < sequence.MembersRS.Count; index++)
                    AddPhonologicalContext(cache, sequence.MembersRS[index], role + ":member:" + index,
                        environment, evidence, unavailable, visited);
                break;
            case IPhIterationContext iteration:
                Add(evidence, iteration.ClassName, iteration.Guid, "minimum", iteration.Minimum.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Add(evidence, iteration.ClassName, iteration.Guid, "maximum", iteration.Maximum.ToString(System.Globalization.CultureInfo.InvariantCulture));
                AddPhonologicalContext(cache, iteration.MemberRA, role + ":member", environment, evidence, unavailable, visited);
                break;
            case IPhSimpleContextSeg segment:
                AddPhoneme(cache, segment.FeatureStructureRA, evidence, unavailable);
                break;
            case IPhSimpleContextNC naturalClassContext:
                AddNaturalClass(cache, naturalClassContext.FeatureStructureRA, evidence, unavailable);
                foreach (var constraint in naturalClassContext.PlusConstrRS.Concat(naturalClassContext.MinusConstrRS)
                             .OrderBy(item => item.Guid))
                {
                    Add(evidence, constraint.ClassName, constraint.Guid, "feature",
                        constraint.FeatureRA?.Guid.ToString("D") ?? "null");
                    if (constraint.FeatureRA is { } feature)
                        AddFeatureDefinition(cache, feature, evidence, unavailable, new HashSet<Guid>());
                }
                break;
            case IPhSimpleContextBdry boundary:
                Add(evidence, boundary.ClassName, boundary.Guid, "boundary", boundary.FeatureStructureRA?.Guid.ToString("D") ?? "null");
                if (boundary.FeatureStructureRA is { } marker)
                    foreach (var code in marker.CodesOS.Select((item, index) => (item, index)))
                        AddAlternatives(cache, code.item.Representation, marker.ClassName, marker.Guid, "boundary-code:" + code.index, evidence);
                break;
            default:
                unavailable.Add($"Phonological context '{context.Guid:D}' has unsupported class '{context.ClassName}'.");
                break;
        }
        _ = cache;
    }

    private static void AddNaturalClass(LcmCache cache, IPhNaturalClass? naturalClass, ICollection<string> evidence,
        ISet<string> unavailable)
    {
        if (naturalClass is null)
        {
            unavailable.Add("A phonological natural-class context has no class identity.");
            return;
        }
        Add(evidence, naturalClass.ClassName, naturalClass.Guid, "owner", naturalClass.Owner?.Guid.ToString("D") ?? "null");
        AddAlternatives(cache, naturalClass.Name, naturalClass.ClassName, naturalClass.Guid, "name", evidence);
        AddAlternatives(cache, naturalClass.Abbreviation, naturalClass.ClassName, naturalClass.Guid,
            "abbreviation", evidence);
        AddAlternatives(cache, naturalClass.Description, naturalClass.ClassName, naturalClass.Guid,
            "description", evidence);
        switch (naturalClass)
        {
            case IPhNCSegments segments:
                AddSet("segment-member", segments.SegmentsRC, segments.ClassName, segments.Guid, evidence);
                foreach (var phoneme in segments.SegmentsRC.OrderBy(item => item.Guid)) AddPhoneme(cache, phoneme, evidence, unavailable);
                break;
            case IPhNCFeatures features:
                AddFeatureStructure(cache, features.FeaturesOA, evidence, unavailable, new HashSet<Guid>());
                break;
            default:
                unavailable.Add($"Natural class '{naturalClass.Guid:D}' has unsupported class '{naturalClass.ClassName}'.");
                break;
        }
    }

    private static void AddPhoneme(LcmCache cache, IPhPhoneme? phoneme, ICollection<string> evidence, ISet<string> unavailable)
    {
        if (phoneme is null)
        {
            unavailable.Add("A phonological segment context has no phoneme identity.");
            return;
        }
        Add(evidence, phoneme.ClassName, phoneme.Guid, "owner", phoneme.Owner?.Guid.ToString("D") ?? "null");
        var index = 0;
        foreach (var code in phoneme.CodesOS)
            AddAlternatives(cache, code.Representation, phoneme.ClassName, phoneme.Guid, "code:" + index++, evidence);
        AddFeatureStructure(cache, phoneme.FeaturesOA, evidence, unavailable, new HashSet<Guid>());
    }

    private static void AddEntryReference(LcmCache cache, ILexEntryRef reference, ICollection<string> evidence)
    {
        Add(evidence, reference.ClassName, reference.Guid, "owner", reference.Owner?.Guid.ToString("D") ?? "null");
        Add(evidence, reference.ClassName, reference.Guid, "reference-type", reference.RefType.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddSequence("component-lexeme", reference.ComponentLexemesRS, reference, evidence);
        AddSequence("primary-lexeme", reference.PrimaryLexemesRS, reference, evidence);
        AddSequence("variant-entry-type", reference.VariantEntryTypesRS, reference, evidence);
        AddSequence("complex-entry-type", reference.ComplexEntryTypesRS, reference, evidence);
        AddSequence("show-complex-form-in", reference.ShowComplexFormsInRS, reference, evidence);
        foreach (var type in reference.VariantEntryTypesRS.Concat(reference.ComplexEntryTypesRS)
                     .DistinctBy(item => item.Guid))
        {
            AddAlternatives(cache, type.Name, type.ClassName, type.Guid, "name", evidence);
            AddAlternatives(cache, type.Abbreviation, type.ClassName, type.Guid, "abbreviation", evidence);
            AddAlternatives(cache, type.Description, type.ClassName, type.Guid, "description", evidence);
        }
    }

    private static void AddDerivationalMsa(LcmCache cache, IMoDerivAffMsa msa, ICollection<string> evidence,
        ISet<string> unavailable)
    {
        Add(evidence, msa.ClassName, msa.Guid, "owner", msa.Owner?.Guid.ToString("D") ?? "null");
        Add(evidence, msa.ClassName, msa.Guid, "from-stem-name", msa.FromStemNameRA?.Guid.ToString("D") ?? "null");
        Add(evidence, msa.ClassName, msa.Guid, "from-part-of-speech", msa.FromPartOfSpeechRA?.Guid.ToString("D") ?? "null");
        Add(evidence, msa.ClassName, msa.Guid, "to-part-of-speech", msa.ToPartOfSpeechRA?.Guid.ToString("D") ?? "null");
        Add(evidence, msa.ClassName, msa.Guid, "from-inflection-class", msa.FromInflectionClassRA?.Guid.ToString("D") ?? "null");
        Add(evidence, msa.ClassName, msa.Guid, "to-inflection-class", msa.ToInflectionClassRA?.Guid.ToString("D") ?? "null");
        Add(evidence, msa.ClassName, msa.Guid, "affix-category", msa.AffixCategoryRA?.Guid.ToString("D") ?? "null");
        Add(evidence, msa.ClassName, msa.Guid, "stratum", msa.StratumRA?.Guid.ToString("D") ?? "null");
        AddSet("from-productivity-restriction", msa.FromProdRestrictRC, msa.ClassName, msa.Guid, evidence);
        AddSet("to-productivity-restriction", msa.ToProdRestrictRC, msa.ClassName, msa.Guid, evidence);
        AddFeatureStructure(cache, msa.FromMsFeaturesOA, evidence, unavailable, new HashSet<Guid>());
        AddFeatureStructure(cache, msa.ToMsFeaturesOA, evidence, unavailable, new HashSet<Guid>());
        if (msa.FromPartOfSpeechRA is { } fromPos) AddPartOfSpeechAncestry(cache, fromPos, evidence, unavailable);
        if (msa.ToPartOfSpeechRA is { } toPos) AddPartOfSpeechAncestry(cache, toPos, evidence, unavailable);
        if (msa.FromInflectionClassRA is { } fromClass)
            AddInflectionClass(cache, fromClass, evidence, unavailable, new HashSet<Guid>());
        if (msa.ToInflectionClassRA is { } toClass)
            AddInflectionClass(cache, toClass, evidence, unavailable, new HashSet<Guid>());
        if (msa.StratumRA is { } stratum) AddStratum(cache, stratum, evidence, unavailable);
    }

    private static void AddDerivation(LcmCache cache, IMoDeriv derivation, ICollection<string> evidence,
        ISet<string> unavailable)
    {
        Add(evidence, derivation.ClassName, derivation.Guid, "owner", derivation.Owner?.Guid.ToString("D") ?? "null");
        Add(evidence, derivation.ClassName, derivation.Guid, "stem-form", derivation.StemFormRA?.Guid.ToString("D") ?? "null");
        Add(evidence, derivation.ClassName, derivation.Guid, "stem-msa", derivation.StemMsaRA?.Guid.ToString("D") ?? "null");
        Add(evidence, derivation.ClassName, derivation.Guid, "inflectional-features", derivation.InflectionalFeatsOA?.Guid.ToString("D") ?? "null");
        AddFeatureStructure(cache, derivation.InflectionalFeatsOA, evidence, unavailable, new HashSet<Guid>());
        if (derivation.StemMsaRA is { } msa) AddStemMsa(cache, msa, evidence, unavailable);
        AddSequence("stratum-app", derivation.StratumAppsOS, derivation, evidence);
        foreach (var app in derivation.StratumAppsOS) AddStratumApp(cache, app, evidence, unavailable);
    }

    private static void AddStratumApp(LcmCache cache, IMoStratumApp app, ICollection<string> evidence,
        ISet<string> unavailable)
    {
        Add(evidence, app.ClassName, app.Guid, "owner", app.Owner?.Guid.ToString("D") ?? "null");
        Add(evidence, app.ClassName, app.Guid, "stratum", app.StratumRA?.Guid.ToString("D") ?? "null");
        if (app.StratumRA is { } stratum) AddStratum(cache, stratum, evidence, unavailable);
        AddSequence("compound-rule-app", app.CompoundRuleAppsOS, app, evidence);
        foreach (var compound in app.CompoundRuleAppsOS) AddCompound(cache, compound, evidence);
        AddSequence("derivational-affix-app", app.DerivAffAppOS, app, evidence);
        foreach (var affixApp in app.DerivAffAppOS)
        {
            Add(evidence, affixApp.ClassName, affixApp.Guid, "affix-form",
                affixApp.AffixFormRA?.Guid.ToString("D") ?? "null");
            if (affixApp.AffixFormRA is { } affixForm) AddForm(cache, affixForm, evidence);
            Add(evidence, affixApp.ClassName, affixApp.Guid, "affix-msa",
                affixApp.AffixMsaRA?.Guid.ToString("D") ?? "null");
            if (affixApp.AffixMsaRA is { } msa) AddDerivationalMsa(cache, msa, evidence, unavailable);
        }
        AddSequence("phonological-rule-app", app.PRuleAppsOS, app, evidence);
        foreach (var ruleApp in app.PRuleAppsOS)
        {
            Add(evidence, ruleApp.ClassName, ruleApp.Guid, "owner", ruleApp.Owner?.Guid.ToString("D") ?? "null");
            unavailable.Add($"Phonological-rule application '{ruleApp.Guid:D}' has no stem-expansion interpretation.");
        }
        if (app.TemplateAppOA is { } templateApp)
        {
            Add(evidence, templateApp.ClassName, templateApp.Guid, "owner", templateApp.Owner?.Guid.ToString("D") ?? "null");
            Add(evidence, templateApp.ClassName, templateApp.Guid, "template",
                templateApp.TemplateRA?.Guid.ToString("D") ?? "null");
            if (templateApp.TemplateRA is { } template) AddInflTemplate(cache, template, evidence, unavailable);
            AddSequence("slot-app", templateApp.SlotAppsOS, templateApp, evidence);
            foreach (var slotApp in templateApp.SlotAppsOS)
            {
                Add(evidence, slotApp.ClassName, slotApp.Guid, "slot", slotApp.SlotRA?.Guid.ToString("D") ?? "null");
                Add(evidence, slotApp.ClassName, slotApp.Guid, "affix-form",
                    slotApp.AffixFormRA?.Guid.ToString("D") ?? "null");
                if (slotApp.AffixFormRA is IMoForm affixForm) AddForm(cache, affixForm, evidence);
                Add(evidence, slotApp.ClassName, slotApp.Guid, "affix-msa",
                    slotApp.AffixMsaRA?.Guid.ToString("D") ?? "null");
                if (slotApp.AffixMsaRA is { } msa) AddInflAffixMsa(cache, msa, evidence, unavailable);
            }
        }
    }

    private static void AddInflTemplate(LcmCache cache, IMoInflAffixTemplate template,
        ICollection<string> evidence, ISet<string> unavailable)
    {
        Add(evidence, template.ClassName, template.Guid, "owner", template.Owner?.Guid.ToString("D") ?? "null");
        AddAlternatives(cache, template.Name, template.ClassName, template.Guid, "name", evidence);
        AddAlternatives(cache, template.Description, template.ClassName, template.Guid, "description", evidence);
        Add(evidence, template.ClassName, template.Guid, "stratum", template.StratumRA?.Guid.ToString("D") ?? "null");
        Add(evidence, template.ClassName, template.Guid, "final", template.Final ? "true" : "false");
        Add(evidence, template.ClassName, template.Guid, "disabled", template.Disabled ? "true" : "false");
        AddSequence("slot", template.SlotsRS, template, evidence);
        AddSequence("prefix-slot", template.PrefixSlotsRS, template, evidence);
        AddSequence("suffix-slot", template.SuffixSlotsRS, template, evidence);
        AddSequence("proclitic-slot", template.ProcliticSlotsRS, template, evidence);
        AddSequence("enclitic-slot", template.EncliticSlotsRS, template, evidence);
        AddFeatureStructure(cache, template.RegionOA, evidence, unavailable, new HashSet<Guid>());
        if (template.StratumRA is { } stratum) AddStratum(cache, stratum, evidence, unavailable);
    }

    private static void AddStratum(LcmCache cache, IMoStratum stratum, ICollection<string> evidence,
        ISet<string> unavailable)
    {
        Add(evidence, stratum.ClassName, stratum.Guid, "owner", stratum.Owner?.Guid.ToString("D") ?? "null");
        AddAlternatives(cache, stratum.Name, stratum.ClassName, stratum.Guid, "name", evidence);
        AddAlternatives(cache, stratum.Abbreviation, stratum.ClassName, stratum.Guid, "abbreviation", evidence);
        AddAlternatives(cache, stratum.Description, stratum.ClassName, stratum.Guid, "description", evidence);
        Add(evidence, stratum.ClassName, stratum.Guid, "phoneme-set", stratum.PhonemesRA?.Guid.ToString("D") ?? "null");
        if (stratum.PhonemesRA is { } phonemeSet)
        {
            Add(evidence, phonemeSet.ClassName, phonemeSet.Guid, "owner", phonemeSet.Owner?.Guid.ToString("D") ?? "null");
            AddSet("phoneme", phonemeSet.PhonemesOC, phonemeSet.ClassName, phonemeSet.Guid, evidence);
            foreach (var phoneme in phonemeSet.PhonemesOC.OrderBy(item => item.Guid))
                AddPhoneme(cache, phoneme, evidence, unavailable);
        }
    }

    private static void AddCompound(LcmCache cache, IMoCompoundRuleApp compound, ICollection<string> evidence)
    {
        Add(evidence, compound.ClassName, compound.Guid, "owner", compound.Owner?.Guid.ToString("D") ?? "null");
        Add(evidence, compound.ClassName, compound.Guid, "left-form", compound.LeftFormRA?.Guid.ToString("D") ?? "null");
        Add(evidence, compound.ClassName, compound.Guid, "right-form", compound.RightFormRA?.Guid.ToString("D") ?? "null");
        Add(evidence, compound.ClassName, compound.Guid, "linker", compound.LinkerRA?.Guid.ToString("D") ?? "null");
        if (compound.LeftFormRA is { } left) AddForm(cache, left, evidence);
        if (compound.RightFormRA is { } right) AddForm(cache, right, evidence);
        if (compound.LinkerRA is { } linker) AddForm(cache, linker, evidence);
    }

    private static void AddFootprint(AllomorphReferenceFootprint footprint, ICollection<string> evidence)
    {
        foreach (var route in footprint.RouteNotes)
            evidence.Add(JsonSerializer.Serialize(new { kind = "reference-route", row = route }));
        foreach (var reference in footprint.References)
            evidence.Add(JsonSerializer.Serialize(new { kind = "reference", row = reference }));
        foreach (var owned in footprint.OwnedDependents)
            evidence.Add(JsonSerializer.Serialize(new { kind = "owned", row = owned }));
        foreach (var wordform in footprint.AffectedWordforms)
            evidence.Add(JsonSerializer.Serialize(new { kind = "wordform", row = wordform }));
        foreach (var historical in footprint.HistoricalReferences)
            evidence.Add(JsonSerializer.Serialize(new { kind = "historical", row = historical }));
    }

    private static void AddSequence<T>(string field, IEnumerable<T> values, ICmObject source,
        ICollection<string> evidence) where T : ICmObject
    {
        var index = 0;
        foreach (var value in values)
            Add(evidence, source.ClassName, source.Guid, field + ":" + index++, value.ClassName + ":" + value.Guid.ToString("D"));
    }

    private static void AddSet<T>(string field, IEnumerable<T> values, string className, Guid id,
        ICollection<string> evidence) where T : ICmObject
    {
        foreach (var value in values.OrderBy(item => item.Guid))
            Add(evidence, className, id, field, value.ClassName + ":" + value.Guid.ToString("D"));
    }

    private static void AddAlternatives(LcmCache cache, IMultiAccessorBase value, string className, Guid id, string field,
        ICollection<string> evidence)
    {
        foreach (var ws in value.AvailableWritingSystemIds
                     .Select(wsId => (Tag: cache.WritingSystemFactory.GetStrFromWs(wsId), Text: value.get_String(wsId)?.Text ?? string.Empty))
                     .OrderBy(item => item.Tag, StringComparer.Ordinal))
            Add(evidence, className, id, field + ":" + ws.Tag, ws.Text);
    }

    private static void Add(ICollection<string> evidence, string className, Guid id, string field, string value) =>
        evidence.Add(JsonSerializer.Serialize(new { className, id = id.ToString("D"), field, value }));
}

/// <summary>Immutable stem-selection evidence and its stable context digest.</summary>
public sealed record StemAllomorphSelectionContext(IReadOnlyList<Guid> Forms, IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Unavailable, string Digest);
