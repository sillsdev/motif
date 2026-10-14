using System;
using System.Collections.Generic;
using System.Linq;
using SIL.LCModel;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Runner.Operations;

/// <summary>Checks ordinary form class restrictions after the complete declared graph exists.</summary>
internal static class LexicalFormSemanticValidator
{
    internal static void ValidateProposal(LcmCache cache, IReadOnlyList<OperationEnvelope> operations)
    {
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
