using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Authors a new affix entry without mutating the caller's loaded project.</summary>
public static class AuthorAffixProcessComposer
{
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, AuthorAffixProcessIntent intent,
        Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        AffixProcessIntentShape.Validate(intent);
        AffixProcessSemanticValidator.RequireCategory(cache,
            ReferenceFieldLowering.Resolve<IPartOfSpeech>(cache, intent.Msa.Category, "AuthorAffixProcess"));
        if (intent.Msa.ToCategory is { } toCategory)
            AffixProcessSemanticValidator.RequireCategory(cache,
                ReferenceFieldLowering.Resolve<IPartOfSpeech>(cache, toCategory, "AuthorAffixProcess"));
        var morphType = ReferenceFieldLowering.Resolve<IMoMorphType>(cache, intent.MorphType, "AuthorAffixProcess");
        AffixProcessConstruction.RequireAffixMorphType(morphType);
        foreach (var part in intent.Input)
        {
            if (part.Phoneme is { } phone) AffixProcessSemanticValidator.RequirePhoneme(cache, phone);
            if (part.NaturalClass is { } naturalClass) AffixProcessSemanticValidator.RequireNaturalClass(cache, naturalClass);
        }
        foreach (var phone in intent.Output.Where(step => step.Insert is not null).SelectMany(step => step.Insert!))
            AffixProcessSemanticValidator.RequirePhoneme(cache, phone);
        var mint = mintId ?? (() => CanonicalId.Mint());
        var entry = new AuthorLexicalEntryIntent("", "", intent.MorphType, intent.Gloss, intent.AnalysisWritingSystem,
            intent.Msa, intent.Definition);
        var operations = AuthorLexicalEntryComposer.BuildEntry(cache, entry, mint,
            new { @class = "MoAffixProcess", morphType = intent.MorphType.Value }).ToList();
        var processCreate = operations.Single(operation => operation.Kind == LexEntryLexemeFormOperationKinds.CreateLexemeForm);
        var process = processCreate.EntityId!.Value;
        var creates = operations.Where(operation => operation.EntityId is not null)
            .ToDictionary(operation => operation.EntityId!.Value, operation => operation.OperationId, CanonicalId.ByGuid);
        var parts = new Dictionary<string, CanonicalId>(StringComparer.Ordinal);
        CanonicalId? priorInput = null;
        foreach (var part in intent.Input)
        {
            var concreteClass = part.Remaining ? "PhVariable" : part.Phoneme is not null ? "PhSimpleContextSeg" : "PhSimpleContextNC";
            var id = Create(MoAffixProcessInputOperationKinds.Create, concreteClass, priorInput);
            parts.Add(part.Name, id);
            if (part.Phoneme is { } phone)
                Add(PhSimpleContextSegFeatureStructureOperationKinds.SetFeatureStructure, id, new { @ref = phone.Value });
            if (part.NaturalClass is { } naturalClass)
                Add(PhSimpleContextNCFeatureStructureOperationKinds.SetFeatureStructure, id, new { @ref = naturalClass.Value });
            priorInput = id;
        }
        CanonicalId? priorOutput = null;
        foreach (var step in intent.Output)
        {
            if (step.Copy is { } name)
            {
                var id = Create(MoAffixProcessOutputOperationKinds.Create, "MoCopyFromInput", priorOutput);
                Add(MoCopyFromInputContentOperationKinds.SetContent, id, new { @ref = parts[name].Value }, parts[name]);
                priorOutput = id;
            }
            else foreach (var phone in step.Insert!)
            {
                var id = Create(MoAffixProcessOutputOperationKinds.Create, "MoInsertPhones", priorOutput);
                Add(MoInsertPhonesContentOperationKinds.AddRefContent, id, new { member = phone.Value });
                priorOutput = id;
            }
        }
        return operations;

        CanonicalId Create(string kind, string concreteClass, CanonicalId? prior)
        {
            var id = mint();
            var operation = Add(kind, process, new { @class = concreteClass }, prior, id,
                prior is { } previous ? new Placement(previous, null) : null);
            creates.Add(id, operation.OperationId);
            return id;
        }
        OperationEnvelope Add(string kind, CanonicalId target, object after, CanonicalId? referenced = null,
            CanonicalId? entity = null, Placement? placement = null)
        {
            var dependencies = new[] { (CanonicalId?)target, referenced }.Where(id => id is not null && creates.ContainsKey(id.Value))
                .Select(id => new OperationDependency(creates[id!.Value])).Distinct().ToArray();
            var operation = new OperationEnvelope(mint(), kind, entityId: entity, target: target,
                after: JsonSerializer.SerializeToElement(after), placement: placement, dependsOn: dependencies,
                rationale: "Authored affix-process intent.");
            operations.Add(operation);
            return operation;
        }
    }
}
