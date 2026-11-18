using System.Text;
using SIL.LCModel;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Core.Text;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.HandAnalyses;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Execution;

namespace SIL.Motif.Commands;

internal sealed class HandAnalysisResolution(LcmCache cache, HandAnalysisInput input)
{
    private readonly List<HandAnalysisDiagnostic> _diagnostics = [];
    private readonly Dictionary<Guid, List<Guid>> _variantEdges = [];
    private readonly IReadOnlyList<IMoMorphType> _types = Types(cache.LangProject.LexDbOA.MorphTypesOA.PossibilitiesOS).ToArray();
    private IWfiWordform? _wordform;

    internal HandAnalysisCheckResponse Read()
    {
        if (cache.LangProject.Guid != input.Expected.ProjectGuid)
            Fail(RefusalCodes.HandAnalysisStale, "/expected/projectGuid", "The input belongs to another project.");
        var ws = Ws(input.Wordform.Ws, "/wordform/ws", vernacular: true);
        var casing = new CaseFunctions(cache.ServiceLocator.WritingSystemManager.Get(ws));
        if (Nfd(casing.ToLower(input.Wordform.Surface)) != Nfd(casing.ToLower(input.Wordform.CaseForm)))
            Fail(RefusalCodes.HandAnalysisInvalidLink, "/wordform/caseForm", "The selected form is not a case alternative of the occurrence surface.");
        var inventory = cache.ServiceLocator.GetInstance<IWfiWordformRepository>();
        var matches = inventory.AllInstances().Where(word => Nfd(word.Form.get_String(ws).Text) == Nfd(input.Wordform.CaseForm))
            .OrderBy(word => word.Guid).ToArray();
        if (input.Wordform.Id is { } wordId)
        {
            _wordform = Resolve<IWfiWordform>(wordId, "/wordform/id");
            if (Nfd(_wordform!.Form.get_String(ws).Text) != Nfd(input.Wordform.CaseForm))
                Fail(RefusalCodes.HandAnalysisInvalidLink, "/wordform/id", "The wordform does not have this writing system and case form.");
        }
        else if (matches.Length > 1)
            Fail(RefusalCodes.HandAnalysisAmbiguousWordform, "/wordform/id", "Select one of the matching wordform identities.",
                string.Join(",", matches.Select(word => word.Guid)));
        else _wordform = matches.SingleOrDefault();
        if (_wordform is null) Warn("wordform-absent", "/wordform", "No matching wordform exists; check creates none.");
        else if (matches.Length > 1) Warn("duplicate-matches", "/wordform", "Other wordforms have the same case form.", matches.Select(w => w.Guid).ToArray());
        if (input.CategoryId is { } category) Resolve<IPartOfSpeech>(category, "/categoryId");
        CheckOccurrence(ws);
        var started = input.StartedFromAnalysisId is { } startedId ? Resolve<IWfiAnalysis>(startedId, "/startedFromAnalysisId") : null;
        if (started is not null && !IsOpeningCaseForm(started.Owner as IWfiWordform, ws))
            Fail(RefusalCodes.HandAnalysisInvalidLink, "/startedFromAnalysisId", "The starting analysis is not owned by a case-related source wordform.");
        var analysis = input.SelectedAnalysisId is { } analysisId ? Resolve<IWfiAnalysis>(analysisId, "/selectedAnalysisId") : null;
        if (analysis is not null && analysis.Owner != _wordform)
            Fail(RefusalCodes.HandAnalysisInvalidLink, "/selectedAnalysisId", "The analysis is not owned by the selected wordform.");
        CheckGloss(started, analysis, ws);
        CheckCreations();
        var pieces = input.Pieces.Select((piece, index) => Piece(piece, index)).ToArray();
        Warn("home-guards-unavailable", "/expected", "A saved .fwdata read cannot verify Home revisions, occurrence heads or external Scripture context.");
        if (input.LexiconPolicy == "SyncSinglePieceGlossAndCategory")
        {
            if (input.Pieces.Count > 1) Fail(RefusalCodes.HandAnalysisInvalidLink, "/lexiconPolicy", "Lexicon synchronization requires a single Piece.");
            Warn("lexicon-policy", "/lexiconPolicy", "Single-piece gloss and category synchronization requires the Home annotation transaction; check changes no lexical record.");
        }
        return new(input.RequestId, cache.LangProject.Guid, matches.Select(word => word.Guid).ToArray(), _wordform?.Guid, pieces, _diagnostics);
    }

    private HandAnalysisPieceCheck Piece(HandAnalysisPiece piece, int index)
    {
        var at = "/pieces/" + index;
        var ws = Ws(piece.Form.Ws, at + "/form/ws");
        Multi(piece.BundleForm, at + "/bundleForm");
        var (type, candidates) = MorphType(piece.Form, at + "/form");
        var link = piece.Link;
        var entry = Resolve<ILexEntry>(link?.Entry, at + "/link/entry");
        var morph = Resolve<IMoForm>(link?.Allomorph, at + "/link/allomorph");
        var sense = Resolve<ILexSense>(link?.Sense, at + "/link/sense");
        var msa = Resolve<IMoMorphSynAnalysis>(link?.Msa, at + "/link/msa");
        var variant = Resolve<ILexEntryRef>(piece.VariantRef, at + "/variantRef");
        entry ??= EntryOf(morph) ?? (variant is null ? EntryOf(sense) ?? EntryOf(msa) : EntryOf(variant));
        if (morph is not null && EntryOf(morph) != entry)
            Fail(RefusalCodes.HandAnalysisInvalidLink, at + "/link/allomorph", "The allomorph is not owned by the selected entry.");
        ILexEntry? main = entry;
        if (variant is not null)
        {
            if (EntryOf(variant) != entry || variant.RefType != LexEntryRefTags.krtVariant || variant.ComponentLexemesRS.Count != 1)
                Fail(RefusalCodes.HandAnalysisInvalidLink, at + "/variantRef", "The variant reference must belong to the allomorph entry and name one component.");
            var component = variant.ComponentLexemesRS[0];
            ValidateVariant(() => VariantLinkSafety.CheckComponent(cache, entry!, component), at + "/variantRef");
            foreach (var variantType in variant.VariantEntryTypesRS)
                ValidateVariant(() => VariantLinkSafety.CheckType(cache, variantType), at + "/variantRef");
            main = component as ILexEntry ?? EntryOf(component);
            if (component is ILexSense selected && sense is not null && selected != sense)
                Fail(RefusalCodes.HandAnalysisInvalidLink, at + "/link/sense", "The selected sense differs from the variant's sense component.");
        }
        if (sense is not null && EntryOf(sense) != main)
            Fail(RefusalCodes.HandAnalysisInvalidLink, at + "/link/sense", "The sense is not owned by the selected main entry.");
        if (msa is not null && EntryOf(msa) != main)
            Fail(RefusalCodes.HandAnalysisInvalidLink, at + "/link/msa", "The grammatical info is not owned by the selected main entry.");
        if (sense is not null && msa is not null && sense.MorphoSyntaxAnalysisRA is not null && sense.MorphoSyntaxAnalysisRA != msa)
            Fail(RefusalCodes.HandAnalysisInvalidLink, at + "/link/msa", "The sense and grammatical info do not agree.");
        if (link?.InflTypeId is { } inflId)
        {
            var infl = Resolve<ILexEntryInflType>(inflId, at + "/link/inflTypeId");
            if (variant is null || !variant.VariantEntryTypesRS.Contains(infl!))
                Fail(RefusalCodes.HandAnalysisInvalidLink, at + "/link/inflTypeId", "The inflectional type must belong to the selected variant relation.");
        }
        if (morph is not null)
        {
            var actual = morph.MorphTypeRA;
            var unmarked = piece.Form.Markers.Before.Length == 0 && piece.Form.Markers.After.Length == 0;
            var clitic = actual?.Guid == MoMorphTypeTags.kguidMorphProclitic || actual?.Guid == MoMorphTypeTags.kguidMorphEnclitic;
            if (actual is null || !(candidates.Any(candidate => candidate.Guid == actual.Guid) || unmarked && clitic))
                Fail(RefusalCodes.HandAnalysisTypeMismatch, at + "/link/allomorph", "The selected allomorph does not fit these markers.", consequence: "choose-another-allomorph-or-explicit-creation");
            if (actual.IsAffixType != (morph is IMoAffixAllomorph or IMoAffixProcess))
                Fail(RefusalCodes.HandAnalysisTypeMismatch, at + "/link/allomorph", "The allomorph class and morph type have different families.");
        }
        if (msa is not null && morph is not null && (msa is IMoStemMsa) != (morph is IMoStemAllomorph))
            Fail(RefusalCodes.HandAnalysisTypeMismatch, at + "/link/msa", "The allomorph and grammatical info have different families.", consequence: "requires-explicit-compatible-msa");
        var forms = entry is null ? [] : Forms(entry).Where(form =>
            form.MorphTypeRA is { } actual && (candidates.Any(candidate => candidate.Guid == actual.Guid) ||
                piece.Form.Markers.Before.Length == 0 && piece.Form.Markers.After.Length == 0 &&
                (actual.Guid == MoMorphTypeTags.kguidMorphProclitic || actual.Guid == MoMorphTypeTags.kguidMorphEnclitic)) &&
            EqualForm(form, ws, piece.Form.Text)).Select(form => form.Guid).ToArray();
        if (forms.Length > 1) Warn("duplicate-matches", at, "Several allomorphs match; lexeme then alternate sequence order is reported.", forms);
        return new(index, candidates.Select(candidate => candidate.Guid).ToArray(), forms,
            entry?.Guid, morph?.Guid, sense?.Guid, msa?.Guid)
        { PieceKey = piece.PieceKey, EffectiveMorphTypeId = type.Guid, TypedSpelling = piece.Form.Markers.Before + piece.Form.Text + piece.Form.Markers.After,
            VariantRefId = variant?.Guid, InflTypeId = link?.InflTypeId };
    }

    private (IMoMorphType Type, IMoMorphType[] Candidates) MorphType(HandAnalysisForm form, string at)
    {
        var markers = form.Markers;
        var candidates = _types.Where(type => (type.Prefix ?? "") == markers.Before && (type.Postfix ?? "") == markers.After).ToArray();
        var stem = _types.FirstOrDefault(type => type.Guid == MoMorphTypeTags.kguidMorphStem);
        if (stem is null) Fail(RefusalCodes.HandAnalysisInvalidLink, at, "The project has no stem morph type.");
        var fallback = candidates.Length == 0;
        IMoMorphType type;
        if (fallback)
        {
            type = stem!;
            Warn("marker-fallback", at + "/markers", "Unknown markers retain their spelling and use the project's stem GUID, including for lexical creation.", [type.Guid], "continue-as-stem");
            candidates = [type];
        }
        else if (form.Text.Length == 0 || !cache.ServiceLocator.WritingSystems.VernacularWritingSystems.Any(system => system.Id == form.Ws)) type = stem!;
        else if (markers.Before.Length == 0 && markers.After.Length == 0)
            type = form.Text.Contains(' ') ? candidates.FirstOrDefault(item => item.Guid == MoMorphTypeTags.kguidMorphPhrase) ?? stem! : stem!;
        else
        {
            type = candidates[0];
            var preferred = type.Guid == MoMorphTypeTags.kguidMorphBoundRoot ? MoMorphTypeTags.kguidMorphBoundStem :
                type.Guid == MoMorphTypeTags.kguidMorphPrefixingInterfix ? MoMorphTypeTags.kguidMorphPrefix :
                type.Guid == MoMorphTypeTags.kguidMorphSuffixingInterfix ? MoMorphTypeTags.kguidMorphSuffix :
                type.Guid == MoMorphTypeTags.kguidMorphInfixingInterfix ? MoMorphTypeTags.kguidMorphInfix : type.Guid;
            type = candidates.FirstOrDefault(item => item.Guid == preferred) ?? type;
        }
        if (form.MorphTypeId is { } explicitId)
        {
            var explicitType = Resolve<IMoMorphType>(explicitId, at + "/morphTypeId")!;
            if (!candidates.Contains(explicitType))
                Fail(RefusalCodes.HandAnalysisTypeMismatch, at + "/morphTypeId", "The explicit morph type disagrees with the project's marker values.");
            type = explicitType;
        }
        if (candidates.Length > 1) Warn("marker-candidates", at + "/markers", "Marker typing has a deterministic default; these other types share its markers.", candidates.Select(item => item.Guid).ToArray());
        return (type, candidates);
    }

    private void CheckGloss(IWfiAnalysis? started, IWfiAnalysis? analysis, int wordformWs)
    {
        foreach (var ws in input.WordGloss.EnabledWs) Ws(ws, "/wordGloss/enabledWs", analysis: true);
        var id = input.WordGloss switch { HandAnalysisSelectGloss selected => selected.Id, HandAnalysisAutoGloss auto => auto.StartedFromId, _ => (Guid?)null };
        if (id is { } glossId)
        {
            var selected = input.WordGloss is HandAnalysisSelectGloss;
            var pointer = "/wordGloss/" + (selected ? "id" : "startedFromId");
            var gloss = Resolve<IWfiGloss>(glossId, pointer);
            var owner = selected ? analysis : started;
            var valid = owner is not null ? gloss!.Owner == owner : selected
                ? gloss!.Owner.Owner == _wordform : IsOpeningCaseForm(gloss!.Owner.Owner as IWfiWordform, wordformWs);
            if (!valid)
                Fail(RefusalCodes.HandAnalysisInvalidLink, pointer, "The gloss does not belong to its analysis and wordform.");
        }
        if (input.WordGloss is HandAnalysisAutoGloss autoGloss)
        {
            Multi(autoGloss.Forms, "/wordGloss/forms", analysis: true);
            foreach (var tag in autoGloss.Forms.Keys)
                if (!autoGloss.EnabledWs.Contains(tag)) Fail(RefusalCodes.HandAnalysisInvalidLink, "/wordGloss/forms/" + HandAnalysisJson.Escape(tag), "The gloss alternative is not enabled.");
        }
    }

    private bool IsOpeningCaseForm(IWfiWordform? wordform, int ws)
    {
        var text = wordform?.Form.get_String(ws).Text;
        if (text is null) return false;
        var casing = new CaseFunctions(cache.ServiceLocator.WritingSystemManager.Get(ws));
        return Nfd(casing.ToLower(text)) == Nfd(casing.ToLower(input.Wordform.Surface));
    }

    private void CheckOccurrence(int ws)
    {
        if (input.Occurrence is not HandAnalysisTextAnchor anchor)
        {
            Warn("source-unavailable", "/occurrence", "Scripture source availability and verse guards require the external Source and Home; they are unverified.");
            return;
        }
        var text = Resolve<IText>(anchor.TextId, "/occurrence/textId");
        var paragraph = Resolve<IStTxtPara>(anchor.ParagraphId, "/occurrence/paragraphId");
        var segment = Resolve<ISegment>(anchor.SegmentId, "/occurrence/segmentId");
        if (paragraph!.Owner?.Owner != text || segment!.Owner != paragraph)
            Fail(RefusalCodes.HandAnalysisInvalidLink, "/occurrence", "The Text, paragraph and Segment ownership does not agree.");
        var occurrences = SegmentServices.GetAnalysisOccurrences(paragraph).Where(item => item.Segment == segment).ToArray();
        var occurrence = occurrences.FirstOrDefault(item => item.Index == anchor.Index);
        if (occurrence is null || !occurrence.HasWordform || occurrence.BaselineWs != ws ||
            Nfd(occurrence.BaselineText.Text) != Nfd(input.Wordform.Surface) ||
            occurrences.Where(item => item.Index <= anchor.Index).Count(item => item.HasWordform) != anchor.WordPosition)
            Fail(RefusalCodes.HandAnalysisStale, "/occurrence", "The native word position, writing system or surface has changed.");
        if (input.Expected.AssignmentId != occurrence!.Analysis.Guid || input.Expected.ParseIsCurrent != paragraph.ParseIsCurrent)
            Fail(RefusalCodes.HandAnalysisStale, "/expected", "The native assignment or parse-current guard has changed.");
    }

    private void CheckCreations()
    {
        for (var index = 0; index < input.Creations.Count; index++)
        {
            var at = "/creations/" + index;
            switch (input.Creations[index])
            {
                case HandAnalysisEntryCreation entry:
                    Multi(entry.Forms, at + "/forms", vernacular: true);
                    Multi(entry.Glosses, at + "/glosses", analysis: true);
                    var entryType = Resolve<IMoMorphType>(entry.MorphTypeId, at + "/morphTypeId")!;
                    Msa(entry.Msa, at + "/msa", entryType.IsAffixType);
                    break;
                case HandAnalysisSenseCreation sense:
                    var owner = Resolve<ILexEntry>(sense.Entry, at + "/entry");
                    Multi(sense.Glosses, at + "/glosses", analysis: true);
                    var defaultTag = cache.ServiceLocator.WritingSystemManager.Get(cache.DefaultAnalWs).Id;
                    if (!sense.Glosses.TryGetValue(defaultTag, out var gloss) || string.IsNullOrWhiteSpace(gloss))
                        Fail(RefusalCodes.HandAnalysisInvalidLink, at + "/glosses/" + HandAnalysisJson.Escape(defaultTag),
                            "Add New Sense requires a nonblank gloss in the default analysis writing system.");
                    var msa = Resolve<IMoMorphSynAnalysis>(sense.Msa.Reference, at + "/msa");
                    if (owner is not null && msa is not null && msa.Owner != owner)
                        Fail(RefusalCodes.HandAnalysisInvalidLink, at + "/msa", "The MSA belongs to another entry.");
                    if (sense.Msa.Definition is { } definition) Msa(definition, at + "/msa");
                    Warn("new-sense", at, "Add New Sense creates a distinct sense; only grammatical info may be reused.");
                    break;
                case HandAnalysisAllomorphCreation form:
                    var entryOwner = Resolve<ILexEntry>(form.Entry, at + "/entry");
                    var morphType = Resolve<IMoMorphType>(form.MorphTypeId, at + "/morphTypeId")!;
                    Multi(form.Forms, at + "/forms", vernacular: true);
                    if (entryOwner?.LexemeFormOA is { } lexeme && morphType.IsAffixType != (lexeme is IMoAffixAllomorph or IMoAffixProcess))
                    {
                        if (form.Mismatch == "refuse") Fail(RefusalCodes.HandAnalysisTypeMismatch, at + "/mismatch", "The new allomorph and entry have different families.", consequence: "would-refuse-without-explicit-compatible-msa");
                        Warn("type-mismatch", at, "A compatible default MSA would be created or reused without rewriting existing senses.", consequence: morphType.IsAffixType ? "unclassified-affix-msa" : "stem-msa");
                    }
                    break;
                case HandAnalysisVariantCreation variant:
                    var variantOwner = Resolve<ILexEntry>(variant.Entry, at + "/entry");
                    Variant(variant.Component, variant.VariantTypeIds, at, variantOwner);
                    break;
                case HandAnalysisVariantEntryCreation variantEntry:
                    Multi(variantEntry.Forms, at + "/forms", vernacular: true);
                    Resolve<IMoMorphType>(variantEntry.MorphTypeId, at + "/morphTypeId");
                    Variant(variantEntry.Component, variantEntry.VariantTypeIds, at);
                    break;
            }
        }
    }

    private void Msa(HandAnalysisMsaSpec spec, string at, bool? affix = null)
    {
        if (spec.CategoryId is { } category) Resolve<IPartOfSpeech>(category, at + "/categoryId");
        if (affix is not null && affix != (spec.Kind == "unclassified-affix"))
            Fail(RefusalCodes.HandAnalysisTypeMismatch, at, "The default MSA and allomorph type have different families.");
    }

    private void Variant(HandAnalysisVariantComponent component, IReadOnlyList<Guid> types, string at, ILexEntry? source = null)
    {
        var pointer = at + "/component/" + (component.EntryId is not null ? "entryId" : "senseId");
        ICmObject? target = component.EntryId is { } entry ? Resolve<ILexEntry>(entry, pointer) :
            component.SenseId is { } sense ? Resolve<ILexSense>(sense, pointer) : null;
        for (var index = 0; index < types.Count; index++)
        {
            var typePointer = at + "/variantTypeIds/" + index;
            var type = Resolve<ILexEntryType>(types[index], typePointer)!;
            ValidateVariant(() => VariantLinkSafety.CheckType(cache, type), typePointer);
        }
        if (source is not null && target is not null)
        {
            ValidateVariant(() => VariantLinkSafety.CheckComponent(cache, source, target, _variantEdges), pointer);
            if (!_variantEdges.TryGetValue(source.Guid, out var targets)) _variantEdges[source.Guid] = targets = [];
            targets.Add(EntryOf(target)!.Guid);
        }
        else Warn("variant-graph-unavailable", at, "The source entry has no native identity until creation; its variant graph remains unresolved.");
    }

    private static void ValidateVariant(Action validate, string at)
    {
        try { validate(); }
        catch (ProposalRefusalException exception)
        { Fail(RefusalCodes.HandAnalysisInvalidLink, at, exception.Message); }
    }

    private T? Resolve<T>(string? reference, string at) where T : class, ICmObject
    {
        if (reference is null) return null;
        if (HandAnalysisJson.TryId(reference, out var id)) return Resolve<T>(id, at);
        Warn("request-local", at, "This request-local reference has no stored identity until its explicit creation is committed.");
        return null;
    }

    private T? Resolve<T>(Guid id, string at) where T : class, ICmObject
    {
        if (!cache.ServiceLocator.ObjectRepository.TryGetObject(id, out var found) || found is not T)
            Fail(RefusalCodes.HandAnalysisInvalidLink, at, "The id is missing from this project or names the wrong class.");
        return (T)found;
    }

    private int Ws(string tag, string at, bool vernacular = false, bool analysis = false)
    {
        var systems = cache.ServiceLocator.WritingSystems;
        var all = vernacular ? systems.VernacularWritingSystems : analysis ? systems.AnalysisWritingSystems :
            systems.VernacularWritingSystems.Concat(systems.AnalysisWritingSystems);
        var system = all.FirstOrDefault(item => item.Id == tag);
        if (system is null) Fail(RefusalCodes.HandAnalysisInvalidLink, at, "The writing system does not belong to the required project writing-system list.");
        return system!.Handle;
    }

    private void Multi(IReadOnlyDictionary<string, string>? alternatives, string at, bool vernacular = false, bool analysis = false)
    {
        if (alternatives is null) return;
        foreach (var tag in alternatives.Keys) Ws(tag, at + "/" + HandAnalysisJson.Escape(tag), vernacular, analysis);
    }

    private bool EqualForm(IMoForm form, int ws, string text)
    {
        var stored = form.Form.get_String(ws).Text;
        if (stored is null) return false;
        var system = cache.ServiceLocator.WritingSystemManager.Get(ws);
        var collator = system.DefaultCollation?.Collator;
        return collator is null ? Nfd(stored) == Nfd(text) : collator.Compare(Nfd(stored), Nfd(text)) == 0;
    }

    private static IEnumerable<IMoForm> Forms(ILexEntry entry) =>
        (entry.LexemeFormOA is { } form ? new[] { form } : []).Concat(entry.AlternateFormsOS);

    private static ILexEntry? EntryOf(ICmObject? item)
    {
        while (item is not null && item is not ILexEntry) item = item.Owner;
        return item as ILexEntry;
    }

    private static IEnumerable<IMoMorphType> Types(IEnumerable<ICmPossibility> items)
    {
        foreach (var item in items)
        {
            if (item is IMoMorphType type) yield return type;
            foreach (var nested in Types(item.SubPossibilitiesOS)) yield return nested;
        }
    }

    private static string Nfd(string? value) => (value ?? "").Normalize(NormalizationForm.FormD);
    private void Warn(string code, string at, string message, IReadOnlyList<Guid>? matches = null, string? consequence = null) =>
        _diagnostics.Add(new(code, at, message, matches, consequence));

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Fail(string code, string at, string message, string? matches = null, string? consequence = null)
    {
        var facts = new Dictionary<string, string> { ["pointer"] = at };
        if (matches is not null) facts["matches"] = matches;
        if (consequence is not null) facts["consequence"] = consequence;
        throw new Refused(new Refusal(code, FailureReason.Refused, message, facts));
    }

    internal sealed class Refused(Refusal refusal) : Exception(refusal.Message)
    { internal Refusal Refusal { get; } = refusal; }
}
