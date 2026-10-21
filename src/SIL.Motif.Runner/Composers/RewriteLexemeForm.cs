using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>One entry whose lexeme form is rewritten in one writing system.</summary>
/// <param name="Entry">The lexical entry that owns the lexeme form.</param>
/// <param name="WritingSystem">The vernacular writing system of the alternative to rewrite.</param>
/// <param name="ExpectedForm">The form the entry holds now; a different current form fails the whole batch.</param>
/// <param name="NewForm">The form to write; null only when the batch folds stress marks.</param>
public sealed record LexemeFormRewrite(CanonicalId Entry, string WritingSystem, string ExpectedForm, string? NewForm);

/// <summary>A batch of lexeme form rewrites.</summary>
/// <param name="Rewrites">The entries to rewrite, each at most once per writing system.</param>
/// <param name="SetEmptyCitationForm">When true, an entry whose citation form is empty in that writing system gets the old lexeme form as its citation form.</param>
/// <param name="FoldStressMarks">When true, each new form is computed by LexemeFormFolding.Fold and no rewrite may carry one.</param>
public sealed record RewriteLexemeFormIntent(
    IReadOnlyList<LexemeFormRewrite> Rewrites, bool SetEmptyCitationForm, bool FoldStressMarks);

/// <summary>
/// The explicit, deterministic fold: decompose to NFD, drop only the stress diacritics, recompose to NFC.
/// Stripped code points: U+0300 COMBINING GRAVE ACCENT, U+0301 COMBINING ACUTE ACCENT, U+0302 COMBINING
/// CIRCUMFLEX ACCENT, U+0304 COMBINING MACRON and U+030C COMBINING CARON. Every other mark, such as the tilde U+0303
/// of a Cebuano n-tilde, is kept. In Wolff's Cebuano orthography the grave marks a final glottal stop and the acute
/// marks length, so the marks carry meaning; that is why the old spelling is worth keeping as the citation form.
/// </summary>
public static class LexemeFormFolding
{
    /// <summary>The combining code points Fold removes.</summary>
    public static readonly IReadOnlyList<int> StrippedCodePoints = [0x0300, 0x0301, 0x0302, 0x0304, 0x030C];

    /// <summary>Returns the form without stress diacritics, in NFC.</summary>
    public static string Fold(string form)
    {
        ArgumentNullException.ThrowIfNull(form);
        var decomposed = form.Normalize(NormalizationForm.FormD);
        var kept = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
            if (!StrippedCodePoints.Contains(ch)) kept.Append(ch);
        return kept.ToString().Normalize(NormalizationForm.FormC);
    }
}

/// <summary>Parses a closed batch of lexeme form rewrites.</summary>
public static class RewriteLexemeFormIntentParser
{
    private const string Construct = "RewriteLexemeForm";

    public static RewriteLexemeFormIntent Parse(JsonElement input)
    {
        SoundSystemIntentParsing.Object(input, Construct, "entries", "setEmptyCitationForm", "foldStressMarks");
        var setCitation = OptionalFlag(input, "setEmptyCitationForm");
        var fold = OptionalFlag(input, "foldStressMarks");
        var rewrites = new List<LexemeFormRewrite>();
        foreach (var item in SoundSystemIntentParsing.Array(input, "entries"))
        {
            SoundSystemIntentParsing.Object(item, "entries item", "entryGuid", "writingSystem", "expectedForm", "newForm");
            string? newForm = null;
            if (item.TryGetProperty("newForm", out _)) newForm = SoundSystemIntentParsing.Text(item, "newForm");
            if (fold && newForm is not null)
                throw new ContractParseException($"'{Construct}': 'newForm' must be omitted when 'foldStressMarks' is true.");
            if (!fold && newForm is null)
                throw new ContractParseException($"'{Construct}': 'newForm' is required unless 'foldStressMarks' is true.");
            rewrites.Add(new(LexicalFormAuthoring.Normalize(SoundSystemIntentParsing.Id(item, "entryGuid")),
                SoundSystemIntentParsing.Text(item, "writingSystem"),
                SoundSystemIntentParsing.Text(item, "expectedForm"), newForm));
        }
        if (rewrites.Count == 0) throw new ContractParseException($"'{Construct}': 'entries' must not be empty.");
        return new(rewrites, setCitation, fold);
    }

    private static bool OptionalFlag(JsonElement input, string name)
    {
        if (!input.TryGetProperty(name, out var value)) return false;
        return value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw new ContractParseException($"'{Construct}': '{name}' must be a Boolean.");
    }
}

/// <summary>
/// Rewrites existing entries' lexeme forms with the existing <c>grammar/moForm/setForm</c> operation, and may keep
/// the old spelling as a citation form with <c>lexical/lexEntry/setCitationForm</c>. One pass, linear in the batch:
/// every entry is checked against its current form before any operation is returned, so one stale form fails all.
/// </summary>
public static class RewriteLexemeFormComposer
{
    private const string Construct = "RewriteLexemeForm";

    /// <summary>The rationale each operation carries; a Draft still needs its own Rationale to finalize.</summary>
    public const string OperationRationale =
        "Authored by the RewriteLexemeForm composer: the lexeme form is rewritten so the parser can match it; " +
        "any citation form set keeps the earlier spelling.";

    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, RewriteLexemeFormIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var checkedSystems = new Dictionary<string, int>(StringComparer.Ordinal);
        var seen = new HashSet<(CanonicalId, string)>();
        var operations = new List<OperationEnvelope>();
        foreach (var rewrite in intent.Rewrites)
        {
            if (!checkedSystems.TryGetValue(rewrite.WritingSystem, out var ws))
            {
                LexicalFormAuthoring.RequireWritingSystem(cache, rewrite.WritingSystem, vernacular: true, Construct);
                ws = checkedSystems[rewrite.WritingSystem] = cache.WritingSystemFactory.GetWsFromStr(rewrite.WritingSystem);
            }
            var entryId = rewrite.Entry.Value;
            if (!seen.Add((rewrite.Entry, rewrite.WritingSystem)))
                throw new InvalidOperationException($"'{Construct}': entry '{entryId}' appears twice for '{rewrite.WritingSystem}'.");
            var entry = LexicalFormAuthoring.Resolve<ICmObject>(cache, rewrite.Entry, Construct) as ILexEntry
                ?? throw new InvalidOperationException($"'{Construct}': '{entryId}' is not a lexical entry.");
            var form = entry.LexemeFormOA
                ?? throw new InvalidOperationException($"'{Construct}': entry '{entryId}' has no lexeme form.");
            var current = form.Form.get_String(ws).Text ?? string.Empty;
            if (!Same(current, rewrite.ExpectedForm))
                throw new InvalidOperationException(
                    $"'{Construct}': entry '{entryId}' holds lexeme form '{current}' in '{rewrite.WritingSystem}', " +
                    $"not the expected '{rewrite.ExpectedForm}'.");
            var newForm = intent.FoldStressMarks ? LexemeFormFolding.Fold(rewrite.ExpectedForm) : rewrite.NewForm!;
            if (string.IsNullOrWhiteSpace(newForm))
                throw new InvalidOperationException($"'{Construct}': the new form for entry '{entryId}' must not be blank.");
            if (Same(current, newForm)) continue;
            if (intent.SetEmptyCitationForm && string.IsNullOrEmpty(entry.CitationForm.get_String(ws).Text))
                operations.Add(new(CanonicalId.Mint(), LexEntryCitationFormOperationKinds.SetCitationForm,
                    target: rewrite.Entry,
                    after: JsonSerializer.SerializeToElement(new { ws = rewrite.WritingSystem, text = current.Normalize(NormalizationForm.FormC) }),
                    rationale: OperationRationale));
            operations.Add(new(CanonicalId.Mint(), MoFormFormOperationKinds.SetForm,
                target: CanonicalId.FromGuid(form.Guid),
                after: JsonSerializer.SerializeToElement(new { ws = rewrite.WritingSystem, text = newForm.Normalize(NormalizationForm.FormC) }),
                rationale: OperationRationale));
        }
        return operations;
    }

    private static bool Same(string a, string b) =>
        string.Equals(a.Normalize(NormalizationForm.FormD), b.Normalize(NormalizationForm.FormD), StringComparison.Ordinal);
}
