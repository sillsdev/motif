using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Snapshotting;

namespace SIL.Motif.Runner.Operations;

/// <summary>
/// Names the operation kind that creates a wordform. LibLCM keeps wordforms unowned and lists them through the
/// wordform repository, so the project root is the virtual owner and the model carries no owning field.
/// </summary>
/// <remarks>
/// The closed <c>after</c> payload is <c>{ "ws": "&lt;tag&gt;", "text": "&lt;form&gt;" }</c>: one authored surface form
/// in one writing system, in NFD. The effect records only the new wordform's membership and form, never the
/// inventory. A form that already exists in that writing system is a semantic conflict that names the existing
/// identity, so a caller reuses it; no match is ever inferred from similarity.
/// </remarks>
public static class LangProjectWordformsOperationKinds
{
    public const string Create = "analysis/langProject/createWordforms";

    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(Create);
        OperationHandlerRegistry.Register(Create, new CreateWordformHandler());
        OperationSlotRegistry.RegisterOwningCreate(Create, SnapshotFields.LangProjectWordforms, false);
    }
}

/// <summary>The closed payload of <see cref="LangProjectWordformsOperationKinds.Create"/>.</summary>
public static class LangProjectWordformsCreatePayload
{
    public static (string WritingSystemTag, string Text) Parse(JsonElement after)
    {
        var kind = LangProjectWordformsOperationKinds.Create;
        ClosedPayloadParsing.RequireObject(after, kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, ["ws", "text"], kind);
        var ws = ClosedPayloadParsing.GetRequiredString(after, "ws", kind);
        var text = ClosedPayloadParsing.GetRequiredString(after, "text", kind);
        if (string.IsNullOrWhiteSpace(ws) || string.IsNullOrWhiteSpace(text) || text != text.Trim() ||
            text.Any(char.IsWhiteSpace))
            throw new ContractParseException($"'{kind}' operation 'after' needs a writing system and one nonblank word without spaces.");
        if (!text.IsNormalized(NormalizationForm.FormD))
            throw new ContractParseException($"'{kind}' operation 'after.text' must be in NFD.");
        return (ws, text);
    }
}

internal sealed class CreateWordformHandler : IOperationHandler
{
    private const string Kind = LangProjectWordformsOperationKinds.Create;

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        if (operation.StorageIdOverride is not null)
            throw new InvalidOperationException($"'{Kind}': storage GUID overrides are not supported for this create.");
        var entityId = operation.EntityId ?? throw new InvalidOperationException($"'{Kind}' requires entityId.");
        var after = operation.After ?? throw new InvalidOperationException($"'{Kind}' requires after.");
        if (operation.Placement is not null)
            throw new InvalidOperationException($"'{Kind}': placement is not defined for wordforms.");
        var (ws, text) = LangProjectWordformsCreatePayload.Parse(after);
        var id = ResolveProject(cache, operation);
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        if (repository.IsValidObjectId(entityId.ToGuid()))
        {
            var existingClass = repository.GetObject(entityId.ToGuid()).ClassName;
            if (existingClass != "WfiWordform")
                throw new InvalidOperationException($"'{Kind}': identity '{entityId.Value}' already exists as {existingClass}, " +
                    "expected WfiWordform; this is a semantic conflict. Choose a fresh identity.");
            throw new InvalidOperationException($"'{Kind}': identity '{entityId.Value}' already exists; " +
                "creation would overwrite/reuse storage. Choose a fresh identity.");
        }
        var wsHandle = Resolve(cache, ws);
        var form = TsStringUtils.MakeString(text, wsHandle);
        var wordforms = cache.ServiceLocator.GetInstance<IWfiWordformRepository>();
        if (wordforms.TryGetObject(form, out var existing))
            throw new InvalidOperationException($"'{Kind}': a wordform '{text}' already exists in '{ws}' as " +
                $"'{CanonicalId.FromGuid(existing.Guid).Value}'; reuse that identity instead of creating another.");

        var before = Read(cache, entityId, ws, text);
        var created = cache.ServiceLocator.GetInstance<IWfiWordformFactory>().Create(entityId.ToGuid());
        created.Form.set_String(wsHandle, form);
        touchedTargets.Add(id);
        return new ExpectedEffect(id, SnapshotFields.LangProjectWordforms, before, Read(cache, entityId, ws, text));
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var entityId = operation.EntityId ?? throw new InvalidOperationException($"'{Kind}' requires entityId.");
        var (ws, text) = LangProjectWordformsCreatePayload.Parse(operation.After ??
            throw new InvalidOperationException($"'{Kind}' requires after."));
        var id = ResolveProject(cache, operation);
        var current = Read(cache, entityId, ws, text);
        return new ExpectedEffect(id, SnapshotFields.LangProjectWordforms, current, current);
    }

    private static CanonicalId ResolveProject(LcmCache cache, OperationEnvelope operation)
    {
        var (id, project) = TargetResolution.Resolve<ILangProject>(cache, operation, Kind);
        if (project.Guid != cache.LangProject.Guid)
            throw new InvalidOperationException($"'{Kind}': wordforms are created only in this project.");
        return id;
    }

    private static int Resolve(LcmCache cache, string ws)
    {
        var handle = cache.WritingSystemFactory.GetWsFromStr(ws);
        return handle != 0 ? handle
            : throw new InvalidOperationException($"'{Kind}' operation: writing system tag '{ws}' is not known to this project.");
    }

    private static Dictionary<string, string> Read(LcmCache cache, CanonicalId entityId, string ws, string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        var handle = cache.WritingSystemFactory.GetWsFromStr(ws);
        if (handle != 0 && cache.ServiceLocator.GetInstance<IWfiWordformRepository>()
                .TryGetObject(TsStringUtils.MakeString(text, handle), out var holder) && holder.Guid != entityId.ToGuid())
            result["formOwner:" + ws] = CanonicalId.FromGuid(holder.Guid).Value;
        if (!repository.TryGetObject(entityId.ToGuid(), out var found)) return result;
        if (found is not IWfiWordform wordform)
        {
            result["collision"] = entityId.Value;
            return result;
        }
        foreach (var (field, value) in WordformOwningSnapshots.Read(cache, wordform)
                     .AlternativesFields[SnapshotFields.LangProjectWordforms])
            if (field == "ref" || field == "form:" + ws) result[field] = value;
        return result;
    }
}
