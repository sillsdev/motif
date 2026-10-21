using System;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;

namespace SIL.Motif.Runner.Operations;

internal static class AffixProcessConstruction
{
    internal static IMoAffixProcess Apply(LcmCache cache, ILexEntry entry, CanonicalId id, JsonElement after)
    {
        const string kind = LexEntryLexemeFormOperationKinds.CreateLexemeForm;
        ClosedPayloadParsing.RequireObject(after, kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, new[] { "class", "morphType" }, kind);
        if (after.EnumerateObject().Count() != 2)
            throw new ContractParseException("A process payload must contain class and morphType exactly once.");
        if (ClosedPayloadParsing.GetRequiredString(after, "class", kind) != "MoAffixProcess")
            throw new ContractParseException("The explicit lexeme-form class must be MoAffixProcess.");
        var morphType = ReferenceFieldLowering.Resolve<IMoMorphType>(cache,
            ClosedPayloadParsing.GetRequiredCanonicalId(after, "morphType", kind), kind);
        RequireAffixMorphType(morphType);
        RequireFreshIdentity(cache, id, "MoAffixProcess");
        if (entry.LexemeFormOA is not null)
            throw new InvalidOperationException("The lexeme-form slot is occupied; process creation cannot replace it.");
        var process = cache.ServiceLocator.GetInstance<IMoAffixProcessFactory>().Create(id.ToGuid());
        entry.LexemeFormOA = process;
        process.MorphTypeRA = morphType;
        process.IsAbstract = false;
        return process;
    }

    /// <summary>
    /// Adds an empty affix process to the entry's alternate forms. The entry's lexeme form must itself be an
    /// affix process, so a process graph is never mixed with plain allomorphs.
    /// </summary>
    internal static IMoAffixProcess ApplyAlternate(LcmCache cache, ILexEntry entry, CanonicalId id, JsonElement after, int index)
    {
        const string kind = LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms;
        ClosedPayloadParsing.RequireObject(after, kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, new[] { "class", "morphType" }, kind);
        if (after.EnumerateObject().Count() != 2)
            throw new ContractParseException("A process payload must contain class and morphType exactly once.");
        if (ClosedPayloadParsing.GetRequiredString(after, "class", kind) != "MoAffixProcess")
            throw new ContractParseException("The explicit alternate-form class must be MoAffixProcess.");
        var morphType = ReferenceFieldLowering.Resolve<IMoMorphType>(cache,
            ClosedPayloadParsing.GetRequiredCanonicalId(after, "morphType", kind), kind);
        RequireAffixMorphType(morphType);
        CreateIdentity.RequireFresh(cache, id, kind, "MoAffixProcess");
        RequireProcessEntry(entry, kind);
        var process = cache.ServiceLocator.GetInstance<IMoAffixProcessFactory>().Create(id.ToGuid());
        entry.AlternateFormsOS.Insert(index, process);
        process.MorphTypeRA = morphType;
        process.IsAbstract = false;
        return process;
    }

    /// <summary>Requires an entry whose lexeme form is an affix process and whose alternates are all processes.</summary>
    internal static void RequireProcessEntry(ILexEntry entry, string construct)
    {
        var name = CanonicalId.FromGuid(entry.Guid).Value;
        if (entry.LexemeFormOA is null)
            throw new InvalidOperationException($"'{construct}': entry '{name}' has no lexeme form to add a process allomorph to.");
        if (entry.LexemeFormOA is IMoStemAllomorph)
            throw new InvalidOperationException($"'{construct}': entry '{name}' is a stem; only an affix takes a process allomorph.");
        if (entry.LexemeFormOA is not IMoAffixProcess || entry.AlternateFormsOS.Any(form => form is not IMoAffixProcess))
            throw new InvalidOperationException(
                $"'{construct}': entry '{name}' mixes plain allomorphs with affix processes, which is not supported yet; " +
                "give the entry an affix-process lexeme form and only process alternates.");
    }

    internal static void RequireAffixMorphType(IMoMorphType morphType)
    {
        if (MoFormConcreteClassSelection.ClassFor(morphType, "AuthorAffixProcess") != MoFormConcreteClass.AffixAllomorph)
            throw new InvalidOperationException("An affix process needs a supported affix morph type.");
    }

    internal static void RequireFreshIdentity(LcmCache cache, CanonicalId id, string desiredClass)
    {
        if (id.ToGuid() == Guid.Empty)
            throw new InvalidOperationException("Creation requires a nonzero entity identity; LibLCM cannot preserve the zero GUID.");
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        if (!repository.IsValidObjectId(id.ToGuid())) return;
        var existingClass = repository.GetObject(id.ToGuid()).ClassName;
        if (existingClass != desiredClass)
            throw new InvalidOperationException($"Identity '{id.Value}' already exists as {existingClass}, expected " +
                $"{desiredClass}; this is a semantic conflict. Choose a fresh identity.");
        throw new InvalidOperationException($"Identity '{id.Value}' already exists; creation would overwrite/reuse " +
            "storage. Choose a fresh identity.");
    }
}
