using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.Motif.Contract.Responses;
using SIL.TextEditing;
using SIL.TextEditing.Markdown;
using SIL.TextEditing.LCModel;

namespace SIL.Motif.Host.Gdr;

/// <summary>
/// What the latest stored Trial or Assessment row says about one word. <paramref name="Parsed"/> is whether it has an
/// analysis; <paramref name="Conclusive"/> is false for a timed-out, capped or skipped word, which settles nothing.
/// <paramref name="Analyses"/> lists the morpheme and rule ids of each analysis.
/// </summary>
public sealed record GdrExampleReading(string AssessmentId, bool Parsed, bool Conclusive, IReadOnlyList<IReadOnlyList<Guid>> Analyses);

/// <summary>Stored parse results for example words. Reads rows already in the store; it never parses.</summary>
public interface IGdrExampleEvidence
{
    /// <summary>The newest reading of each word that has one, keyed by the word as asked.</summary>
    IReadOnlyDictionary<string, GdrExampleReading> Read(IReadOnlyCollection<string> words);
}

/// <summary>A passed hypothesis's link to a GDR section, from the ledger (ADR 0117).</summary>
public sealed record GdrLedgerLink(string HypothesisId, string GdrId, string? Section);

/// <summary>
/// The GDR sync check (ADR 0119). One pass over the grammar's rules and affix processes, one over the records' paragraphs
/// and runs, one batched read of example words: O(rules + sections + examples). It never opens a unit of work and never parses.
/// </summary>
public static class GdrSyncChecker
{
    private const int SubjectCap = 10;

    private static readonly string[] NegativeHeadings = ["false parses", "does not parse"];

    private static readonly char[] TrimmedPunctuation =
        ['.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '"', '\'', '‘', '’', '“', '”', '/', '\\', '|', '<', '>'];

    /// <summary>Checks the records of <paramref name="cache"/>.</summary>
    public static GdrCheckResponse Check(LcmCache cache, IGdrExampleEvidence evidence, IReadOnlyList<GdrLedgerLink> ledger)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(ledger);
        var findings = new List<GdrSyncFinding>();
        var records = GdrReader.ReadStructure(cache);
        var vernacular = cache.ServiceLocator.WritingSystems.VernacularWritingSystems.Select(ws => ws.Handle).ToHashSet();
        var repository = cache.ServiceLocator.ObjectRepository;

        var sections = new List<Section>();
        foreach (var record in records) sections.AddRange(Sections(record, vernacular));
        var linked = new HashSet<Guid>();
        var linkCount = 0;
        foreach (var guid in sections.SelectMany(section => section.Paragraphs).SelectMany(paragraph => paragraph.Links))
        {
            linkCount++;
            linked.Add(guid);
        }

        // (b) a link whose object is gone.
        foreach (var group in sections.GroupBy(section => (section.Gdr, section.Title)))
        {
            var missing = group.SelectMany(section => section.Paragraphs).SelectMany(paragraph => paragraph.Links)
                .Where(guid => !repository.TryGetObject(guid, out _)).Distinct().ToArray();
            if (missing.Length == 0) continue;
            findings.Add(new GdrSyncFinding(GdrSyncCodes.CitesMissingObject,
                $"{Place(group.Key.Gdr, group.Key.Title)} cites {missing.Length} object(s) that are no longer in the project. " +
                "Remove the link or point it at the object that replaced it.",
                group.Key.Gdr, group.Key.Title.Length == 0 ? null : group.Key.Title, null, missing.Length,
                missing.Take(SubjectCap).Select(guid => guid.ToString("D")).ToArray()));
        }

        // The ledger's own links: a passed hypothesis names a GDR and section that must still exist.
        foreach (var link in ledger)
        {
            var record = records.FirstOrDefault(candidate => string.Equals(candidate.Abbreviation, link.GdrId, StringComparison.OrdinalIgnoreCase));
            if (record is not null && (string.IsNullOrWhiteSpace(link.Section) ||
                SectionExists(record, sections.Where(section => section.Gdr == record.Label), link.Section!))) continue;
            findings.Add(new GdrSyncFinding(GdrSyncCodes.LedgerLinkMissing, record is null
                ? $"{link.HypothesisId} names {link.GdrId}, which the project does not hold. Link the hypothesis to a record that exists."
                : $"{link.HypothesisId} names section '{link.Section}' of {record.Label}, which has no such heading. Add the section or relink.",
                record?.Label ?? link.GdrId, record is null ? null : link.Section, null, 1, [link.HypothesisId]));
        }

        // (a) a rule or affix process no record links, directly or through its owner.
        var coverage = new List<GdrCoverageKind>();
        var anyUncovered = false;
        foreach (var kind in Subjects(cache).GroupBy(subject => subject.Kind))
        {
            var all = kind.ToArray();
            var uncovered = all.Where(subject => !IsCovered(subject.Object, linked)).ToArray();
            coverage.Add(new GdrCoverageKind(kind.Key, all.Length, all.Length - uncovered.Length));
            if (uncovered.Length == 0) continue;
            anyUncovered = true;
            findings.Add(new GdrSyncFinding(GdrSyncCodes.ObjectUncovered,
                $"{uncovered.Length} of {all.Length} {Plural(kind.Key)} have no GDR section. Add a section that links them, " +
                "or link them from the section that already describes them.",
                null, null, kind.Key, uncovered.Length, uncovered.Take(SubjectCap).Select(subject => subject.Name).ToArray()));
        }
        if (anyUncovered && records.Count == 0)
            findings.Insert(0, new GdrSyncFinding(GdrSyncCodes.NoRecords,
                "The project has no Grammar Design Records list, so no rule has a section. Create the first records (ADR 0083).",
                null, null, null, 0, []));

        // (c) examples against the stored rows.
        var examples = sections.Where(section => section.Expectation != Expectation.None)
            .SelectMany(section => section.Paragraphs.SelectMany(paragraph => paragraph.Words.Select(word => (section, paragraph, word))))
            .ToArray();
        var distinct = examples.Select(example => example.word).Distinct(StringComparer.Ordinal).ToArray();
        var readings = distinct.Length == 0 ? new Dictionary<string, GdrExampleReading>() : evidence.Read(distinct);
        var checkedWords = new HashSet<string>(StringComparer.Ordinal);
        var assessments = new List<string>();
        foreach (var group in examples.GroupBy(example => (example.section.Gdr, example.section.Title, example.section.Expectation)))
        {
            var notParsing = new List<string>();
            var changed = new List<string>();
            var nowParses = new List<string>();
            var unverified = new List<string>();
            foreach (var (_, paragraph, word) in group.DistinctBy(example => (example.word, example.paragraph)))
            {
                if (!readings.TryGetValue(word, out var reading) || !reading.Conclusive)
                {
                    unverified.Add(word);
                    continue;
                }
                checkedWords.Add(word);
                if (!assessments.Contains(reading.AssessmentId)) assessments.Add(reading.AssessmentId);
                if (group.Key.Expectation == Expectation.Parses)
                {
                    if (!reading.Parsed) notParsing.Add(word);
                    else if (!UsesCited(repository, paragraph.Links, reading.Analyses)) changed.Add(word);
                }
                else if (reading.Parsed) nowParses.Add(word);
            }
            var section = group.Key.Title.Length == 0 ? null : group.Key.Title;
            Add(findings, GdrSyncCodes.ExampleNotParsing, notParsing, group.Key.Gdr, section,
                "example word(s) no longer parse. Re-read them from the grammar and fix the rule or the example.");
            Add(findings, GdrSyncCodes.ExampleAnalysisChanged, changed, group.Key.Gdr, section,
                "example word(s) parse, but no analysis uses the morphemes the example row cites. Update the row or the grammar.");
            Add(findings, GdrSyncCodes.ExampleNowParses, nowParses, group.Key.Gdr, section,
                "word(s) listed as not parsing now parse. Update the section or the rule that lets them through.");
            Add(findings, GdrSyncCodes.ExampleUnverified, unverified, group.Key.Gdr, section,
                "example word(s) have no conclusive Trial or Assessment row. Include them in the next Trial's words; nothing was parsed.");
        }

        var counts = new GdrSyncCounts(records.Count, sections.Count(section => section.Title.Length > 0), linkCount,
            distinct.Length, checkedWords.Count, assessments);
        return new GdrCheckResponse(true, findings.Count == 0, coverage, findings, counts);
    }

    private static void Add(List<GdrSyncFinding> findings, string code, List<string> words, string gdr, string? section, string tail)
    {
        if (words.Count == 0) return;
        findings.Add(new GdrSyncFinding(code, $"{Place(gdr, section)}: {words.Count} {tail}", gdr, section, null, words.Count,
            words.Take(SubjectCap).ToArray()));
    }

    private static string Place(string gdr, string? section) => string.IsNullOrEmpty(section) ? gdr : $"{gdr}, {section}";

    private static string Plural(string kind) => kind.EndsWith('s') ? kind : kind + "s";

    private static bool SectionExists(GdrStructure record, IEnumerable<Section> sections, string wanted)
    {
        var name = Normal(wanted);
        return name == Normal(record.Title) || name == "abstract" || sections.Any(section => Normal(section.Title) == name);
    }

    private static string Normal(string text) => text.Trim().TrimEnd(':').Trim().ToLowerInvariant();

    // Only cited morphemes are claims a stored row can test; rules and categories leave no id in an analysis.
    private static bool UsesCited(ICmObjectRepository repository, IReadOnlyList<Guid> links, IReadOnlyList<IReadOnlyList<Guid>> analyses)
    {
        var claims = new List<Guid[]>();
        foreach (var guid in links.Distinct())
        {
            if (!repository.TryGetObject(guid, out var item)) continue;
            switch (item)
            {
                case ILexEntry entry: claims.Add([entry.Guid]); break;
                case IMoForm form when form.Owner is ILexEntry owner: claims.Add([form.Guid, owner.Guid]); break;
                case IMoMorphSynAnalysis msa when msa.Owner is ILexEntry owner: claims.Add([msa.Guid, owner.Guid]); break;
            }
        }
        return claims.Count == 0 || analyses.Any(analysis => claims.All(claim => claim.Any(analysis.Contains)));
    }

    // A link to an owner covers what it owns: an entry its processes, a category its templates.
    private static bool IsCovered(ICmObject item, HashSet<Guid> linked)
    {
        for (var at = item; at is not null; at = at.Owner)
        {
            if (linked.Contains(at.Guid)) return true;
            if (at is ICmPossibilityList or ILangProject) return false;
        }
        return false;
    }

    private enum Expectation { None, Parses, DoesNotParse }

    private sealed record Section(string Gdr, string Title, Expectation Expectation, IReadOnlyList<Paragraph> Paragraphs);

    private sealed record Paragraph(IReadOnlyList<Guid> Links, IReadOnlyList<string> Words);

    private static IEnumerable<Section> Sections(GdrStructure record, HashSet<int> vernacular)
    {
        var title = "";
        var expectation = Expectation.None;
        var current = new List<Paragraph>();
        var started = false;
        foreach (var paragraph in record.Paragraphs)
        {
            if (HeadingLevel(paragraph.StyleName) > 0)
            {
                if (started || current.Count > 0) yield return new Section(record.Label, title, expectation, current);
                title = paragraph.Contents.Text?.Trim() ?? "";
                var key = Normal(title);
                expectation = key.StartsWith("examples", StringComparison.Ordinal) ? Expectation.Parses
                    : NegativeHeadings.Contains(key) ? Expectation.DoesNotParse : Expectation.None;
                current = [];
                started = true;
                continue;
            }
            current.Add(Read(paragraph.Contents, vernacular, expectation != Expectation.None));
        }
        if (started || current.Count > 0) yield return new Section(record.Label, title, expectation, current);
    }

    private static int HeadingLevel(string? style) =>
        style is not null && style.StartsWith("Heading ", StringComparison.OrdinalIgnoreCase) && int.TryParse(style.AsSpan(8), out var level)
            ? level : 0;

    private static Paragraph Read(ITsString contents, HashSet<int> vernacular, bool wantWords)
    {
        var links = new List<Guid>();
        var words = new List<string>();
        for (var run = 0; run < contents.RunCount; run++)
        {
            var props = contents.get_Properties(run);
            var data = props.GetStrPropValue((int)FwTextPropType.ktptObjData);
            if (!string.IsNullOrEmpty(data) && DefaultObjectMapper.Instance.Describe(data) is { Guid: { } guid } link &&
                link.Kind != MarkdownObjectKind.Picture)
                links.Add(guid);
            if (!wantWords) continue;
            if (!vernacular.Contains(props.GetIntPropValues((int)FwTextPropType.ktptWs, out _))) continue;
            words.AddRange(Tokens(contents.get_RunText(run)));
        }
        return new Paragraph(links, words);
    }

    private static IEnumerable<string> Tokens(string text)
    {
        foreach (var raw in text.Split([' ', '\t', '\r', '\n', ' ', '￼'], StringSplitOptions.RemoveEmptyEntries))
        {
            var word = raw.Trim(TrimmedPunctuation);
            // A leading or trailing hyphen or equals sign marks a morpheme, not a word to parse.
            if (word.Length == 0 || word[0] is '-' or '=' or '*' or '~' || word[^1] is '-' or '=' or '~') continue;
            yield return word;
        }
    }

    private sealed record Subject(string Kind, ICmObject Object, string Name);

    private static IEnumerable<Subject> Subjects(LcmCache cache)
    {
        var project = cache.LangProject;
        foreach (var rule in project.PhonologicalDataOA?.PhonRulesOS ?? Enumerable.Empty<IPhSegmentRule>())
            yield return new Subject("Phonological rule", rule, Name(cache, rule.Name, "unnamed rule"));
        var morphology = project.MorphologicalDataOA;
        foreach (var rule in morphology?.CompoundRulesOS ?? Enumerable.Empty<IMoCompoundRule>())
            yield return new Subject("Compound rule", rule, Name(cache, rule.Name, "unnamed compound rule"));
        var number = 0;
        foreach (var rule in morphology?.AdhocCoProhibitionsOC ?? Enumerable.Empty<IMoAdhocProhib>())
            yield return new Subject("Ad hoc rule", rule, $"Ad hoc rule {++number}");
        foreach (var feature in morphology?.ProdRestrictOA?.ReallyReallyAllPossibilities ?? Enumerable.Empty<ICmPossibility>())
            yield return new Subject("Exception feature", feature, Name(cache, feature.Name, "unnamed exception feature"));
        foreach (var category in Categories(project.PartsOfSpeechOA?.PossibilitiesOS ?? Enumerable.Empty<ICmPossibility>()))
        {
            var label = Name(cache, category.Name, "unnamed category");
            foreach (var template in category.AffixTemplatesOS)
                yield return new Subject("Affix template", template, $"{Name(cache, template.Name, "unnamed template")} ({label})");
            foreach (var stem in category.StemNamesOC)
                yield return new Subject("Stem name", stem, $"{Name(cache, stem.Name, "unnamed stem name")} ({label})");
            foreach (var inflection in category.InflectionClassesOC.SelectMany(InflectionClasses))
                yield return new Subject("Inflection class", inflection, $"{Name(cache, inflection.Name, "unnamed class")} ({label})");
        }
        foreach (var process in cache.ServiceLocator.GetInstance<IMoFormRepository>().AllInstances().OfType<IMoAffixProcess>())
            yield return new Subject("Affix process", process, process.Owner is ILexEntry entry
                ? $"{Name(cache, process.Form, entry.ShortName ?? "unnamed process")} ({entry.ShortName})"
                : Name(cache, process.Form, "unnamed process"));
    }

    private static IEnumerable<IMoInflClass> InflectionClasses(IMoInflClass item) =>
        new[] { item }.Concat(item.SubclassesOC.SelectMany(InflectionClasses));

    private static IEnumerable<IPartOfSpeech> Categories(IEnumerable<ICmPossibility> items)
    {
        foreach (var category in items.OfType<IPartOfSpeech>())
        {
            yield return category;
            foreach (var child in Categories(category.SubPossibilitiesOS)) yield return child;
        }
    }

    private static string Name(LcmCache cache, IMultiAccessorBase text, string fallback)
    {
        foreach (var ws in new[] { cache.DefaultAnalWs, cache.DefaultVernWs }.Concat(text.AvailableWritingSystemIds))
        {
            var value = text.get_String(ws).Text;
            if (!string.IsNullOrEmpty(value)) return value;
        }
        return fallback;
    }
}
