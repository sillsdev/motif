using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Edits one writing-system alternative of an ordinary alternate form in its existing entry.</summary>
/// <param name="Entry">The expected owning entry; the target must still belong to its alternate forms.</param>
/// <param name="Target">The existing stem or affix allomorph; a lexeme form uses RewriteLexemeForm instead.</param>
/// <param name="WritingSystem">The project's vernacular writing system to edit.</param>
/// <param name="ExpectedForm">The current text required for the edit, compared by canonical Unicode equivalence.</param>
/// <param name="NewForm">The replacement without folding phonological distinctions, or empty to clear.</param>
public sealed record EditAllomorphFormIntent(CanonicalId Entry, CanonicalId Target, string WritingSystem,
    string ExpectedForm, string NewForm);

/// <summary>Parses a closed alternate-form edit with an explicit owner and expected text.</summary>
public static class EditAllomorphFormIntentParser
{
    public static EditAllomorphFormIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "EditAllomorphForm", "entry", "target", "writingSystem",
            "expectedForm", "newForm");
        return new(SoundSystemIntentParsing.Id(authored, "entry"), SoundSystemIntentParsing.Id(authored, "target"),
            SoundSystemIntentParsing.Text(authored, "writingSystem"), SoundSystemIntentParsing.Text(authored, "expectedForm"),
            SoundSystemIntentParsing.Text(authored, "newForm"));
    }
}

/// <summary>Preserves the selected alternate's identity and conditions by emitting only a Form write.</summary>
public static class EditAllomorphFormComposer
{
    private const string Construct = "EditAllomorphForm";

    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, EditAllomorphFormIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var entry = LexicalFormAuthoring.Resolve<ILexEntry>(cache, intent.Entry, Construct);
        var target = LexicalFormAuthoring.Resolve<ICmObject>(cache, intent.Target, Construct);
        if (target is not (IMoStemAllomorph or IMoAffixAllomorph))
            throw new InvalidOperationException($"'{Construct}': target must be an ordinary stem or affix allomorph; " +
                "an affix process requires a pattern edit.");
        if (!entry.AlternateFormsOS.Any(form => form.Guid == target.Guid))
            throw new InvalidOperationException($"'{Construct}': target is not an alternate form of the expected entry; " +
                "use compose-rewrite-lexeme-form for its lexeme form.");
        var form = (IMoForm)target;
        return GuardedFormTextEdit.Build(cache, form.Form, intent.WritingSystem, intent.ExpectedForm, intent.NewForm,
            CanonicalId.FromGuid(form.Guid), MoFormFormOperationKinds.SetForm, MoFormFormOperationKinds.ClearForm, Construct);
    }
}

/// <summary>Validates one vernacular text alternative before producing a single write with no cache mutation.</summary>
internal static class GuardedFormTextEdit
{
    internal static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, SIL.LCModel.Core.KernelInterfaces.ITsMultiString text,
        string writingSystem, string expected, string desired, CanonicalId target, string setKind, string clearKind, string construct)
    {
        LexicalFormAuthoring.RequireWritingSystem(cache, writingSystem, vernacular: true, construct);
        ArgumentNullException.ThrowIfNull(desired);
        if (desired.Length > 0 && string.IsNullOrWhiteSpace(desired))
            throw new InvalidOperationException($"'{construct}': 'newForm' must be empty or contain non-whitespace text.");
        ArgumentNullException.ThrowIfNull(expected);
        var ws = cache.WritingSystemFactory.GetWsFromStr(writingSystem);
        var current = text.get_String(ws).Text ?? string.Empty;
        if (!Same(current, expected))
            throw new InvalidOperationException($"'{construct}': current form '{current}' in '{writingSystem}' " +
                $"differs from expectedForm '{expected}'; read the current form before editing.");
        if (Same(current, desired)) return [];
        var after = desired.Length == 0
            ? JsonSerializer.SerializeToElement(new { ws = writingSystem })
            : JsonSerializer.SerializeToElement(new { ws = writingSystem, text = desired.Normalize(NormalizationForm.FormC) });
        return [new(CanonicalId.Mint(), desired.Length == 0 ? clearKind : setKind, target: target, after: after,
            rationale: $"Authored by the {construct} composer.")];
    }

    private static bool Same(string left, string right) => string.Equals(left.Normalize(NormalizationForm.FormD),
        right.Normalize(NormalizationForm.FormD), StringComparison.Ordinal);
}
