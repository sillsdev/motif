using SIL.Motif.Contract.Responses;
using System;
using System.Collections.Generic;
using System.IO;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Store;
using SIL.Motif.Projection;
using SIL.Motif.Projection.Usage;

namespace SIL.Motif.Commands;

/// <summary>
/// The CLI verbs that get text into Motif: <c>add-corpus</c>, <c>add-document</c>,
/// <c>add-corpus-bundle</c>, and the two read-only verbs for seeing what is there.
/// </summary>
/// <remarks>
/// <para>
/// <b>In plain terms:</b> a linguist — or more often a script — says "here is a body of text, here is where it
/// came from, and here is what its licence allows", and Motif takes it in and hashes it. Nothing measures a
/// grammar's reach until text has arrived this way, because a number computed over text of unknown origin
/// cannot be published from or reproduced.
/// </para>
/// <para>
/// The bundle verb is the one a program uses; the other two are what a person uses for a single file.
/// </para>
/// </remarks>
public static class CorpusCommands
{
    /// <summary>The Corpus store for one project — over the same gated database a verb already opened.</summary>
    public static ICorpusStore StoreFor(MotifDatabase database) => new SqliteCorpusStore(database);

    /// <summary>Create an empty Corpus with its origin and tokenisation recorded.</summary>
    public static CommandOutcome<CorpusAddedResponse> AddCorpus(AddCorpusRequest request)
    {
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
        {
            try
            {
                var ingestion = new CorpusIngestion(StoreFor(database));

                var provenance = new CorpusProvenance(
                    new CorpusOrigin(
                        request.Description, request.Uri, request.RetrievedUtc ?? DateTimeOffset.UtcNow,
                        request.Licence, request.Capabilities),
                    new TokenisationRecord(request.Tokeniser, request.TokeniserVersion, request.TokeniserNotes ?? ""));

                ingestion.AddCorpus(request.CorpusId, provenance);

                var derivationNote = request.Capabilities.PermitsDerivedArtefacts
                    ? "Derived works (n-gram models, published word lists) are permitted. Basis: " +
                        request.Capabilities.Basis
                    : request.Capabilities.WhyDerivedArtefactsAreNotPermitted(request.Description);

                return CommandOutcome<CorpusAddedResponse>.Success(new CorpusAddedResponse(
                    request.CorpusId, request.Description, request.Uri, request.Licence, request.Tokeniser,
                    request.TokeniserVersion, derivationNote));
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                return CommandOutcome<CorpusAddedResponse>.Refused(new Refusal(
                    "corpus.invalid", FailureReason.InvalidArgument, ex.Message,
                    Fact(("corpusId", request.CorpusId))));
            }
        });
    }

    /// <summary>Add one Document to a Corpus, from a file on disk or a URL to retrieve.</summary>
    public static CommandOutcome<CorpusDocumentAddedResponse> AddDocument(AddDocumentRequest request)
    {
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
        {
            try
            {
                var ingestion = new CorpusIngestion(StoreFor(database));
                var source = DocumentSource.Parse(request.FileOrUrl);

                var document = ingestion.AddDocumentAsync(
                    request.CorpusId,
                    source,
                    new DocumentMetadata(
                        request.DocumentId, request.Title ?? request.DocumentId, request.Licence,
                        request.Capabilities)).GetAwaiter().GetResult();

                return CommandOutcome<CorpusDocumentAddedResponse>.Success(new CorpusDocumentAddedResponse(
                    request.CorpusId, document.DocumentId, document.Title, source.Describe(),
                    document.Text.Length, document.ContentSha256, document.Licence));
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException)
            {
                return CommandOutcome<CorpusDocumentAddedResponse>.Refused(new Refusal(
                    "corpus.document-invalid", FailureReason.InvalidArgument, ex.Message,
                    Fact(("corpusId", request.CorpusId), ("documentId", request.DocumentId))));
            }
        });
    }

    /// <summary>Take in a whole handoff bundle written by a fetching tool.</summary>
    public static CommandOutcome<CorpusBundleAddedResponse> AddBundle(AddCorpusBundleRequest request)
    {
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
        {
            try
            {
                var bundle = CorpusBundle.ReadFile(request.BundlePath);
                var ingestion = new CorpusIngestion(StoreFor(database));
                var corpus = ingestion.AddBundleAsync(bundle).GetAwaiter().GetResult();

                var accuracyNote = corpus.Provenance.SupportsAccuracyClaims
                    ? "This corpus is attested, so accuracy figures may be computed over it."
                    : corpus.Provenance.WhyAccuracyIsNotComputable();

                return CommandOutcome<CorpusBundleAddedResponse>.Success(new CorpusBundleAddedResponse(
                    corpus.CorpusId, corpus.Documents.Count, corpus.Provenance.Origin.Description,
                    corpus.Provenance.Origin.Licence, corpus.DescribeDerivationRestrictions(), accuracyNote));
            }
            catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException
                                          or ArgumentException or IOException)
            {
                return CommandOutcome<CorpusBundleAddedResponse>.Refused(new Refusal(
                    "corpus.bundle-invalid", FailureReason.InvalidArgument, ex.Message,
                    Fact(("bundlePath", request.BundlePath))));
            }
        });
    }

    /// <summary>Every stored Corpus, with its size and what may be done with it.</summary>
    public static CommandOutcome<CorpusListProjection> ListCorpora(ListCorporaRequest request, UsageLog? usage = null)
    {
        usage?.Record("corpora", new[] { UsageArgumentShape.Text("fwDataPath") });
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
            CommandOutcome<CorpusListProjection>.Success(BuildCorpusList(database)));
    }

    private static CorpusListProjection BuildCorpusList(MotifDatabase database)
        => CorpusProjectionQuery.List(StoreFor(database));

    /// <summary>One Corpus in full: provenance, every Document, and what each is licensed for.</summary>
    public static CommandOutcome<CorpusDetailProjection> ShowCorpus(ShowCorpusRequest request, UsageLog? usage = null)
    {
        usage?.Record(
            "show-corpus",
            new[] { UsageArgumentShape.Text("fwDataPath"), UsageArgumentShape.Text("corpusId") });
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
        {
            var projection = CorpusProjectionQuery.Detail(StoreFor(database), request.CorpusId);
            return projection is not null
                ? CommandOutcome<CorpusDetailProjection>.Success(projection)
                : CommandOutcome<CorpusDetailProjection>.Refused(new Refusal(
                    "corpus.not-found", FailureReason.InvalidArgument,
                    $"No corpus '{request.CorpusId}' in store.", Fact(("corpusId", request.CorpusId))));
        });
    }

    /// <summary>
    /// Build capabilities from CLI flags, defaulting to "nothing established" rather than to permission.
    /// </summary>
    public static LicenceCapabilities CapabilitiesFromFlags(IReadOnlyDictionary<string, string> flags)
    {
        var basis = flags.GetValueOrDefault("licence-basis");

        var any = flags.ContainsKey("may-derive")
                  || flags.ContainsKey("may-redistribute")
                  || flags.ContainsKey("may-use-commercially")
                  || flags.ContainsKey("requires-attribution")
                  || basis is not null;

        if (!any) return LicenceCapabilities.Unknown();

        return new LicenceCapabilities(
            MayRedistribute: Tri(flags, "may-redistribute"),
            MayDerive: Tri(flags, "may-derive"),
            MayUseCommercially: Tri(flags, "may-use-commercially"),
            RequiresAttribution: Tri(flags, "requires-attribution") ?? true,
            Basis: basis ?? "stated on the command line, source unrecorded");

        static bool? Tri(IReadOnlyDictionary<string, string> f, string name) =>
            f.TryGetValue(name, out var v) && bool.TryParse(v, out var parsed) ? parsed : null;
    }

    private static Dictionary<string, string> Fact(params (string Key, string? Value)[] entries)
    {
        var facts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in entries)
        {
            if (value is not null) facts[key] = value;
        }
        return facts;
    }
}
