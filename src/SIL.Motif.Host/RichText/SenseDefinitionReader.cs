using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.TextEditing;
using SIL.TextEditing.LCModel;
using SIL.TextEditing.Markdown;
using SIL.TextEditing.Project;

namespace SIL.Motif.Host.RichText;

/// <summary>
/// Reads every sense's Definition from an open project copy, in each current analysis and vernacular writing system,
/// as runs with the character styles and writing systems they use. It never opens a unit of work.
/// </summary>
public static class SenseDefinitionReader
{
    /// <summary>The senses in headword order, then the order of their entries' senses.</summary>
    public static SenseDefinitionsResponse Read(LcmCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        var writingSystems = new ProjectWritingSystems(cache);
        var styles = new ProjectStyles(cache);
        var systems = cache.ServiceLocator.WritingSystems;
        var shown = systems.CurrentAnalysisWritingSystems.Concat(systems.CurrentVernacularWritingSystems)
            .Select(ws => (ws.Handle, Tag: ws.Id)).DistinctBy(ws => ws.Tag).ToArray();
        var wsTags = new SortedSet<string>(shown.Select(ws => ws.Tag), StringComparer.Ordinal);
        var characterStyles = new SortedSet<string>(StringComparer.Ordinal);

        var senses = cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances()
            .OrderBy(entry => entry.HeadWord?.Text ?? "", StringComparer.Ordinal).ThenBy(entry => entry.Guid)
            .SelectMany(entry => entry.AllSenses.Select(sense => new SenseDefinitions(
                CanonicalId.FromGuid(sense.Guid).Value,
                $"{entry.HeadWord?.Text ?? ""}  {sense.Gloss.BestAnalysisAlternative?.Text ?? ""}".TrimEnd(),
                shown.Select(ws => new RichStringAlternative(ws.Tag, Runs(sense.Definition.get_String(ws.Handle), writingSystems, wsTags, characterStyles)))
                    .ToArray())))
            .ToArray();

        return new SenseDefinitionsResponse(senses,
            characterStyles.Where(styles.IsCharacterStyle).Select(name => new GdrStyle(name, GdrStyleKind.Character,
                wsTags.ToDictionary(tag => tag, tag => Properties(styles.Resolve(name, tag)!), StringComparer.Ordinal))).ToArray(),
            wsTags.Select(tag =>
            {
                var info = writingSystems.DescribeWritingSystem(tag) ?? WritingSystemInfo.Default(tag);
                return new GdrWritingSystem(tag, info.Direction, info.Font);
            }).ToArray());
    }

    // An empty alternative has no runs, so its fingerprint does not depend on the writing system it was stored in.
    private static GdrRun[] Runs(SIL.LCModel.Core.KernelInterfaces.ITsString? value, ProjectWritingSystems writingSystems,
        SortedSet<string> wsTags, SortedSet<string> characterStyles)
    {
        if (value is null || value.Length == 0) return [];
        return TsStringConversion.ToRuns(value, writingSystems).Where(run => run.Text.Length > 0).Select(run =>
        {
            wsTags.Add(run.Ws);
            var style = run.Props.TryGetStr(TextPropertyNumbers.NamedStyle, out var named) && named.Length > 0 ? named : null;
            if (style is not null) characterStyles.Add(style);
            return new GdrRun(run.Text, run.Ws, style, Properties(run.Props), run.ObjData);
        }).ToArray();
    }

    private static GdrTextProperties Properties(PropertyBag bag) => new(
        bag.IntProps.ToDictionary(pair => pair.Key, pair => new GdrIntProperty(pair.Value.Variation, pair.Value.Value)),
        bag.StrProps.ToDictionary(pair => pair.Key, pair => pair.Value));
}
