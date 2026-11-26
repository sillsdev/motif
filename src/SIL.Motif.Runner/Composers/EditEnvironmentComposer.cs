using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>
/// Rewrites the left context, the right context or both of one existing environment, keeping the object so every
/// allomorph that names it keeps its link. FieldWorks path: Grammar, Phonology, Environments, edit the environment's
/// pattern (<c>IPhEnvironment.StringRepresentation</c>).
/// </summary>
/// <param name="Environment">Canonical id of the environment.</param>
/// <param name="ExpectedText">The environment's current string such as <c>/ a _ [C]</c>, or its name; a stale-edit guard.</param>
/// <param name="Left">The new left context, or null to keep the current one.</param>
/// <param name="Right">The new right context, or null to keep the current one.</param>
public sealed record EditEnvironmentIntent(CanonicalId Environment, string ExpectedText,
    IReadOnlyList<EnvironmentContext>? Left, IReadOnlyList<EnvironmentContext>? Right);

/// <summary>Parses the closed environment edit document.</summary>
public static class EditEnvironmentIntentParser
{
    /// <summary>Parses <c>{ environment, expectedText, left?, right? }</c>.</summary>
    /// <exception cref="ContractParseException">The document has unknown fields or malformed values.</exception>
    public static EditEnvironmentIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "EditEnvironment", "environment", "expectedText", "left", "right");
        return new(SoundSystemIntentParsing.Id(authored, "environment"),
            SoundSystemIntentParsing.Text(authored, "expectedText"),
            authored.TryGetProperty("left", out _) ? AuthorEnvironmentIntentParser.Contexts(authored, "left") : null,
            authored.TryGetProperty("right", out _) ? AuthorEnvironmentIntentParser.Contexts(authored, "right") : null);
    }
}

/// <summary>
/// Composes one environment string rewrite. A side the intent leaves out is read back from the stored string and must
/// round-trip exactly, so an edit never silently rewrites the side it did not name. Each refusal begins with a stable name.
/// </summary>
public static class EditEnvironmentComposer
{
    /// <summary>Refusal name: the intent names neither a left nor a right context.</summary>
    public const string Empty = "environment-edit-empty";

    /// <summary>Refusal name: the target is not an environment of the phonological data.</summary>
    public const string NotFound = "environment-edit-not-found";

    /// <summary>Refusal name: the environment no longer carries the expected string or name.</summary>
    public const string Stale = "environment-edit-stale";

    /// <summary>Refusal name: a context names an unknown phoneme or natural class, or cannot condition an allomorph.</summary>
    public const string Invalid = "environment-edit-invalid";

    /// <summary>Refusal name: the side kept as it is does not parse back into contexts; give both sides.</summary>
    public const string Unparseable = "environment-edit-unparseable";

    /// <summary>Refusal name: the new string equals the current one.</summary>
    public const string Unchanged = "environment-edit-unchanged";

    /// <summary>Returns the one string rewrite, or throws a refusal that begins with its stable name.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, EditEnvironmentIntent intent, Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        if (intent.Left is null && intent.Right is null)
            throw new InvalidOperationException($"{Empty}: name a left context, a right context or both.");
        var environment = Resolve(cache, intent.Environment);
        var current = environment.StringRepresentation?.Text;
        var names = environment.Name.AvailableWritingSystemIds.Select(ws => environment.Name.get_String(ws).Text);
        if (!names.Append(current).Any(text => string.Equals(text, intent.ExpectedText, StringComparison.Ordinal)))
            throw new InvalidOperationException(
                $"{Stale}: environment '{intent.Environment.Value}' has {(string.IsNullOrEmpty(current) ? "no string" : $"the string '{current}'")}, " +
                $"not '{intent.ExpectedText}'.");

        var (keptLeft, keptRight) = intent.Left is not null && intent.Right is not null
            ? (null, null)
            : ReadSides(cache, current, needLeft: intent.Left is null, needRight: intent.Right is null);
        var left = intent.Left ?? keptLeft!;
        var right = intent.Right ?? keptRight!;
        string pattern;
        try
        {
            if (left.Count + right.Count == 0)
                throw new InvalidOperationException("An environment needs at least one left or right context.");
            AuthorEnvironmentComposer.RequireWordBoundaryOnly(left);
            AuthorEnvironmentComposer.RequireWordBoundaryOnly(right);
            pattern = AuthorEnvironmentComposer.RenderPattern(cache, left, right);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Collections.Generic.KeyNotFoundException)
        {
            throw new InvalidOperationException($"{Invalid}: {exception.Message}");
        }
        if (string.Equals(pattern, current, StringComparison.Ordinal))
            throw new InvalidOperationException($"{Unchanged}: the environment already reads '{pattern}'.");

        var builder = new SoundSystemAuthoring.Builder(mintId, "Authored by the EditEnvironment composer: the environment keeps its identity.");
        builder.Add(PhEnvironmentStringRepresentationOperationKinds.Set, intent.Environment, new
        {
            ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs), text = pattern,
            left = AuthorEnvironmentComposer.ContextPayload(left), right = AuthorEnvironmentComposer.ContextPayload(right),
        });
        return builder.Operations;
    }

    /// <summary>Returns the allomorphs and rules that hold the environment, which keep their link through the edit.</summary>
    public static IReadOnlyList<ComposedRelatedObject> ReadAffectedUsers(LcmCache cache, EditEnvironmentIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var environment = Resolve(cache, intent.Environment);
        return UnusedObjects.Referrers(environment)
            .Select(user => new ComposedRelatedObject(user.ClassName, SoundSystemAuthoring.Id(user).Value, UnusedObjects.Describe(user)))
            .ToArray();
    }

    private static IPhEnvironment Resolve(LcmCache cache, CanonicalId id)
    {
        if (!cache.ServiceLocator.GetInstance<ICmObjectRepository>().TryGetObject(id.ToGuid(), out var found) ||
            found is not IPhEnvironment environment || environment.Owner is not IPhPhonData)
            throw new InvalidOperationException($"{NotFound}: '{id.Value}' is not an environment of the phonological data.");
        return environment;
    }

    // Reads the sides the edit keeps. Each must come back as contexts that render to exactly the stored text.
    private static (IReadOnlyList<EnvironmentContext>? Left, IReadOnlyList<EnvironmentContext>? Right) ReadSides(
        LcmCache cache, string? current, bool needLeft, bool needRight)
    {
        var text = (current ?? string.Empty).Trim();
        var parts = text.StartsWith('/') ? text[1..].Split('_') : [];
        if (parts.Length != 2)
            throw new InvalidOperationException(
                $"{Unparseable}: the current string '{current}' is not of the form '/ left _ right'; give both left and right.");
        return (needLeft ? ParseSide(cache, parts[0], true, current) : null,
            needRight ? ParseSide(cache, parts[1], false, current) : null);
    }

    private static IReadOnlyList<EnvironmentContext> ParseSide(LcmCache cache, string side, bool left, string? current)
    {
        var which = left ? "left" : "right";
        string Fail(string why) =>
            $"{Unparseable}: the {which} side of '{current}' {why}; give both left and right.";
        var inventory = SoundSystemAuthoring.FirstSet(cache);
        var codes = inventory.PhonemesOC
            .SelectMany(phoneme => phoneme.CodesOS.Select(code => (Phoneme: phoneme, Spelling: EditPhonemeComposer.Spelling(cache, code))))
            .Where(item => item.Spelling is not null)
            .GroupBy(item => item.Spelling!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Phoneme, StringComparer.Ordinal);
        var ordered = codes.Keys.OrderByDescending(code => code.Length).ToArray();
        var classes = cache.LangProject.PhonologicalDataOA.NaturalClassesOS;
        var text = side.Normalize(NormalizationForm.FormD).Trim();
        var contexts = new List<EnvironmentContext>();
        for (var at = 0; at < text.Length;)
        {
            if (char.IsWhiteSpace(text[at])) { at++; continue; }
            if (text[at] == '#') { contexts.Add(new(Boundary: "word")); at++; continue; }
            if (text[at] == '[')
            {
                var end = text.IndexOf(']', at + 1);
                if (end < 0) throw new InvalidOperationException(Fail("has an unclosed '['"));
                var abbreviation = text[(at + 1)..end].Trim();
                var matches = classes.Where(n => n.Abbreviation.BestAnalysisAlternative.Text == abbreviation).ToArray();
                if (matches.Length != 1) throw new InvalidOperationException(Fail($"names '[{abbreviation}]', which is not exactly one natural class"));
                contexts.Add(new(NaturalClass: SoundSystemAuthoring.Id(matches[0])));
                at = end + 1;
                continue;
            }
            var code = ordered.FirstOrDefault(candidate => string.CompareOrdinal(text, at, candidate, 0, candidate.Length) == 0);
            if (code is null) throw new InvalidOperationException(Fail($"holds '{text[at]}', which is not a phoneme, a [class] or '#'"));
            contexts.Add(new(Phoneme: SoundSystemAuthoring.Id(codes[code])));
            at += code.Length;
        }
        string Compact(string value) => new string(value.Normalize(NormalizationForm.FormD).Where(c => !char.IsWhiteSpace(c)).ToArray());
        string rendered;
        try
        {
            AuthorEnvironmentComposer.RequireWordBoundaryOnly(contexts);
            rendered = AuthorEnvironmentComposer.RenderPattern(cache, left ? contexts : [], left ? [] : contexts);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Collections.Generic.KeyNotFoundException)
        {
            throw new InvalidOperationException(Fail($"cannot be kept ({exception.Message})"));
        }
        // "/ left _" or "/ _ right": compare the side only.
        var renderedSide = rendered.Split('_')[left ? 0 : 1].TrimStart('/');
        if (!string.Equals(Compact(renderedSide), Compact(side), StringComparison.Ordinal))
            throw new InvalidOperationException(Fail("does not read back the same way (a phoneme with several spellings, or notation the typed contexts cannot hold)"));
        return contexts;
    }
}
