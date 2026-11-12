using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>
/// Deletes one item of the Exception Features list that nothing uses. FieldWorks path: Grammar, Exception Features,
/// select the item, Delete (the Lists editor for <c>MorphologicalData.ProdRestrictOA</c>, LibLCM <c>ICmObject.Delete</c>).
/// </summary>
/// <param name="Feature">Canonical id of the exception feature.</param>
/// <param name="ExpectedName">The name the feature must still carry, in any writing system.</param>
public sealed record DeleteExceptionFeatureIntent(CanonicalId Feature, string ExpectedName);

/// <summary>
/// Deletes one phonological environment that nothing uses. FieldWorks path: Grammar, Phonology, Environments,
/// select the environment, Delete (<c>IPhPhonData.EnvironmentsOS</c>, LibLCM <c>ICmObject.Delete</c>).
/// </summary>
/// <param name="Environment">Canonical id of the environment.</param>
/// <param name="ExpectedText">The environment's current string such as <c>/ _ [C]</c>, or its name.</param>
public sealed record DeleteEnvironmentIntent(CanonicalId Environment, string ExpectedText);

/// <summary>
/// Deletes one natural class that nothing uses. FieldWorks path: Grammar, Phonology, Natural Classes, select the class,
/// Delete (<c>IPhPhonData.NaturalClassesOS</c>, LibLCM <c>ICmObject.Delete</c>).
/// </summary>
/// <param name="NaturalClass">Canonical id of the natural class.</param>
/// <param name="ExpectedName">The name the class must still carry, in any writing system.</param>
public sealed record DeleteNaturalClassIntent(CanonicalId NaturalClass, string ExpectedName);

/// <summary>
/// Deletes one Note of a Text Segment. FieldWorks path: Texts &amp; Words, Interlinear Text, the Free Translation and
/// Note lines of a segment, clear the Note (<c>ISegment.NotesOS</c>, the objects <c>compose-edit-text-notes</c> sets).
/// </summary>
/// <param name="Note">Canonical id of the Note.</param>
/// <param name="ExpectedText">The Note's current text, in any writing system.</param>
public sealed record DeleteTextSegmentNoteIntent(CanonicalId Note, string ExpectedText);

/// <summary>Parses the closed JSON intents for the unused-object deletions.</summary>
public static class DeleteUnusedObjectsIntentParser
{
    /// <summary>Reads <c>{ "feature", "expectedName" }</c>.</summary>
    public static DeleteExceptionFeatureIntent ParseExceptionFeature(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "DeleteExceptionFeature", "feature", "expectedName");
        return new(SoundSystemIntentParsing.Id(authored, "feature"), SoundSystemIntentParsing.Text(authored, "expectedName"));
    }

    /// <summary>Reads <c>{ "environment", "expectedText" }</c>.</summary>
    public static DeleteEnvironmentIntent ParseEnvironment(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "DeleteEnvironment", "environment", "expectedText");
        return new(SoundSystemIntentParsing.Id(authored, "environment"),
            SoundSystemIntentParsing.Text(authored, "expectedText"));
    }

    /// <summary>Reads <c>{ "naturalClass", "expectedName" }</c>.</summary>
    public static DeleteNaturalClassIntent ParseNaturalClass(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "DeleteNaturalClass", "naturalClass", "expectedName");
        return new(SoundSystemIntentParsing.Id(authored, "naturalClass"), SoundSystemIntentParsing.Text(authored, "expectedName"));
    }

    /// <summary>Reads <c>{ "note", "expectedText" }</c>.</summary>
    public static DeleteTextSegmentNoteIntent ParseTextSegmentNote(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "DeleteTextSegmentNote", "note", "expectedText");
        return new(SoundSystemIntentParsing.Id(authored, "note"), SoundSystemIntentParsing.Text(authored, "expectedText"));
    }
}

/// <summary>
/// Builds the deletions after a stale-name guard and a check that nothing refers to the object. A refusal names
/// each referrer. Nothing is reassigned or cascaded.
/// </summary>
public static class DeleteUnusedObjectsComposer
{
    private static OperationEnvelope Delete(string kind, CanonicalId target, string construct) =>
        new(CanonicalId.Mint(), kind, target: target, after: JsonSerializer.SerializeToElement(new { }),
            rationale: $"Authored by the {construct} composer: nothing refers to the object.");

    private static void RequireMatch(string construct, CanonicalId id, string expected, IEnumerable<string?> actual, string what)
    {
        var values = actual.Where(text => !string.IsNullOrEmpty(text)).Select(text => text!).ToArray();
        if (values.Contains(expected, StringComparer.Ordinal)) return;
        throw new InvalidOperationException(
            $"'{construct}': '{id.Value}' has {what} {(values.Length == 0 ? "(empty)" : string.Join(" / ", values.Select(v => $"'{v}'")))}, not '{expected}'.");
    }

    private static IEnumerable<string?> Alternatives(IMultiAccessorBase field) =>
        field.AvailableWritingSystemIds.Select(ws => field.get_String(ws).Text);

    /// <summary>Returns the deletion, or throws on a stale name or an exception feature something still uses.</summary>
    public static IReadOnlyList<OperationEnvelope> BuildExceptionFeature(LcmCache cache, DeleteExceptionFeatureIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        const string construct = "DeleteExceptionFeature";
        var feature = ReferenceFieldLowering.Resolve<ICmPossibility>(cache, intent.Feature, construct);
        RequireMatch(construct, intent.Feature, intent.ExpectedName, Alternatives(feature.Name), "the name");
        UnusedObjects.RequireUnusedExceptionFeature(cache, feature, intent.Feature, construct);
        return [Delete(UnusedObjectDeleteOperationKinds.DeleteExceptionFeature, intent.Feature, construct)];
    }

    /// <summary>Returns the deletion, or throws on a stale string or an environment something still uses.</summary>
    public static IReadOnlyList<OperationEnvelope> BuildEnvironment(LcmCache cache, DeleteEnvironmentIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        const string construct = "DeleteEnvironment";
        var environment = ReferenceFieldLowering.Resolve<IPhEnvironment>(cache, intent.Environment, construct);
        RequireMatch(construct, intent.Environment, intent.ExpectedText,
            Alternatives(environment.Name).Append(environment.StringRepresentation?.Text), "the name or string");
        UnusedObjects.RequireUnusedEnvironment(environment, intent.Environment, construct);
        return [Delete(UnusedObjectDeleteOperationKinds.DeleteEnvironment, intent.Environment, construct)];
    }

    /// <summary>Returns the deletion, or throws on a stale name or a natural class something still uses.</summary>
    public static IReadOnlyList<OperationEnvelope> BuildNaturalClass(LcmCache cache, DeleteNaturalClassIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        const string construct = "DeleteNaturalClass";
        var naturalClass = ReferenceFieldLowering.Resolve<IPhNaturalClass>(cache, intent.NaturalClass, construct);
        RequireMatch(construct, intent.NaturalClass, intent.ExpectedName, Alternatives(naturalClass.Name), "the name");
        UnusedObjects.RequireUnusedNaturalClass(cache, naturalClass, intent.NaturalClass, construct);
        return [Delete(UnusedObjectDeleteOperationKinds.DeleteNaturalClass, intent.NaturalClass, construct)];
    }

    /// <summary>Returns the deletion, or throws on a stale text or a target that is not a Segment's Note.</summary>
    public static IReadOnlyList<OperationEnvelope> BuildTextSegmentNote(LcmCache cache, DeleteTextSegmentNoteIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        const string construct = "DeleteTextSegmentNote";
        var note = ReferenceFieldLowering.Resolve<INote>(cache, intent.Note, construct);
        UnusedObjects.RequireSegmentNote(note, intent.Note, construct);
        RequireMatch(construct, intent.Note, intent.ExpectedText, Alternatives(note.Content), "the text");
        return [Delete(UnusedObjectDeleteOperationKinds.DeleteTextSegmentNote, intent.Note, construct)];
    }
}
