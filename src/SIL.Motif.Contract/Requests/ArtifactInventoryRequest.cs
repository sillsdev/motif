namespace SIL.Motif.Contract.Requests;

/// <summary>Names the exact worker root and store manifest for a read-only artifact inventory.</summary>
public sealed record ArtifactInventoryRequest(string RootPath, string StoresManifestPath);
