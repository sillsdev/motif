using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Responses;

/// <summary>The identity of the response shape, so a reader can refuse a version it does not know.</summary>
public static class FormsExplainSchema
{
    /// <summary>The schema id every <see cref="FormsExplainResponse"/> carries.</summary>
    public const string Id = "motif.forms-explain.v1";
}

/// <summary>One morpheme of a context, named by its FieldWorks identity and the form the project shows for it.</summary>
/// <param name="EntryGuid">The lexical entry's GUID, lowercase.</param>
/// <param name="MsaGuid">The morphosyntactic analysis's GUID, lowercase.</param>
/// <param name="MsaClass">The analysis's LibLCM class, such as <c>MoStemMsa</c>.</param>
/// <param name="Form">The entry's headword or lexeme form, or null when the project gave none.</param>
public sealed record FormsExplainMorpheme(string EntryGuid, string MsaGuid, string MsaClass, string? Form);

/// <summary>The rule PanGloss applied in one step.</summary>
/// <param name="Kind">The rule's kind, such as <c>phon</c> or <c>morph</c>.</param>
/// <param name="Name">The rule's name, or null.</param>
/// <param name="Id">The <c>grammar.xml</c> id, or null.</param>
/// <param name="Guid">The FieldWorks guid, or null.</param>
public sealed record FormsExplainRule(string Kind, string? Name, string? Id, string? Guid);

/// <summary>One step of a derivation: the stratum, the rule, and the word before and after it.</summary>
/// <param name="Stratum">The stratum the rule ran in.</param>
/// <param name="Rule">The rule applied.</param>
/// <param name="Subrule">The 0-based subrule of the rule, when it has several.</param>
/// <param name="Input">The shape before the step; <c>+</c> marks a morpheme boundary.</param>
/// <param name="Output">The shape after the step.</param>
public sealed record FormsExplainStep(string Stratum, FormsExplainRule Rule, int? Subrule, string Input, string Output);

/// <summary>One allomorph the generator chose for a morpheme.</summary>
/// <param name="Role">The morpheme's role in the request, <c>root</c> or an affix kind.</param>
/// <param name="Shape">The allomorph's written shape.</param>
/// <param name="AllomorphId">PanGloss's id for the allomorph, or null.</param>
/// <param name="EntryGuid">The entry's GUID, or null.</param>
/// <param name="MsaGuid">The analysis's GUID, or null.</param>
/// <param name="FormGuid">The allomorph's FieldWorks GUID, or null.</param>
public sealed record FormsExplainAllomorph(
    string? Role, string Shape, int? AllomorphId, string? EntryGuid, string? MsaGuid, string? FormGuid);

/// <summary>One surface form reached by one choice of allomorphs.</summary>
/// <param name="Surface">The generated word.</param>
/// <param name="Seed">PanGloss's key for the allomorph choice that reached it, such as <c>r0/p0</c>.</param>
/// <param name="Allomorphs">The allomorph chosen for the root and then for each requested morpheme, in request order.</param>
/// <param name="Derivations">How many derivations reached this surface under this choice.</param>
/// <param name="Steps">The rules that fired, in application order. Empty when several derivations reached the surface
/// (see <paramref name="Alternatives"/>), when PanGloss could not prove one (see <paramref name="StepsUnresolved"/>), or when it kept no trace.</param>
/// <param name="StepsUnresolved">True when PanGloss could not prove which rules made this surface and so gave none; never a guess.</param>
/// <param name="Alternatives">Each distinct derivation that reached the surface, as its own step list, when there was more than one.</param>
/// <param name="TraceTruncated">Whether the trace cap cut this choice's steps short.</param>
public sealed record FormsExplainSurface(
    string Surface, string Seed, IReadOnlyList<FormsExplainAllomorph> Allomorphs, int Derivations,
    IReadOnlyList<FormsExplainStep> Steps, bool StepsUnresolved, IReadOnlyList<IReadOnlyList<FormsExplainStep>> Alternatives,
    bool TraceTruncated);

/// <summary>One set of morphemes generated together and everything PanGloss produced for it.</summary>
/// <param name="Morphemes">The root, then the morphemes added to it, in request order.</param>
/// <param name="Surfaces">The surfaces, sorted by surface and then by seed.</param>
/// <param name="Complete">Whether PanGloss finished this context without a cap.</param>
/// <param name="StopReason">Why it stopped early, such as <c>work_limit</c>, or null when complete.</param>
public sealed record FormsExplainContext(
    IReadOnlyList<FormsExplainMorpheme> Morphemes, IReadOnlyList<FormsExplainSurface> Surfaces, bool Complete,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? StopReason);

/// <summary>An analysis of the word that could not be explained, and why.</summary>
/// <param name="Reading">The analysis as a person reads it: its forms in order, such as <c>kaon -an</c>.</param>
/// <param name="Reason">One sentence saying what could not be named.</param>
public sealed record FormsExplainSkipped(string Reading, string Reason);

/// <summary>
/// Every form PanGloss generates for a morpheme in one or more contexts, with the rules that fired to make each one.
/// <see cref="Complete"/> is false when any context stopped early, so a missing form is never read as an impossible one.
/// </summary>
/// <param name="Schema"><see cref="FormsExplainSchema.Id"/>.</param>
/// <param name="Morpheme">The root as the caller named it, or null when a word was explained.</param>
/// <param name="Word">The word whose analyses were explained, or null when a morpheme was.</param>
/// <param name="Contexts">The contexts, in the order asked; for a word, sorted by their morpheme ids.</param>
/// <param name="Complete">True only when every context is complete.</param>
/// <param name="StopReason">The first context's stop reason that is not null, or null when complete.</param>
/// <param name="GrammarHash">The SHA-256 of the grammar file PanGloss generated from, as <c>sha256:</c> and 64 hex digits.</param>
/// <param name="PanglossVersion">The PanGloss version that generated, or <c>unknown</c>.</param>
/// <param name="TracedAgainst"><c>baseline</c>, or <c>proposal &lt;id&gt;</c>.</param>
/// <param name="Skipped">Analyses of the word that could not be explained; omitted when none.</param>
public sealed record FormsExplainResponse(
    string Schema, string? Morpheme, string? Word, IReadOnlyList<FormsExplainContext> Contexts, bool Complete,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? StopReason, string GrammarHash,
    string PanglossVersion, string TracedAgainst, IReadOnlyList<FormsExplainSkipped>? Skipped = null);
