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
        AffixProcessGraphBuilder.RequireReferences(cache, intent.Input, intent.Output);
        var mint = mintId ?? (() => CanonicalId.Mint());
        var entry = new AuthorLexicalEntryIntent("", "", intent.MorphType, intent.Gloss, intent.AnalysisWritingSystem,
            intent.Msa, intent.Definition);
        var operations = AuthorLexicalEntryComposer.BuildEntry(cache, entry, mint,
            new { @class = "MoAffixProcess", morphType = intent.MorphType.Value }).ToList();
        var processCreate = operations.Single(operation => operation.Kind == LexEntryLexemeFormOperationKinds.CreateLexemeForm);
        var process = processCreate.EntityId!.Value;
        var creates = operations.Where(operation => operation.EntityId is not null)
            .ToDictionary(operation => operation.EntityId!.Value, operation => operation.OperationId, CanonicalId.ByGuid);
        if (intent.Form is not null)
        {
            LexicalFormAuthoring.RequireWritingSystem(cache, intent.FormWritingSystem!, vernacular: true, "AuthorAffixProcess");
            operations.Add(new OperationEnvelope(mint(), MoFormFormOperationKinds.SetForm, target: process,
                after: JsonSerializer.SerializeToElement(new
                {
                    ws = intent.FormWritingSystem,
                    text = intent.Form.Normalize(System.Text.NormalizationForm.FormC),
                }),
                dependsOn: [new OperationDependency(processCreate.OperationId)],
                rationale: "Authored affix-process headword."));
        }
        AffixProcessGraphBuilder.Append(operations, mint, creates, process, intent.Input, intent.Output,
            "Authored affix-process intent.");
        return operations;
    }
}
