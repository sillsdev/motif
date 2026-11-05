using System;
using System.Collections.Generic;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Writes a selected source headword without changing the entry's parser forms.</summary>
/// <param name="Target">The existing lexical entry.</param>
/// <param name="WritingSystem">The project's vernacular writing system to edit.</param>
/// <param name="ExpectedForm">The current Citation Form, or an empty string when absent.</param>
/// <param name="NewForm">The source headword to retain, or empty to clear the alternative.</param>
public sealed record EditCitationFormIntent(CanonicalId Target, string WritingSystem, string ExpectedForm, string NewForm);

/// <summary>Parses a closed Citation Form edit, requiring an explicit expected text even when empty.</summary>
public static class EditCitationFormIntentParser
{
    public static EditCitationFormIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "EditCitationForm", "target", "writingSystem", "expectedForm", "newForm");
        return new(SoundSystemIntentParsing.Id(authored, "target"), SoundSystemIntentParsing.Text(authored, "writingSystem"),
            SoundSystemIntentParsing.Text(authored, "expectedForm"), SoundSystemIntentParsing.Text(authored, "newForm"));
    }
}

/// <summary>Changes only one Citation Form alternative after verifying its current text.</summary>
public static class EditCitationFormComposer
{
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, EditCitationFormIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var entry = LexicalFormAuthoring.Resolve<ILexEntry>(cache, intent.Target, "EditCitationForm");
        return GuardedFormTextEdit.Build(cache, entry.CitationForm, intent.WritingSystem, intent.ExpectedForm, intent.NewForm,
            CanonicalId.FromGuid(entry.Guid), LexEntryCitationFormOperationKinds.SetCitationForm,
            LexEntryCitationFormOperationKinds.ClearCitationForm, "EditCitationForm");
    }
}
