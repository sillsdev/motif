using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;

namespace SIL.Motif.Commands;

/// <summary>Deletes an Exception Features item that nothing uses.</summary>
internal sealed class DeleteExceptionFeatureDefinition : ComposerDefinition<DeleteExceptionFeatureIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-delete-exception-feature";

    /// <inheritdoc />
    public override string Composer => "DeleteExceptionFeature";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["inflection-classes"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("feature", IdRole.Target, required: true),
        Field.Text("expectedName", required: true));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_delete_exception_feature", AgentTool: false);

    /// <inheritdoc />
    public override DeleteExceptionFeatureIntent Parse(JsonElement authored) => DeleteUnusedObjectsIntentParser.ParseExceptionFeature(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, DeleteExceptionFeatureIntent intent) =>
        DeleteUnusedObjectsComposer.BuildExceptionFeature(context.Cache, intent);
}

/// <summary>Deletes a phonological environment that nothing uses.</summary>
internal sealed class DeleteEnvironmentDefinition : ComposerDefinition<DeleteEnvironmentIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-delete-environment";

    /// <inheritdoc />
    public override string Composer => "DeleteEnvironment";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["sound-inventory"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("environment", IdRole.Target, required: true),
        Field.Text("expectedText", required: true));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_delete_environment", AgentTool: false);

    /// <inheritdoc />
    public override DeleteEnvironmentIntent Parse(JsonElement authored) => DeleteUnusedObjectsIntentParser.ParseEnvironment(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, DeleteEnvironmentIntent intent) =>
        DeleteUnusedObjectsComposer.BuildEnvironment(context.Cache, intent);
}

/// <summary>Deletes a natural class that nothing uses.</summary>
internal sealed class DeleteNaturalClassDefinition : ComposerDefinition<DeleteNaturalClassIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-delete-natural-class";

    /// <inheritdoc />
    public override string Composer => "DeleteNaturalClass";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["natural-classes"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("naturalClass", IdRole.Target, required: true),
        Field.Text("expectedName", required: true));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_delete_natural_class", AgentTool: false);

    /// <inheritdoc />
    public override DeleteNaturalClassIntent Parse(JsonElement authored) => DeleteUnusedObjectsIntentParser.ParseNaturalClass(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, DeleteNaturalClassIntent intent) =>
        DeleteUnusedObjectsComposer.BuildNaturalClass(context.Cache, intent);
}

/// <summary>Deletes one Note of a Text Segment.</summary>
internal sealed class DeleteTextSegmentNoteDefinition : ComposerDefinition<DeleteTextSegmentNoteIntent>
{
    /// <inheritdoc />
    public override string Verb => "compose-delete-text-segment-note";

    /// <inheritdoc />
    public override string Composer => "DeleteTextSegmentNote";

    /// <inheritdoc />
    public override IReadOnlyList<string> Subjects { get; } = ["texts-and-analyses"];

    /// <inheritdoc />
    public override IntentShape Shape { get; } = IntentShape.Of(
        Field.Id("note", IdRole.Target, required: true),
        Field.Text("expectedText", required: true));

    /// <inheritdoc />
    public override ComposerExposure Exposure { get; } =
        new(CommandSurface.AdvancedAi, AgentClass.Draft, ToolName: "motif_delete_text_segment_note", AgentTool: false);

    /// <inheritdoc />
    public override DeleteTextSegmentNoteIntent Parse(JsonElement authored) => DeleteUnusedObjectsIntentParser.ParseTextSegmentNote(authored);

    /// <inheritdoc />
    public override IReadOnlyList<OperationEnvelope> Lower(ComposeContext context, DeleteTextSegmentNoteIntent intent) =>
        DeleteUnusedObjectsComposer.BuildTextSegmentNote(context.Cache, intent);
}
