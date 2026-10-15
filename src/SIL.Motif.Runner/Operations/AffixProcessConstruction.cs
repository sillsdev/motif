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
        foreach (var mapping in process.OutputOS.ToArray()) process.OutputOS.Remove(mapping);
        foreach (var input in process.InputOS.ToArray()) process.InputOS.Remove(input);
        process.MorphTypeRA = morphType;
        process.IsAbstract = false;
        return process;
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
