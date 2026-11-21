using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>A new sense for an existing entry, under one of the entry's grammatical analyses.</summary>
/// <param name="Entry">The existing entry that gains the sense.</param>
/// <param name="Gloss">The new sense's gloss.</param>
/// <param name="AnalysisWritingSystem">The analysis writing system tag of the gloss and definition.</param>
/// <param name="Definition">The new sense's optional definition.</param>
/// <param name="Analysis">
/// The entry's analysis the sense points at. Absent means the entry's only analysis, and an entry with several
/// needs one named.
/// </param>
public sealed record AddSenseIntent(
    CanonicalId Entry, string Gloss, string AnalysisWritingSystem, string? Definition = null, CanonicalId? Analysis = null);

/// <summary>Parses the closed JSON intent for adding a sense to an entry.</summary>
public static class AddSenseIntentParser
{
    private const string ConstructName = "AddSense";

    /// <summary>Reads the entry, the gloss and its writing system; the definition and the analysis are optional.</summary>
    public static AddSenseIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, ConstructName, "entry", "gloss", "analysisWs", "definition", "analysis");
        return new AddSenseIntent(SoundSystemIntentParsing.Id(authored, "entry"),
            SoundSystemIntentParsing.Text(authored, "gloss"), SoundSystemIntentParsing.Text(authored, "analysisWs"),
            authored.TryGetProperty("definition", out _) ? SoundSystemIntentParsing.Text(authored, "definition") : null,
            authored.TryGetProperty("analysis", out _) ? SoundSystemIntentParsing.Id(authored, "analysis") : null);
    }
}

/// <summary>
/// Lowers an <see cref="AddSenseIntent"/> into a new sense of an existing entry, with its gloss, optional definition
/// and a pointer to one of the entry's analyses. The entry's analyses and alternate forms stay shared, so a second
/// meaning does not need a homograph entry.
/// </summary>
public static class AddSenseComposer
{
    private const string ConstructName = "AddSense";
    private const string Rationale = "Authored by the AddSense composer: the entry has one more meaning.";

    /// <summary>Builds the operations without mutating the caller's project.</summary>
    /// <param name="cache">The project to resolve <paramref name="intent"/>'s references against.</param>
    /// <param name="intent">The authored sense.</param>
    /// <param name="mintId">Overrides id minting; the Draft supplies one so ids stay unique within it.</param>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, AddSenseIntent intent, Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var entryId = LexicalFormAuthoring.Normalize(intent.Entry);
        var entry = LexicalFormAuthoring.Resolve<ILexEntry>(cache, entryId, ConstructName);
        var gloss = LexicalFormAuthoring.RequireText(intent.Gloss, "gloss", ConstructName);
        var definition = intent.Definition is null
            ? null : LexicalFormAuthoring.RequireText(intent.Definition, "definition", ConstructName);
        LexicalFormAuthoring.RequireWritingSystem(cache, intent.AnalysisWritingSystem, vernacular: false, ConstructName);
        var msa = ChooseAnalysis(cache, entry, entryId, intent, gloss);
        var ws = cache.WritingSystemFactory.GetWsFromStr(intent.AnalysisWritingSystem);
        var same = entry.AllSenses.FirstOrDefault(sense => sense.Gloss.get_String(ws).Text == gloss && sense.MorphoSyntaxAnalysisRA?.Guid == msa.Guid);
        if (same is not null)
            throw new InvalidOperationException(
                $"'{ConstructName}': entry '{entryId.Value}' already has a sense glossed '{gloss}' under that analysis " +
                $"('{CanonicalId.FromGuid(same.Guid).Value}'). To change that sense, use compose-edit-gloss or " +
                "compose-edit-definition on it; to point it at another analysis, use compose-set-sense-msa.");

        var source = mintId ?? (() => CanonicalId.Mint());
        CanonicalId Mint() => LexicalFormAuthoring.Normalize(source());
        var operations = new List<OperationEnvelope>();
        var senseId = Mint();
        var last = entry.SensesOS.LastOrDefault();
        var create = Add(LexEntrySensesOperationKinds.Create, entryId, new { }, entityId: senseId,
            placement: last is null ? null : new Placement(CanonicalId.FromGuid(last.Guid), null));
        var afterSense = new[] { new OperationDependency(create) };
        Add(LexicalSenseOperationKinds.SetGloss, senseId,
            new { ws = intent.AnalysisWritingSystem, text = gloss }, dependsOn: afterSense);
        if (definition is not null)
            Add(LexSenseDefinitionOperationKinds.SetDefinition, senseId,
                new { ws = intent.AnalysisWritingSystem, text = definition }, dependsOn: afterSense);
        Add(LexSenseMorphoSyntaxAnalysisOperationKinds.SetMorphoSyntaxAnalysis, senseId,
            new { @ref = CanonicalId.FromGuid(msa.Guid).Value }, dependsOn: afterSense);
        return operations;

        CanonicalId Add(string kind, CanonicalId target, object after, CanonicalId? entityId = null,
            IReadOnlyList<OperationDependency>? dependsOn = null, Placement? placement = null)
        {
            var operationId = Mint();
            operations.Add(new OperationEnvelope(operationId, kind, entityId: entityId, target: target,
                placement: placement, after: JsonSerializer.SerializeToElement(after), dependsOn: dependsOn,
                rationale: Rationale));
            return operationId;
        }
    }

    private static IMoMorphSynAnalysis ChooseAnalysis(
        LcmCache cache, ILexEntry entry, CanonicalId entryId, AddSenseIntent intent, string gloss)
    {
        var analyses = entry.MorphoSyntaxAnalysesOC.ToArray();
        string Retry(IMoMorphSynAnalysis item) =>
            $"compose-add-sense with {{\"entry\":\"{entryId.Value}\",\"gloss\":\"{gloss}\"," +
            $"\"analysisWs\":\"{intent.AnalysisWritingSystem}\",\"analysis\":\"{CanonicalId.FromGuid(item.Guid).Value}\"}}";
        if (intent.Analysis is { } named)
        {
            var found = analyses.FirstOrDefault(item => item.Guid == named.ToGuid());
            if (found is not null) return found;
            throw new InvalidOperationException(
                $"'{ConstructName}': '{named.Value}' is not an analysis of entry '{entryId.Value}'." +
                (analyses.Length == 0 ? NoAnalysisAdvice(entryId) : " Its analyses are " + Listing(analyses) +
                    ". For a different one, use compose-add-morphosyntax-analysis with 'gloss'."));
        }
        if (analyses.Length == 1) return analyses[0];
        if (analyses.Length == 0)
            throw new InvalidOperationException($"'{ConstructName}': entry '{entryId.Value}' has no analysis." + NoAnalysisAdvice(entryId));
        throw new InvalidOperationException(
            $"'{ConstructName}': entry '{entryId.Value}' has {analyses.Length} analyses, so say which one the sense " +
            $"uses with 'analysis'. They are {Listing(analyses)}. For example, {Retry(analyses[0])}.");
    }

    private static string NoAnalysisAdvice(CanonicalId entryId) =>
        $" Use compose-add-morphosyntax-analysis with {{\"entry\":\"{entryId.Value}\",\"msa\":{{...}},\"gloss\":\"...\"," +
        "\"analysisWs\":\"...\"} to give it an analysis and the sense together.";

    /// <summary>Names each analysis by id, kind and category, for a refusal that asks the caller to choose.</summary>
    internal static string Listing(IEnumerable<IMoMorphSynAnalysis> analyses) =>
        string.Join("; ", analyses.Select(item => $"'{CanonicalId.FromGuid(item.Guid).Value}' ({Describe(item)})"));

    private static string Describe(IMoMorphSynAnalysis item)
    {
        var (kind, category) = item switch
        {
            IMoStemMsa stem => ("stem", stem.PartOfSpeechRA),
            IMoInflAffMsa infl => ("inflectional", infl.PartOfSpeechRA),
            IMoDerivAffMsa deriv => ("derivational", deriv.FromPartOfSpeechRA),
            IMoUnclassifiedAffixMsa plain => ("unclassified", plain.PartOfSpeechRA),
            _ => (item.ClassName, null),
        };
        var name = category?.Name.BestAnalysisAlternative.Text;
        return string.IsNullOrEmpty(name) ? kind : $"{kind}, {name}";
    }
}
