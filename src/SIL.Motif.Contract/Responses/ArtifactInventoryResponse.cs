namespace SIL.Motif.Contract.Responses;

/// <summary>Reports generated files and whether their ownership and references are understood.</summary>
public sealed record ArtifactInventoryResponse(
    string RootPath,
    string StoresManifestSha256,
    bool ReferenceDomainDeclaredComplete,
    bool ReferenceDomainComplete,
    bool InventoryComplete,
    long? LogicalBytes,
    IReadOnlyList<ArtifactInventoryStore> Stores,
    IReadOnlyList<ArtifactInventoryEntry> Artifacts,
    IReadOnlyList<ArtifactInventoryDiagnostic> Diagnostics);

/// <summary>Reports the snapshot result for one explicitly listed project store.</summary>
public sealed record ArtifactInventoryStore(
    string DatabasePath,
    string Status,
    string ReasonCode,
    int? SchemaVersion,
    string? SnapshotSha256);

/// <summary>Describes one exact path in a measured generated-artifact family.</summary>
public sealed record ArtifactInventoryEntry(
    string Path,
    string Family,
    long? LogicalBytes,
    long? AllocatedBytes,
    IReadOnlyList<string> OwnerInvocationIds,
    string Disposition,
    string ReasonCode,
    IReadOnlyList<string> RetainingRecordIds,
    bool ReferenceDomainComplete);

/// <summary>Explains why the inventory could not establish complete ownership or references.</summary>
public sealed record ArtifactInventoryDiagnostic(string Code, string? Path);
