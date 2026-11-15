using System.Security.Cryptography;
using System.Text;
using SIL.LCModel;
using SIL.Motif.Host.Sources;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Texts;
using SIL.Motif.Worker.Baselines;

namespace SIL.Motif.Worker.Sources;

/// <summary>
/// Indexes the Texts of the saved project as one Source whose Passages are Texts. A place is the occurrence
/// identity the Text words projection already uses (segment GUID and occurrence index), and a wordform keeps both its
/// FieldWorks id and its (writing system, spelling) type so homographs stay apart.
/// </summary>
internal static class FieldWorksTextsIndex
{
    /// <summary>The reserved Source id; its colon cannot occur in a Paratext folder-bound Source's Abbreviation by convention.</summary>
    public const string SourceId = "FIELDWORKS:TEXTS";
    public const string Kind = "FieldWorksTexts";
    public const string TokenizerVersion = "fw-texts-1";
    public const string AnchorVersion = "fw-occurrence-1";

    /// <summary>Observes each Text whose places are read from the saved project during a build.</summary>
    internal static readonly AsyncLocal<Action<string>?> TextRead = new();

    /// <summary>Builds from the supplied saved cache and publishes only if the project is still the one captured.</summary>
    public static bool Build(MotifDatabase database, LcmCache cache, string writingSystem,
        ProgressSourceIdentity projectIdentity, CancellationToken cancellationToken = default)
    {
        var repository = new SourceIndexRepository(database);
        var previous = repository.ReadState(SourceId);
        var identity = new SourceIndexIdentity("", "", "", "Active", writingSystem, projectIdentity.Serialize(), []);
        var projection = TextWordsProjectionBuilder.Build(cache, cancellationToken, includeCoverage: true);
        var wordforms = projection.Wordforms.ToDictionary(word => word.WordformId);
        var passages = new List<SourcePassage>();
        var types = new Dictionary<string, SourceWordform>(StringComparer.Ordinal);
        var places = new List<SourceWordPlace>();
        foreach (var text in projection.Texts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var passageId = text.TextId.ToString("D");
            TextRead.Value?.Invoke(passageId);
            var start = places.Count;
            var eligible = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var line in text.Lines)
            {
                Append(hash, line.SegmentId.ToString("D"));
                Append(hash, line.Sentence);
                foreach (var token in line.Tokens)
                {
                    Append(hash, token.Text);
                    Append(hash, token.WordformId?.ToString("D") ?? "");
                    Append(hash, token.AnalysisId?.ToString("D") ?? "");
                    if (token.WordformId is not { } wordformId) continue;
                    if (!wordforms.TryGetValue(wordformId, out var word))
                        throw new InvalidDataException("A Text occurrence has no projected wordform.");
                    var key = "fw:" + wordformId.ToString("D");
                    var ws = word.CoverageWritingSystem ?? token.TextWritingSystem ?? writingSystem;
                    if (!types.ContainsKey(key))
                        types[key] = new(key, ws, token.Text.Normalize(NormalizationForm.FormC).ToLowerInvariant(),
                            token.Text, wordformId.ToString("D"));
                    if (word.CoverageExclusion is null) eligible++;
                    places.Add(new(passageId, places.Count - start + 1, $"{line.SegmentId:D}#{token.OccurrenceIndex}",
                        line.Number.ToString(System.Globalization.CultureInfo.InvariantCulture), token.Text, key,
                        token.AnalysisId?.ToString("D"), token.AnalysisKey,
                        token.AnalysisId is null ? "no-analysis" : "unmeasured"));
                }
            }
            passages.Add(new(passageId, passageId, Convert.ToHexStringLower(hash.GetHashAndReset()),
                places.Count - start, eligible));
        }
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join('\n', passages.Select(p => p.PassageId + ":" + p.ContentHash)))));
        var state = new SourceIndexState(SourceId, Kind, Guid.NewGuid().ToString("N"), digest, identity.Serialize(),
            "ready", TokenizerVersion, AnchorVersion);
        using var stage = SourceIndexStage.Create(state, passages, types.Values, places);
        cancellationToken.ThrowIfCancellationRequested();
        if (!identity.ProjectCurrent(database.Project.FullFwDataPath)) return false;
        return repository.Publish(stage, previous?.Generation ?? "");
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(bytes.Length)));
        hash.AppendData(bytes);
    }
}
