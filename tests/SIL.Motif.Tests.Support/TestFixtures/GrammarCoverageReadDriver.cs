using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Corpus;
using SIL.Motif.Worker.Corpus.Adapter;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>Exercises the persisted stamp, seek page and typed refusals through the storage seam.</summary>
public sealed class GrammarCoverageReadDriver(ProjectLocator project)
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter<FailureReason>() },
    };

    private readonly IGrammarCoverageReader _reader = new GrammarCoverageStore(project);

    public JsonElement ReadSummary(string stamp, CancellationToken cancellationToken = default) =>
        Read(() => GrammarCoverageWire.Summary(_reader.ReadSummary(stamp, cancellationToken)));

    public JsonElement ReadNoAnalysis(string stamp, int limit = 50, string? cursor = null,
        CancellationToken cancellationToken = default) =>
        Read(() => _reader.ReadNoAnalysis(stamp, new PageRequest(limit, cursor), cancellationToken));

    private static JsonElement Read<T>(Func<T> read)
    {
        try { return JsonSerializer.SerializeToElement(read(), Wire); }
        catch (GrammarCoverageReadException exception)
        {
            var (reason, code) = exception.Failure switch
            {
                GrammarCoverageReadFailure.Unavailable => (FailureReason.NotFound, "corpus.index-unavailable"),
                GrammarCoverageReadFailure.ExpiredPin => (FailureReason.NotFound, "corpus.revision-unavailable"),
                GrammarCoverageReadFailure.Busy => (FailureReason.Busy, "project.busy"),
                GrammarCoverageReadFailure.InvalidCursor => (FailureReason.InvalidArgument, "corpus.invalid-cursor"),
                _ => (FailureReason.InvalidArgument, "corpus.invalid-request"),
            };
            return JsonSerializer.SerializeToElement(new FailureEnvelope(reason, exception.Message, code: code), Wire);
        }
    }
}
