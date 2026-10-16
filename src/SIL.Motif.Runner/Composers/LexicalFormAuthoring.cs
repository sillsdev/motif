using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>
/// The checks and link operations shared by ordinary stem and affix form authoring:
/// the environments it occurs in and, for an affix, the inflection classes of the stems it attaches to.
/// Both link kinds are unordered, so callers sort identities and the lowered order is deterministic.
/// </summary>
internal static class LexicalFormAuthoring
{
    public static T Resolve<T>(LcmCache cache, CanonicalId id, string construct) where T : ICmObject
    {
        if (!cache.ServiceLocator.GetInstance<ICmObjectRepository>().IsValidObjectId(id.ToGuid()))
            throw new InvalidOperationException($"'{construct}': referenced identity '{id.Value}' does not exist.");
        return ReferenceFieldLowering.Resolve<T>(cache, id, construct);
    }

    public static void RequireMatchingKind(ILexEntry entry, MoFormConcreteClass formClass, string construct)
    {
        var incumbent = entry.LexemeFormOA;
        if (incumbent is IMoAffixProcess || entry.AlternateFormsOS.Any(form => form is IMoAffixProcess))
            throw new InvalidOperationException(
                $"'{construct}': entry '{CanonicalId.FromGuid(entry.Guid).Value}' has an affix process as its " +
                "form; ordinary alternates are not supported in a process graph.");
        var entryClass = incumbent switch
        {
            IMoStemAllomorph => MoFormConcreteClass.StemAllomorph,
            IMoAffixAllomorph => MoFormConcreteClass.AffixAllomorph,
            _ => throw new InvalidOperationException(
                $"'{construct}': entry '{CanonicalId.FromGuid(entry.Guid).Value}' has no stem or affix lexeme " +
                "form to match the alternate form against."),
        };
        if (entryClass != formClass)
            throw new InvalidOperationException(
                $"'{construct}': the morph type makes " +
                (formClass == MoFormConcreteClass.StemAllomorph ? "a stem form" : "an affix form") +
                " but the entry's lexeme form is " + (entryClass == MoFormConcreteClass.StemAllomorph ? "a stem." : "an affix."));
    }

    public static string RequireText(string text, string name, string construct)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException($"'{construct}': '{name}' must not be blank.");
        return text.Trim().Normalize(NormalizationForm.FormD);
    }

    public static void RequireWritingSystem(LcmCache cache, string tag, bool vernacular, string construct)
    {
        var systems = vernacular
            ? cache.ServiceLocator.WritingSystems.VernacularWritingSystems
            : cache.ServiceLocator.WritingSystems.AnalysisWritingSystems;
        if (!systems.Any(system => system.Id == tag))
            throw new InvalidOperationException(
                $"'{construct}': '{tag}' is not one of this project's " +
                (vernacular ? "vernacular" : "analysis") + " writing systems.");
    }

    public static IPartOfSpeech? OwningCategory(IMoInflClass inflectionClass)
    {
        ICmObject? current = inflectionClass.Owner;
        while (current is IMoInflClass) current = current.Owner;
        return current as IPartOfSpeech;
    }

    public static bool InCategoryOrAncestor(IPartOfSpeech? owner, IPartOfSpeech category)
    {
        if (owner is null) return false;
        for (var current = category; current is not null; current = current.Owner as IPartOfSpeech)
            if (owner.Guid == current.Guid) return true;
        return false;
    }

    /// <summary>Resolves distinct environment identities, refusing an empty or repeating list.</summary>
    public static IReadOnlyList<CanonicalId> RequireEnvironments(
        LcmCache cache, IReadOnlyList<CanonicalId> environments, string construct)
    {
        RequireDistinct(environments, "environments", construct);
        foreach (var environment in environments)
            RequireKnownNaturalClasses(Resolve<IPhEnvironment>(cache, environment, construct), construct);
        return Sorted(environments);
    }

    /// <summary>
    /// Refuses an environment whose pattern names a natural class by anything but an existing abbreviation, so a
    /// form is never linked to an environment that does not restrict it; pinned by
    /// `AnEnvironmentNamingAnUnknownNaturalClassIsRefusedAndOneNamingAnAbbreviationIsAccepted`.
    /// </summary>
    public static void RequireKnownNaturalClasses(IPhEnvironment environment, string construct)
    {
        var abbreviations = environment.Cache.LangProject.PhonologicalDataOA.NaturalClassesOS
            .Select(item => item.Abbreviation.BestAnalysisAlternative.Text)
            .Where(text => !string.IsNullOrEmpty(text)).ToHashSet(StringComparer.Ordinal);
        var pattern = environment.StringRepresentation?.Text ?? "";
        foreach (System.Text.RegularExpressions.Match token in System.Text.RegularExpressions.Regex.Matches(pattern, @"\[([^\]]*)\]"))
        {
            var name = token.Groups[1].Value;
            if (!abbreviations.Contains(name))
                throw new InvalidOperationException(
                    $"'{construct}': environment '{environment.Guid}' names natural class '{name}', which is not the " +
                    "abbreviation of any natural class, so the parser would ignore the environment and leave the form " +
                    "unrestricted; use the class abbreviation in the environment.");
        }
    }

    /// <summary>
    /// Resolves distinct inflection classes, each owned by one of the <paramref name="categories"/> or an
    /// ancestor of one, so a class cannot restrict an affix to stems of a category it never attaches to.
    /// </summary>
    public static IReadOnlyList<CanonicalId> RequireInflectionClasses(LcmCache cache,
        IReadOnlyList<CanonicalId> classes, IReadOnlyList<IPartOfSpeech> categories, string construct)
    {
        RequireDistinct(classes, "inflectionClasses", construct);
        if (categories.Count == 0)
            throw new InvalidOperationException(
                $"'{construct}': inflection classes need grammatical info to check them against.");
        foreach (var classId in classes)
        {
            var inflectionClass = Resolve<IMoInflClass>(cache, classId, construct);
            var owner = OwningCategory(inflectionClass);
            if (!categories.Any(category => InCategoryOrAncestor(owner, category)))
                throw new InvalidOperationException(
                    $"'{construct}': inflection class '{classId.Value}' does not belong to the category or one of its ancestors.");
        }
        return Sorted(classes);
    }

    /// <summary>The link operation kind and member for each environment then class, in a stable order.</summary>
    public static IEnumerable<(string Kind, CanonicalId Member)> Links(MoFormConcreteClass formClass,
        IReadOnlyList<CanonicalId> sortedEnvironments, IReadOnlyList<CanonicalId> sortedClasses)
    {
        var environmentKind = formClass == MoFormConcreteClass.StemAllomorph
            ? MoStemAllomorphPhoneEnvOperationKinds.AddRefPhoneEnv
            : MoAffixAllomorphPhoneEnvOperationKinds.AddRefPhoneEnv;
        foreach (var environment in sortedEnvironments) yield return (environmentKind, environment);
        foreach (var inflectionClass in sortedClasses)
            yield return (MoAffixFormInflectionClassesOperationKinds.AddRefInflectionClasses, inflectionClass);
    }

    private static void RequireDistinct(IReadOnlyList<CanonicalId> ids, string name, string construct)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
            throw new InvalidOperationException($"'{construct}': '{name}' must name at least one identity when present.");
        if (ids.Select(id => id.ToGuid()).Distinct().Count() != ids.Count)
            throw new InvalidOperationException($"'{construct}': '{name}' must not repeat an identity.");
    }

    /// <summary>Emits one spelling per decoded identity for this author's local dependency and slot keys.</summary>
    public static CanonicalId Normalize(CanonicalId id) => CanonicalId.FromGuid(id.ToGuid());

    public static IReadOnlyList<CanonicalId>? Normalize(IReadOnlyList<CanonicalId>? ids) =>
        ids?.Select(Normalize).ToArray();

    private static IReadOnlyList<CanonicalId> Sorted(IReadOnlyList<CanonicalId> ids) =>
        ids.OrderBy(id => id.Value, StringComparer.Ordinal).ToArray();
}
