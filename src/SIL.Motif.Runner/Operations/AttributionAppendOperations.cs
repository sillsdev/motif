using System.Runtime.CompilerServices;
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
/// Names the two operation kinds that append one run of text to a writing-system alternative of an entry's Bibliography or
/// of the project's Description. They append to the existing string, so its styles and writing-system runs survive.
/// </summary>
public static class AttributionOperationKinds
{
    public const string AppendToBibliography = "lexical/lexEntry/appendBibliography";
    public const string AppendToDescription = "project/langProject/appendDescription";

#pragma warning disable CA2255 // Load-time registration makes these kinds available before parsing and dispatch.
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(AppendToBibliography);
        OperationKindRegistry.Register(AppendToDescription);
        OperationHandlerRegistry.Register(AppendToBibliography, new AttributionAppendHandler<ILexEntry>(
            AppendToBibliography, SnapshotFields.LexEntryBibliography, entry => entry.Bibliography));
        OperationHandlerRegistry.Register(AppendToDescription, new AttributionAppendHandler<ILangProject>(
            AppendToDescription, SnapshotFields.LangProjectDescription, project => project.Description));
        OperationSlotRegistry.RegisterAlternative(AppendToBibliography, SnapshotFields.LexEntryBibliography);
        OperationSlotRegistry.RegisterAlternative(AppendToDescription, SnapshotFields.LangProjectDescription);
    }
#pragma warning restore CA2255
}

/// <summary>The closed <c>after</c> payload <c>{ "ws": tag, "text": item, "separator": joiner }</c> of an append operation.</summary>
public static class AttributionAppendPayload
{
    /// <summary>Reads the payload, refusing an unknown property or an empty text.</summary>
    public static (string WritingSystemTag, string Text, string Separator) Parse(JsonElement after, string kind)
    {
        ClosedPayloadParsing.RequireObject(after, kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, ["ws", "text", "separator"], kind);
        var text = ClosedPayloadParsing.GetRequiredString(after, "text", kind);
        if (text.Length == 0) throw new ContractParseException($"'{kind}' operation 'after.text' must not be empty.");
        return (ClosedPayloadParsing.GetRequiredString(after, "ws", kind), text,
            ClosedPayloadParsing.GetRequiredString(after, "separator", kind));
    }

    /// <summary>Whether <paramref name="current"/> already holds <paramref name="item"/> as one of its segments.</summary>
    public static bool Holds(string current, string item) =>
        current.Split([';', '\n', '\u2028']).Any(part => part.Trim() == item);
}

internal sealed class AttributionAppendHandler<TOwner>(string kind, string field, Func<TOwner, IMultiString> accessor) : IOperationHandler
    where TOwner : class, ICmObject
{
    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        var after = operation.After ?? throw new InvalidOperationException(
            $"Operation '{operation.OperationId.Value}' of kind '{kind}' requires 'after'.");
        var (tag, text, separator) = AttributionAppendPayload.Parse(after, kind);
        var (id, owner) = Resolve(cache, operation);
        touchedTargets.Add(id);

        var before = ReadField(cache, owner);
        var ws = cache.WritingSystemFactory.GetWsFromStr(tag);
        if (ws == 0)
            throw new InvalidOperationException($"'{kind}' operation: writing system tag '{tag}' is not known to this project.");
        var multi = accessor(owner);
        var current = multi.get_String(ws);
        if (current is null || !AttributionAppendPayload.Holds(current.Text ?? string.Empty, text))
        {
            var builder = (current ?? TsStringUtils.MakeString(string.Empty, ws)).GetBldr();
            var addition = builder.Length == 0 ? text : separator + text;
            builder.ReplaceTsString(builder.Length, builder.Length, TsStringUtils.MakeString(addition, ws));
            multi.set_String(ws, builder.GetString());
        }
        return new ExpectedEffect(id, field, before, ReadField(cache, owner));
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var (id, owner) = Resolve(cache, operation);
        var current = ReadField(cache, owner);
        return new ExpectedEffect(id, field, current, current);
    }

    private (CanonicalId Id, TOwner Owner) Resolve(LcmCache cache, OperationEnvelope operation)
    {
        var (id, owner) = TargetResolution.Resolve<TOwner>(cache, operation, kind);
        if (owner is ILangProject project && project.Guid != cache.LangProject.Guid)
            throw new InvalidOperationException($"'{kind}': the Description is changed only in this project.");
        return (id, owner);
    }

    private IReadOnlyDictionary<string, string> ReadField(LcmCache cache, TOwner owner) =>
        MultiAlternativesFieldSnapshotting.ReadAlternatives(cache, accessor(owner));
}
