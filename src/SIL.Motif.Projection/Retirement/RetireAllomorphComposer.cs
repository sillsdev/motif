using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Retirement;

namespace SIL.Motif.Projection.Retirement;

public static class RetireAllomorphComposer
{
    public static IReadOnlyList<OperationEnvelope> Build(
        LcmCache cache, RetireAllomorphIntentDocument document, Func<CanonicalId> operationIdFactory,
        RuleReplacementWitness? ruleReplacement = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(operationIdFactory);
        var intent = document.Retirement;
        var survivorIdentities = document.DuplicateSurvivors;
        if (intent.Scope != AllomorphRetirementScope.Affix)
            throw new InvalidOperationException("Only ordinary affix alternates are supported by this retirement composer.");
        var entryId = ParseId(intent.Entry, "entry");
        if (!cache.ServiceLocator.ObjectRepository.TryGetObject(entryId.ToGuid(), out var entryObject) ||
            entryObject is not ILexEntry entry || entry.LexemeFormOA is null)
            throw new InvalidOperationException("Allomorph retirement requires an entry with a primary form.");

        var retired = intent.RetiredForms.Select(item => ResolveForm(cache, item, entry, source: true)).ToArray();
        var footprint = AllomorphReferenceFootprintReader.Read(cache, retired.Select(item => item.Guid));
        if (footprint.OwnedDependents.Count > 0)
            throw new InvalidOperationException("A retired form owns objects that deletion would cascade.");
        var retiredIds = retired.Select(item => CanonicalId.FromGuid(item.Guid)).ToHashSet();
        if (survivorIdentities.Select(item => ParseId(item.Id, "surviving form")).Distinct().Count() != survivorIdentities.Count)
            throw new InvalidOperationException("A duplicate survivor may be declared only once.");
        var survivors = survivorIdentities
            .Select(item => ResolveForm(cache, item, entry, source: false)).ToArray();
        if (survivors.Length == 0 || survivors.Any(item => retiredIds.Contains(CanonicalId.FromGuid(item.Guid))))
            throw new InvalidOperationException("Retirement requires a surviving duplicate form for every retired form.");

        var matchedSurvivors = new HashSet<Guid>();
        foreach (var form in retired)
        {
            var matching = survivors.Where(item => SameDuplicateSignature(cache, form, item) ||
                ruleReplacement is { } proof && RuleBackedAllomorphFormProof.Matches(cache, form, item,
                    proof.InputPhoneme, proof.OutputPhoneme)).ToArray();
            if (matching.Length == 0)
                throw new InvalidOperationException("Every retired form requires an exact surviving duplicate or a single-rule form match in the same entry.");
            foreach (var destination in matching) matchedSurvivors.Add(destination.Guid);
            if (IsZeroForm(cache, form) && HasMorphologicalFeatures(entry))
                throw new InvalidOperationException("Feature-bearing zero morphology cannot be retired as a duplicate form.");
        }
        if (matchedSurvivors.Count != survivors.Length)
            throw new InvalidOperationException("Every declared survivor must be an exact duplicate of a retired form.");
        var declaredSurvivorIds = survivorIdentities.Select(item => ParseId(item.Id, "surviving form")).ToHashSet();
        var declaredSurvivors = survivorIdentities.ToDictionary(item => ParseId(item.Id, "surviving form"));
        var replacementAssertions = intent.RoleReplacements.Select(item => item.Replacement)
            .Concat(intent.AdhocReplacements.Select(item => item.Replacement));
        if (replacementAssertions.Any(item => !declaredSurvivorIds.Contains(ParseId(item.Id, "reference destination")) ||
                !SameIdentity(declaredSurvivors[ParseId(item.Id, "reference destination")], item)))
            throw new InvalidOperationException("Every reference destination must be declared as a duplicate survivor.");

        var retargets = AllomorphRetargetComposer.Build(cache, intent, operationIdFactory);
        var before = entry.AlternateFormsOS.Select(item => CanonicalId.FromGuid(item.Guid)).ToArray();
        if (before.Length == 0 || retired.Any(item => !entry.AlternateFormsOS.Contains(item)))
            throw new InvalidOperationException("Only current alternate forms can be retired.");
        var after = before.Where(item => !retiredIds.Contains(item)).ToArray();
        var deleteId = operationIdFactory();
        if (retargets.Any(item => item.OperationId == deleteId))
            throw new InvalidOperationException("The operation id factory returned a duplicate id.");
        var delete = new OperationEnvelope(deleteId, AlternateFormRetirementOperationKinds.DeleteAlternateForm,
            target: entryId,
            after: JsonSerializer.SerializeToElement(new
            {
                forms = retired.Select(item => CanonicalId.FromGuid(item.Guid).Value).OrderBy(item => item, StringComparer.Ordinal),
                duplicateSurvivors = survivors.Select(item => CanonicalId.FromGuid(item.Guid).Value).OrderBy(item => item, StringComparer.Ordinal),
                formWitnesses = intent.RetiredForms.Concat(survivorIdentities)
                    .DistinctBy(item => ParseId(item.Id, "allomorph identity"))
                    .OrderBy(item => item.Id, StringComparer.Ordinal)
                    .Select(item => new { form = CanonicalId.Parse(item.Id).Value, semanticDigest = item.SemanticDigest }),
                before = before.Select(item => item.Value),
                after = after.Select(item => item.Value),
                ruleReplacement = ruleReplacement is { } proof ? new
                {
                    rule = proof.Rule.Value,
                    inputPhoneme = proof.InputPhoneme.Value,
                    outputPhoneme = proof.OutputPhoneme.Value
                } : null
            }),
            dependsOn: retargets.Select(item => new OperationDependency(item.OperationId)).ToArray());
        return retargets.Append(delete).ToArray();
    }

    private static IMoAffixAllomorph ResolveForm(
        LcmCache cache, AllomorphIdentity identity, ILexEntry entry, bool source)
    {
        var id = ParseId(identity.Id, source ? "retired form" : "surviving form");
        if (!cache.ServiceLocator.ObjectRepository.TryGetObject(id.ToGuid(), out var value) ||
            value is not IMoAffixAllomorph form || form.ClassName != "MoAffixAllomorph" ||
            form.Owner?.Guid != entry.Guid || form.IsAbstract)
            throw new InvalidOperationException("Retirement identities must resolve to ordinary forms of the owning entry.");
        var location = entry.AlternateFormsOS.Contains(form) ? "alternate" :
            entry.LexemeFormOA?.Guid == form.Guid ? "lexeme" : "other";
        var position = form.MorphTypeRA?.Guid == MoMorphTypeTags.kguidMorphPrefix ? "prefix" :
            form.MorphTypeRA?.Guid == MoMorphTypeTags.kguidMorphSuffix ? "suffix" : "unsupported";
        if (identity.Entry != CanonicalId.FromGuid(entry.Guid).Value || identity.Id != id.Value ||
            identity.Class != form.ClassName || identity.Location != location || identity.Position != position ||
            (source && location != "alternate") || (!source && location is not ("alternate" or "lexeme")))
            throw new InvalidOperationException("An authored allomorph identity no longer matches its LibLCM object.");
        if (!StringComparer.Ordinal.Equals(identity.SemanticDigest, AllomorphRetirementSemanticDigest.Compute(cache, form)))
            throw new InvalidOperationException("An allomorph's measured semantic meaning changed before retirement composition.");
        return form;
    }

    private static bool SameDuplicateSignature(LcmCache cache, IMoAffixAllomorph source, IMoAffixAllomorph destination)
    {
        if (source.Guid == destination.Guid || source.MorphTypeRA?.Guid != destination.MorphTypeRA?.Guid ||
            source.MsEnvFeaturesOA?.Guid != destination.MsEnvFeaturesOA?.Guid ||
            !Alternatives(cache, source.Form).SequenceEqual(Alternatives(cache, destination.Form)))
            return false;
        return source.PhoneEnvRC.Select(item => item.Guid).Order().SequenceEqual(destination.PhoneEnvRC.Select(item => item.Guid).Order()) &&
               source.PositionRS.Select(item => item.Guid).SequenceEqual(destination.PositionRS.Select(item => item.Guid)) &&
                   source.InflectionClassesRC.Select(item => item.Guid).Order()
                   .SequenceEqual(destination.InflectionClassesRC.Select(item => item.Guid).Order());
    }

    private static bool SameIdentity(AllomorphIdentity left, AllomorphIdentity right) =>
        ParseId(left.Id, "surviving form") == ParseId(right.Id, "surviving form") &&
        ParseId(left.Entry, "surviving entry") == ParseId(right.Entry, "surviving entry") &&
        left.Class == right.Class && left.Location == right.Location && left.Position == right.Position &&
        StringComparer.Ordinal.Equals(left.SemanticDigest, right.SemanticDigest) && left.StemName == right.StemName;

    private static KeyValuePair<string, string>[] Alternatives(LcmCache cache, IMultiAccessorBase form) =>
        form.AvailableWritingSystemIds
            .Select(ws => (Tag: cache.WritingSystemFactory.GetStrFromWs(ws),
                Text: form.get_String(ws)?.get_NormalizedForm(FwNormalizationMode.knmNFSC)?.Text ?? string.Empty))
            .Where(item => item.Text.Length > 0)
            .Select(item => new KeyValuePair<string, string>(item.Tag, item.Text.Normalize(System.Text.NormalizationForm.FormD)))
            .OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();

    private static bool IsZeroForm(LcmCache cache, IMoAffixAllomorph form)
    {
        var values = Alternatives(cache, form.Form).Select(item => item.Value).ToArray();
        return values.Length == 0 || values.All(value => value.Trim() is "0" or "∅" or "Ø");
    }

    private static bool HasMorphologicalFeatures(ILexEntry entry) =>
        entry.MorphoSyntaxAnalysesOC.OfType<IMoInflAffMsa>().Any(item => item.InflFeatsOA is not null) ||
        entry.MorphoSyntaxAnalysesOC.OfType<IMoDerivAffMsa>()
            .Any(item => item.FromMsFeaturesOA is not null || item.ToMsFeaturesOA is not null);

    private static CanonicalId ParseId(string value, string role) =>
        CanonicalId.TryParse(value, out var id) ? id : throw new InvalidOperationException($"The {role} is not a canonical id.");

    private static bool SameId(string value, Guid guid) => CanonicalId.TryParse(value, out var id) && id.ToGuid() == guid;
}
