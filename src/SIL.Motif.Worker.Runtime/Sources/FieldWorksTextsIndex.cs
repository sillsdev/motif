using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SIL.LCModel;
using SIL.LCModel.DomainServices;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.Sources;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Baselines;

namespace SIL.Motif.Worker.Sources;

/// <summary>
/// Indexes the Texts of the saved project as one Source whose Passages are Texts. A place is the occurrence
/// identity the Text words projection already uses (segment GUID and occurrence index), and a wordform keeps both its
/// FieldWorks id and its (writing system, spelling) type so homographs stay apart.
/// </summary>
/// <remarks>
/// The build reads one Text at a time and appends its rows to the staging database before reading the next, so memory
/// holds one Text plus the distinct wordforms. A Text whose content fingerprint matches the previous generation has its
/// rows copied inside the staging database instead of being rebuilt; the fingerprint covers every value a row is made of,
/// so a copied Text is the Text a full rebuild would produce.
/// </remarks>
internal static class FieldWorksTextsIndex
{
    /// <summary>The reserved Source id; its colon cannot occur in a Paratext folder-bound Source's Abbreviation by convention.</summary>
    public const string SourceId = "FIELDWORKS:TEXTS";
    public const string Kind = "FieldWorksTexts";
    public const string TokenizerVersion = "fw-texts-1";
    public const string AnchorVersion = "fw-occurrence-1";

    /// <summary>Observes each Text read from the saved project during a build.</summary>
    internal static readonly AsyncLocal<Action<string>?> TextRead = new();

    /// <summary>Observes each Text whose rows were copied from the previous generation instead of rebuilt.</summary>
    internal static readonly AsyncLocal<Action<string>?> TextReused = new();

    /// <summary>Observes each Text once its rows are staged, copied or rebuilt.</summary>
    internal static readonly AsyncLocal<Action<string>?> TextStaged = new();

    /// <summary>Builds from the supplied saved cache and publishes only if the project is still the one captured.</summary>
    public static bool Build(MotifDatabase database, LcmCache cache, string writingSystem,
        ProgressSourceIdentity projectIdentity, CancellationToken cancellationToken = default)
    {
        var repository = new SourceIndexRepository(database);
        var previous = repository.ReadState(SourceId);
        var earlier = previous is { } p && p.Kind == Kind && p.TokenizerVersion == TokenizerVersion && p.AnchorVersion == AnchorVersion
            ? repository.ReadPassages(SourceId).ToDictionary(passage => passage.PassageId, StringComparer.Ordinal)
            : null;
        var identity = new SourceIndexIdentity("", "", "", "Active", writingSystem, projectIdentity.Serialize(), []);
        var state = new SourceIndexState(SourceId, Kind, Guid.NewGuid().ToString("N"), "", identity.Serialize(),
            "ready", TokenizerVersion, AnchorVersion);
        using var builder = SourceIndexStage.Begin(state, earlier is null ? null : database.FullPath);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var reader = new TextReader(cache, writingSystem);
        var staged = new HashSet<string>(StringComparer.Ordinal);
        var first = true;
        foreach (var text in cache.ServiceLocator.GetInstance<ITextRepository>().AllInstances()
                     .OrderBy(text => text.Guid.ToString("D"), StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var passageId = text.Guid.ToString("D");
            TextRead.Value?.Invoke(passageId);
            var lines = reader.Read(text);
            var hash = reader.Fingerprint(lines);
            if (earlier is not null && earlier.TryGetValue(passageId, out var old) && old.ContentHash == hash
                && builder.TryCopyPassage(old))
            {
                TextReused.Value?.Invoke(passageId);
                Chain(digest, ref first, old);
                TextStaged.Value?.Invoke(passageId);
                continue;
            }
            var passage = Rows(builder, staged, lines, passageId, hash);
            Chain(digest, ref first, passage);
            TextStaged.Value?.Invoke(passageId);
        }
        cancellationToken.ThrowIfCancellationRequested();
        using var stage = builder.Complete(Convert.ToHexStringLower(digest.GetHashAndReset()));
        if (!projectIdentity.MatchesMetadata(database.Project.FullFwDataPath)) return false;
        return repository.Publish(stage, previous?.Generation ?? "");
    }

    private static void Chain(IncrementalHash digest, ref bool first, SourcePassage passage)
    {
        digest.AppendData(Encoding.UTF8.GetBytes((first ? "" : "\n") + passage.PassageId + ":" + passage.ContentHash));
        first = false;
    }

    private static SourcePassage Rows(SourceIndexStage.Builder builder, HashSet<string> staged, List<Line> lines,
        string passageId, string hash)
    {
        var total = 0;
        var eligible = 0;
        foreach (var line in lines)
            foreach (var token in line.Tokens)
            {
                if (token.Word is not { } word) continue;
                total++;
                if (word.Exclusion is null) eligible++;
            }
        var passage = new SourcePassage(passageId, passageId, hash, total, eligible);
        builder.AddPassage(passage);
        var ordinal = 0;
        foreach (var line in lines)
        {
            var verse = line.Number.ToString(CultureInfo.InvariantCulture);
            foreach (var token in line.Tokens)
            {
                if (token.Word is not { } word) continue;
                if (staged.Add(word.Type.WordformKey)) builder.AddWordform(word.Type);
                builder.AddPlace(new(passageId, ++ordinal, $"{line.SegmentId:D}#{token.OccurrenceIndex}", verse, word.Text,
                    word.Type.WordformKey, token.AnalysisId?.ToString("D"), token.AnalysisKey,
                    token.AnalysisId is null ? "no-analysis" : "unmeasured"));
            }
        }
        return passage;
    }

    private sealed record Word(string Text, string? Exclusion, SourceWordform Type, string Fingerprint);

    private readonly record struct Token(int OccurrenceIndex, string? Punctuation, Word? Word, Guid? AnalysisId, string? AnalysisKey);

    private sealed record Line(Guid SegmentId, int Number, string Sentence, List<Token> Tokens);

    /// <summary>Reads Texts into the values their rows are made of.</summary>
    private sealed class TextReader(LcmCache cache, string writingSystem)
    {
        private readonly Dictionary<Guid, Word> _words = [];
        private readonly Dictionary<Guid, string> _analysisKeys = [];

        public List<Line> Read(IText text)
        {
            var lines = new List<Line>();
            var number = 0;
            foreach (var paragraph in text.ContentsOA?.ParagraphsOS.OfType<IStTxtPara>() ?? Enumerable.Empty<IStTxtPara>())
            {
                var bySegment = SegmentServices.GetAnalysisOccurrences(paragraph).ToLookup(occurrence => occurrence.Segment);
                foreach (var segment in paragraph.SegmentsOS)
                {
                    number++;
                    lines.Add(new(segment.Guid, number, segment.BaselineText?.Text ?? string.Empty,
                        bySegment[segment].Select(Read).ToList()));
                }
            }
            return lines;
        }

        /// <summary>Hashes every value a row is made of; the same Text content gives the same value.</summary>
        public string Fingerprint(List<Line> lines)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Append(hash, "fw-texts-fingerprint-1/" + TokenizerVersion + "/" + AnchorVersion);
            Append(hash, writingSystem);
            foreach (var line in lines)
            {
                Append(hash, line.SegmentId.ToString("D"));
                Append(hash, line.Number.ToString(CultureInfo.InvariantCulture));
                Append(hash, line.Sentence);
                Append(hash, line.Tokens.Count.ToString(CultureInfo.InvariantCulture));
                foreach (var token in line.Tokens)
                {
                    Append(hash, token.OccurrenceIndex.ToString(CultureInfo.InvariantCulture));
                    Append(hash, token.Punctuation ?? "");
                    Append(hash, token.Word?.Fingerprint ?? "");
                    Append(hash, token.AnalysisId?.ToString("D") ?? "");
                    Append(hash, token.AnalysisKey ?? "");
                }
            }
            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }

        private Token Read(AnalysisOccurrence occurrence)
        {
            var analysis = occurrence.Analysis;
            if (analysis is IPunctuationForm punctuation)
                return new(occurrence.Index, punctuation.Form?.Text ?? string.Empty, null, null, null);
            var (wordform, wfiAnalysis) = analysis switch
            {
                IWfiGloss gloss => ((IWfiWordform)gloss.Owner.Owner, (IWfiAnalysis)gloss.Owner),
                IWfiAnalysis wordAnalysis => ((IWfiWordform)wordAnalysis.Owner, wordAnalysis),
                IWfiWordform bare => (bare, (IWfiAnalysis?)null),
                _ => throw new NotSupportedException($"Unrecognized analysis kind: {analysis.GetType()}"),
            };
            if (!_words.TryGetValue(wordform.Guid, out var word)) _words.Add(wordform.Guid, word = ReadWord(wordform));
            string? key = null;
            if (wfiAnalysis is not null && !_analysisKeys.TryGetValue(wfiAnalysis.Guid, out key))
                _analysisKeys.Add(wfiAnalysis.Guid, key = AnalysisContent.ComputeDigest(wfiAnalysis.MorphBundlesOS
                    .Select(bundle => new MorphBundleContent(bundle.MorphRA?.Guid.ToString("D"),
                        bundle.MsaRA?.Guid.ToString("D"), bundle.InflTypeRA?.Guid.ToString("D"))).ToList()));
            return new(occurrence.Index, null, word, wfiAnalysis?.Guid, key);
        }

        private Word ReadWord(IWfiWordform wordform)
        {
            var form = TextWordsProjectionBuilder.TxtForms(cache, wordform.Form).FirstOrDefault();
            var text = form?.Text ?? string.Empty;
            var coverage = TextWordsProjectionBuilder.CoverageIdentity(cache, wordform);
            var type = new SourceWordform("fw:" + wordform.Guid.ToString("D"),
                coverage.WritingSystem ?? form?.WritingSystem ?? writingSystem,
                text.Normalize(NormalizationForm.FormC).ToLowerInvariant(), text, wordform.Guid.ToString("D"));
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var part in new[] { type.WordformKey, type.WritingSystem, type.Normalized, type.Display, coverage.Exclusion ?? "" })
                Append(hash, part);
            return new(text, coverage.Exclusion, type, Convert.ToHexStringLower(hash.GetHashAndReset()));
        }
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(bytes.Length)));
        hash.AppendData(bytes);
    }
}
