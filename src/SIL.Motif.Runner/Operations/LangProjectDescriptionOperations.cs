using System.Runtime.CompilerServices;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Snapshotting;

namespace SIL.Motif.Runner.Operations;

/// <summary>
/// Names the operation kind that sets one writing-system alternative of the project's Description. It targets the
/// project root, and writes the whole alternative: a caller that means to add to the text sends the old text with the
/// addition, as the Attribution composer does.
/// </summary>
public static class LangProjectDescriptionOperationKinds
{
    public const string Set = "project/langProject/setDescription";

#pragma warning disable CA2255 // Load-time registration makes these kinds available before parsing and dispatch.
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(Set);
        OperationHandlerRegistry.Register(Set, LangProjectDescriptionHandler.Instance);
        OperationSlotRegistry.RegisterAlternative(Set, SnapshotFields.LangProjectDescription);
    }
#pragma warning restore CA2255
}

/// <summary>The closed <c>after</c> payload <c>{ "ws": tag, "text": text }</c> of the set operation.</summary>
public static class LangProjectDescriptionPayload
{
    /// <summary>Reads the payload, refusing an unknown property or an empty text.</summary>
    public static (string WritingSystemTag, string Text) Parse(JsonElement after)
    {
        var kind = LangProjectDescriptionOperationKinds.Set;
        ClosedPayloadParsing.RequireObject(after, kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, ["ws", "text"], kind);
        var text = ClosedPayloadParsing.GetRequiredString(after, "text", kind);
        if (text.Length == 0)
            throw new ContractParseException($"'{kind}' operation 'after.text' must not be empty.");
        return (ClosedPayloadParsing.GetRequiredString(after, "ws", kind), text);
    }
}

internal sealed class LangProjectDescriptionHandler : IOperationHandler
{
    private const string Kind = LangProjectDescriptionOperationKinds.Set;
    internal static readonly LangProjectDescriptionHandler Instance = new();
    private LangProjectDescriptionHandler() { }

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        var after = operation.After ?? throw new InvalidOperationException(
            $"Operation '{operation.OperationId.Value}' of kind '{Kind}' requires 'after'.");
        var (tag, text) = LangProjectDescriptionPayload.Parse(after);
        var (id, project) = Resolve(cache, operation);
        touchedTargets.Add(id);

        var before = ReadField(cache, project);
        MultiAlternativesFieldLowering.ApplySet(cache, project.Description, tag, text, Kind);
        return new ExpectedEffect(id, SnapshotFields.LangProjectDescription, before, ReadField(cache, project));
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var (id, project) = Resolve(cache, operation);
        var current = ReadField(cache, project);
        return new ExpectedEffect(id, SnapshotFields.LangProjectDescription, current, current);
    }

    private static (CanonicalId Id, ILangProject Project) Resolve(LcmCache cache, OperationEnvelope operation)
    {
        var (id, project) = TargetResolution.Resolve<ILangProject>(cache, operation, Kind);
        if (project.Guid != cache.LangProject.Guid)
            throw new InvalidOperationException($"'{Kind}': the Description is changed only in this project.");
        return (id, project);
    }

    private static IReadOnlyDictionary<string, string> ReadField(LcmCache cache, ILangProject project) =>
        MultiAlternativesFieldSnapshotting.ReadAlternatives(cache, project.Description);
}
