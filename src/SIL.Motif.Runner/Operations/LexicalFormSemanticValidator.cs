using System;
using System.Collections.Generic;
using System.Linq;
using SIL.LCModel;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Runner.Operations;

/// <summary>Checks explicit default and ordinary form class domains after the complete declared graph exists.</summary>
internal static class LexicalFormSemanticValidator
{
    internal static void ValidateProposal(LcmCache cache, IReadOnlyList<OperationEnvelope> operations)
    {
        foreach (var operation in operations.Where(operation => operation.Kind is
            PartOfSpeechDefaultInflectionClassOperationKinds.SetDefaultInflectionClass or
            PartOfSpeechDefaultInflectionClassOperationKinds.ClearDefaultInflectionClass))
        {
            var target = operation.Target ?? throw new InvalidOperationException("A category default edit needs a target.");
            var category = InflectionAuthoring.Category(cache, target);
            if (category.DefaultInflectionClassRA is { } cls &&
                !LexicalFormAuthoring.InCategoryOrAncestor(InflectionAuthoring.OwningCategory(cls), category))
                throw new InvalidOperationException("The default class must belong to the category or one of its ancestors.");
        }
        var relevant = operations.Where(operation => operation.Kind is
            LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms or
            MoAffixFormInflectionClassesOperationKinds.AddRefInflectionClasses).ToArray();
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        var forms = new Dictionary<Guid, IMoAffixForm>();
        foreach (var operation in relevant)
        {
            if (operation.Target is { } target && repository.IsValidObjectId(target.ToGuid())) Include(repository.GetObject(target.ToGuid()));
            if (operation.EntityId is { } entity && repository.IsValidObjectId(entity.ToGuid())) Include(repository.GetObject(entity.ToGuid()));
        }
        foreach (var form in forms.Values)
        {
            if (form.InflectionClassesRC.Count == 0) continue;
            if (form.Owner is not ILexEntry entry)
                throw new InvalidOperationException("An affix form must belong to a lexical entry.");
            var categories = entry.MorphoSyntaxAnalysesOC.Select(msa => msa switch
            {
                IMoInflAffMsa inflectional => inflectional.PartOfSpeechRA,
                IMoDerivAffMsa derivational => derivational.FromPartOfSpeechRA,
                IMoUnclassifiedAffixMsa unclassified => unclassified.PartOfSpeechRA,
                _ => null,
            }).OfType<IPartOfSpeech>().ToArray();
            LexicalFormAuthoring.RequireInflectionClasses(cache,
                form.InflectionClassesRC.Select(item => SIL.Motif.Contract.Ids.CanonicalId.FromGuid(item.Guid)).ToArray(),
                categories, "Affix form restrictions");
        }
        void Include(ICmObject value)
        {
            if (value is IMoAffixForm form) forms[form.Guid] = form;
        }
    }
}
