using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Execution;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Scripture;

namespace SIL.Motif.Commands;

/// <summary>What the five Source composers share: the closed intent fields, how they are read, and the exposure.</summary>
internal static class SourceIntents
{
    public static readonly IReadOnlyList<string> Subjects = ["scripture-sources"];

    public static ComposerExposure ExposureFor(string toolName) =>
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: toolName);

    public static string Text(JsonElement authored, string name) =>
        authored.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : throw new ArgumentException($"'{name}' is required and must be text.");

    /// <summary>A later edit of the same part of the same Source replaces an earlier one, whichever case the id is written in.</summary>
    public static ReplacementRule SameSource(string what) => ReplacementRule.SameIdentity(authored =>
        authored.ValueKind == JsonValueKind.Object && authored.TryGetProperty("source", out var source) &&
        source.ValueKind == JsonValueKind.String ? source.GetString()!.Trim().ToLowerInvariant() : null,
        $"An earlier intent in this batch already sets the {what} of the same Source; author one edit.");
}

/// <summary>Stages a new Source in the project's Motif Sources list, which the first one creates.</summary>
internal sealed class AddSourceDefinition : ComposerDefinition<AddSourceIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-add-source";
    /// <inheritdoc />
    public override string Composer => "AddSource";
    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects => SourceIntents.Subjects;
    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Text("id", required: true),
        Field.Text("name", required: true),
        Field.Text("versification", required: true),
        Field.Text("context", required: true),
        Field.Choice("kind", [ScriptureSourceFields.Paratext, "Corpus"]));
    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } = SourceIntents.ExposureFor("motif_add_source");
    /// <inheritdoc />
    public override string? PrincipalKind => "Source";
    /// <inheritdoc />
    public override AddSourceIntent Parse(JsonElement authored) => new(
        SourceIntents.Text(authored, "id").Trim(), SourceIntents.Text(authored, "name").Trim(),
        SourceIntents.Text(authored, "versification"), SourceIntents.Text(authored, "context"),
        authored.TryGetProperty("kind", out var kind) ? kind.GetString() ?? ScriptureSourceFields.Paratext : ScriptureSourceFields.Paratext);
    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, AddSourceIntent intent)
    {
        if (intent.Kind != ScriptureSourceFields.Paratext)
            throw SourceRefusalException.Of(RefusalCodes.SourceKindUnsupported, FailureReason.InvalidArgument,
                "Only a Paratext Source can be added so far; a Corpus Source is modelled but not built.", ("kind", intent.Kind));
        SourceComposerSupport.RequireFreshId(context.Cache, intent.Id);
        if (intent.Name.Length == 0) throw new ArgumentException("A Source needs a name.");
        var (versificationName, versification) = SourceComposerSupport.Versification(intent.Versification);
        SourceComposerSupport.RequireContext(intent.Context, versification);
        return [new OperationEnvelope(context.MintId(), ScriptureSourceOperationKinds.Create,
            entityId: context.MintId(), target: CanonicalId.FromGuid(context.Cache.LangProject.Guid),
            after: JsonSerializer.SerializeToElement(new
            {
                id = intent.Id, name = intent.Name, versification = versificationName,
                context = intent.Context, kind = intent.Kind,
            }))];
    }
}

/// <summary>Stages a new display name for a Source.</summary>
internal sealed class SetSourceNameDefinition : ComposerDefinition<SetSourceNameIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-set-source-name";
    /// <inheritdoc />
    public override string Composer => "SetSourceName";
    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects => SourceIntents.Subjects;
    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Text("source", required: true), Field.Text("name", required: true));
    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } = SourceIntents.ExposureFor("motif_set_source_name");
    /// <inheritdoc />
    public override ReplacementRule? Replaces { get; } = SourceIntents.SameSource("name");
    /// <inheritdoc />
    public override SetSourceNameIntent Parse(JsonElement authored) =>
        new(SourceIntents.Text(authored, "source"), SourceIntents.Text(authored, "name").Trim());
    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, SetSourceNameIntent intent)
    {
        var (item, _) = SourceComposerSupport.Find(context.Cache, intent.Source);
        if (intent.Name.Length == 0) throw new ArgumentException("A Source needs a name.");
        return [SourceComposerSupport.Set(ScriptureSourceOperationKinds.SetName, item, context, "name", intent.Name)];
    }
}

/// <summary>Stages a new Context for a Source, checked in the Source's own versification.</summary>
internal sealed class SetSourceContextDefinition : ComposerDefinition<SetSourceContextIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-set-source-context";
    /// <inheritdoc />
    public override string Composer => "SetSourceContext";
    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects => SourceIntents.Subjects;
    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Text("source", required: true), Field.Text("context", required: true));
    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } = SourceIntents.ExposureFor("motif_set_source_context");
    /// <inheritdoc />
    public override ReplacementRule? Replaces { get; } = SourceIntents.SameSource("context");
    /// <inheritdoc />
    public override SetSourceContextIntent Parse(JsonElement authored) =>
        new(SourceIntents.Text(authored, "source"), SourceIntents.Text(authored, "context"));
    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, SetSourceContextIntent intent)
    {
        var (item, entry) = SourceComposerSupport.Find(context.Cache, intent.Source);
        var (_, versification) = SourceComposerSupport.Versification(entry.VersificationName);
        SourceComposerSupport.RequireContext(intent.Context, versification);
        return [SourceComposerSupport.Set(ScriptureSourceOperationKinds.SetContext, item, context, "context", intent.Context)];
    }
}

/// <summary>Stages a new versification for a Source; the Source's Context must still parse in it.</summary>
internal sealed class SetSourceVersificationDefinition : ComposerDefinition<SetSourceVersificationIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-set-source-versification";
    /// <inheritdoc />
    public override string Composer => "SetSourceVersification";
    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects => SourceIntents.Subjects;
    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Text("source", required: true), Field.Text("versification", required: true));
    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } = SourceIntents.ExposureFor("motif_set_source_versification");
    /// <inheritdoc />
    public override ReplacementRule? Replaces { get; } = SourceIntents.SameSource("versification");
    /// <inheritdoc />
    public override SetSourceVersificationIntent Parse(JsonElement authored) =>
        new(SourceIntents.Text(authored, "source"), SourceIntents.Text(authored, "versification"));
    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, SetSourceVersificationIntent intent)
    {
        var (item, entry) = SourceComposerSupport.Find(context.Cache, intent.Source);
        var (name, versification) = SourceComposerSupport.Versification(intent.Versification);
        SourceComposerSupport.RequireContext(entry.ContextText, versification);
        return [SourceComposerSupport.Set(ScriptureSourceOperationKinds.SetVersification, item, context, "versification", name)];
    }
}

/// <summary>Stages a Source as removed or active again. A Source is never deleted: it keeps its id and Context.</summary>
internal sealed class SetSourceStatusDefinition : ComposerDefinition<SetSourceStatusIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-set-source-status";
    /// <inheritdoc />
    public override string Composer => "SetSourceStatus";
    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects => SourceIntents.Subjects;
    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Text("source", required: true),
        Field.Choice("status", [ScriptureSourceFields.Active, ScriptureSourceFields.Removed], required: true));
    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } = SourceIntents.ExposureFor("motif_set_source_status");
    /// <inheritdoc />
    public override ReplacementRule? Replaces { get; } = SourceIntents.SameSource("status");
    /// <inheritdoc />
    public override SetSourceStatusIntent Parse(JsonElement authored) =>
        new(SourceIntents.Text(authored, "source"), SourceIntents.Text(authored, "status"));
    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, SetSourceStatusIntent intent)
    {
        var (item, _) = SourceComposerSupport.Find(context.Cache, intent.Source);
        return [SourceComposerSupport.Set(ScriptureSourceOperationKinds.SetStatus, item, context, "status", intent.Status)];
    }
}

internal sealed record AddSourceIntent(string Id, string Name, string Versification, string Context, string Kind);
internal sealed record SetSourceNameIntent(string Source, string Name);
internal sealed record SetSourceContextIntent(string Source, string Context);
internal sealed record SetSourceVersificationIntent(string Source, string Versification);
internal sealed record SetSourceStatusIntent(string Source, string Status);
