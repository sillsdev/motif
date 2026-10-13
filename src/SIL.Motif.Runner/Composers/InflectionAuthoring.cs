using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

internal static class InflectionAuthoring
{
    internal static string Text(string text, string field)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException($"'{field}' must not be blank.");
        return text.Trim().Normalize(NormalizationForm.FormD);
    }

    internal static int WritingSystem(LcmCache cache, string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) throw new InvalidOperationException("A writing system tag is required.");
        var ws = cache.WritingSystemFactory.GetWsFromStr(tag);
        if (ws == 0) throw new InvalidOperationException($"Writing system '{tag}' is not known to this project.");
        return ws;
    }

    internal static void UniqueName<T>(IEnumerable<T> objects, Func<T, string?> read, string name) where T : ICmObject
    {
        var existing = objects.FirstOrDefault(item => string.Equals(read(item)?.Normalize(NormalizationForm.FormD),
            name, StringComparison.Ordinal));
        if (existing is not null)
            throw new InvalidOperationException($"'{name}' already exists as '{CanonicalId.FromGuid(existing.Guid).Value}'.");
    }

    internal static IPartOfSpeech Category(LcmCache cache, CanonicalId id)
    {
        var category = ReferenceFieldLowering.Resolve<IPartOfSpeech>(cache, id, "inflection authoring");
        if (!cache.LangProject.PartsOfSpeechOA.ReallyReallyAllPossibilities.Contains(category))
            throw new InvalidOperationException("The category must belong to this project's parts-of-speech list.");
        return category;
    }

    internal static IPartOfSpeech? OwningCategory(IMoInflClass inflectionClass)
    {
        ICmObject? owner = inflectionClass.Owner;
        while (owner is IMoInflClass) owner = owner.Owner;
        return owner as IPartOfSpeech;
    }

    internal static IEnumerable<IMoInflClass> Classes(IPartOfSpeech category) => Descendants(category.InflectionClassesOC);

    private static IEnumerable<IMoInflClass> Descendants(IEnumerable<IMoInflClass> classes)
    {
        foreach (var item in classes)
        {
            yield return item;
            foreach (var child in Descendants(item.SubclassesOC)) yield return child;
        }
    }
}
