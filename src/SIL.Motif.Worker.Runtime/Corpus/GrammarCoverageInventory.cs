using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.LCModel.Core.WritingSystems;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Sources;
using SIL.Motif.Host.Store;
using SIL.Motif.Scripture;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Sources;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Worker.Corpus;

internal sealed record GrammarCoverageInventory(
    string Id, FrequencyProvenance Provenance, string MappingDigest, string WeightDigest, FrequencyRow[] Rows,
    string? ComparisonVersion = null)
{
    public static GrammarCoverageInventory FromManifest(FrequencyManifest manifest, string comparisonVersion,
        CancellationToken cancellationToken)
    {
        var provenance = manifest.Provenance;
        string[] pins = [provenance.ProjectId, provenance.SourceId, provenance.SourceDigest, provenance.ScopeDigest,
            provenance.WeightProvenance, provenance.TokenizerId, provenance.TokenizerHash, provenance.ProfileHash,
            provenance.WritingSystemId];
        if (!provenance.AllowsSurfaceStorage)
            throw new InvalidDataException("The Source has no authorization to persist surface-bearing Wordforms.");
        if (pins.Any(string.IsNullOrWhiteSpace) || provenance.FormNormalization is not ("NFC" or "NFD") ||
            provenance.CaseFallback is not ("exact" or "explicit" or "fieldworks-lowercase" or "first-letter-lowercase") ||
            provenance.Exclusions.Any(item => string.IsNullOrWhiteSpace(item.Key) || item.Value < 0))
            throw new InvalidDataException("The frequency manifest needs pinned Source, scope, weights and segmentation provenance.");
        provenance = provenance with
        {
            Exclusions = new SortedDictionary<string, long>(provenance.Exclusions.ToDictionary(), StringComparer.Ordinal),
        };

        HashSet<string> ids = new(StringComparer.Ordinal);
        HashSet<(string WritingSystem, string Form)> forms = [];
        var rows = new List<FrequencyRow>(manifest.Rows.Count);
        foreach (var input in manifest.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = input with
            {
                CasedForm = input.CasedForm is { } cased ? Normalize(cased) : null,
                ParserKey = Normalize(input.ParserKey),
                FallbackParserKey = input.FallbackParserKey is { } fallback ? Normalize(fallback) : null,
            };
            if (string.IsNullOrWhiteSpace(row.WordformId) || row.WordformId.Any(ch => !char.IsAscii(ch) || char.IsControl(ch)) ||
                string.IsNullOrWhiteSpace(row.WritingSystemId) || row.Frequency <= 0 ||
                !ids.Add(row.WordformId) || !forms.Add((row.WritingSystemId, row.CasedForm ?? row.ParserKey)) ||
                (!provenance.Fold && row.CasedForm != row.ParserKey) ||
                (provenance.CaseFallback == "exact" && row.FallbackParserKey is not null) ||
                (row.FallbackParserKey is not null && row.FallbackParserKey == row.ParserKey))
                throw new InvalidDataException("The frequency manifest contains duplicate identities or an incompatible Wordform mapping.");
            rows.Add(row);
        }

        var ordered = rows.OrderBy(row => row.WordformId, StringComparer.Ordinal).ToArray();
        var mapping = Digest(ordered.Select(row =>
            new { row.WordformId, row.WritingSystemId, row.CasedForm, row.ParserKey, row.FallbackParserKey }));
        var weights = Digest(ordered.Select(row => new { row.WordformId, row.Frequency }));
        return new(Digest(new { provenance, mapping, weights, comparisonVersion }), provenance, mapping, weights, ordered,
            comparisonVersion);
    }

    public static FrequencyManifest FromParatext(MotifDatabase database, string projectId, string sourceId, string generation,
        string profileHash, CoreWritingSystemDefinition writingSystem, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writingSystem);
        using var connection = database.OpenConnection();
        using var snapshot = connection.BeginTransaction(deferred: true);
        var provenance = ReadParatextProvenance(connection, snapshot, projectId, sourceId, generation, profileHash, writingSystem);
        return new(provenance, ReadParatextRows(connection, snapshot, provenance, LowercasePass.For(writingSystem), cancellationToken));
    }

    internal static FrequencyProvenance ReadParatextProvenance(SqliteConnection connection, SqliteTransaction snapshot,
        string projectId, string sourceId, string generation, string profileHash, CoreWritingSystemDefinition writingSystem)
    {
        var state = SourceIndexRepository.ReadState(connection, snapshot, sourceId);
        if (state is not { Status: "ready", Kind: nameof(SourceKind.Paratext) } || state.Generation != generation ||
            string.IsNullOrWhiteSpace(generation) || string.IsNullOrWhiteSpace(state.SourceDigest) ||
            state.TokenizerVersion != ParatextPassages.TokenizerVersion || state.AnchorVersion != ParatextPassages.AnchorVersion)
            throw new InvalidDataException("The pinned Paratext Source generation is unavailable.");
        var identity = SourceIndexIdentity.Parse(state.IdentityJson);
        if (identity?.WritingSystem != writingSystem.Id || string.IsNullOrWhiteSpace(profileHash) ||
            ProgressSourceIdentity.Parse(identity.ProjectIdentityJson)?.SidecarDigest != profileHash)
            throw new InvalidDataException("The Paratext Source index was not built on the pinned segmentation profile.");

        var paratext = new ParatextFrequencyScope(state.Generation, identity.Context, identity.Versification,
            state.AnchorVersion, state.SourceDigest);
        var scope = Digest(new { state.SourceId, paratext.Context, paratext.Versification, paratext.AnchorVersion });
        var sourceDigest = Digest(new { paratext.PassageDigest, ScopeDigest = scope });
        return new(projectId, state.SourceId, sourceDigest, scope, "paratext-source-index", state.TokenizerVersion,
            Digest(state.TokenizerVersion), profileHash, false, "fieldworks-lowercase", "NFD", identity.WritingSystem,
            new Dictionary<string, long>(), AllowsSurfaceStorage: true, ParatextScope: paratext);
    }

    internal static FrequencyRow[] ReadParatextRows(SqliteConnection connection, SqliteTransaction snapshot,
        FrequencyProvenance provenance, LowercasePass casing, CancellationToken cancellationToken)
    {
        using var query = connection.CreateCommand();
        query.Transaction = snapshot;
        // The index's SourceWordforms are case-folded, so the Occurrence surface supplies the cased key.
        query.CommandText = """
            SELECT w.WritingSystem, p.Surface, COUNT(*)
            FROM SourceWordPlaces p
            JOIN SourceWordforms w ON w.SourceId = p.SourceId AND w.WordformKey = p.WordformKey
            WHERE p.SourceId = $source
            GROUP BY w.WritingSystem, p.Surface;
            """;
        query.Parameters.AddWithValue("$source", provenance.SourceId);
        Dictionary<(string WritingSystem, string Form), long> weights = [];
        using (var reader = query.ExecuteReader())
        {
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = (reader.GetString(0), Normalize(reader.GetString(1)));
                weights[key] = checked(weights.GetValueOrDefault(key) + reader.GetInt64(2));
            }
        }

        return weights.Select(item =>
        {
            var (wordWritingSystem, form) = item.Key;
            var lower = wordWritingSystem == provenance.WritingSystemId
                ? casing.LowercaseOf(form)?.Normalize(NormalizationForm.FormD) : null;
            var id = Digest(new { writingSystem = wordWritingSystem, form });
            return new FrequencyRow(id, wordWritingSystem, form, form, item.Value, lower == form ? null : lower);
        }).ToArray();
    }

    internal static string Normalize(string form)
    {
        if (string.IsNullOrWhiteSpace(form) || form.Any(char.IsControl))
            throw new InvalidDataException("A parser key or Wordform must be a single nonempty form.");
        return form.Normalize(NormalizationForm.FormD);
    }

    internal static string Digest<T>(T value) => AssessmentMaterial.Digest(JsonSerializer.Serialize(value));
}
