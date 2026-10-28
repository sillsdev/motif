using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// Turns coverlet hit indices back into source files: each injected <c>ldc.i4 index; call RecordHit</c> pair sits in
/// the method whose portable PDB names the file.
/// </summary>
internal sealed class CoverageSourceResolver(string root)
{
    /// <summary>The namespace coverlet gives the tracker type it adds to every instrumented module.</summary>
    internal const string TrackerNamespace = "Coverlet.Core.Instrumentation.Tracker";

    private const byte LoadInt32 = 0x20;
    private const byte LoadInt8 = 0x1F;
    private const byte Call = 0x28;

    /// <summary>Maps each of <paramref name="indices"/> to the repository-relative source files of its method.</summary>
    internal Dictionary<int, string[]> Resolve(Assembly assembly, HashSet<int> indices)
    {
        using var stream = File.OpenRead(assembly.Location);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var hitTokens = HitMethodTokens(metadata);
        if (!pe.TryOpenAssociatedPortablePdb(assembly.Location,
                path => File.Exists(path) ? File.OpenRead(path) : null, out var pdbProvider, out _) || pdbProvider is null)
            throw new InvalidOperationException($"No portable PDB for {assembly.Location}; the coverage map needs one.");
        using var pdbProviderScope = pdbProvider;
        var pdb = pdbProvider.GetMetadataReader();

        var result = new Dictionary<int, string[]>();
        var cache = new Dictionary<MethodDefinitionHandle, string[]>();
        foreach (var handle in metadata.MethodDefinitions)
        {
            var definition = metadata.GetMethodDefinition(handle);
            if (definition.RelativeVirtualAddress == 0) continue;
            var il = pe.GetMethodBody(definition.RelativeVirtualAddress).GetILBytes();
            if (il is null) continue;
            for (var at = 1; at + 4 < il.Length; at++)
            {
                if (il[at] != Call || !hitTokens.Contains(BitConverter.ToInt32(il, at + 1)) || !TryReadIndex(il, at, out var index)) continue;
                if (!indices.Contains(index)) continue;
                if (!cache.TryGetValue(handle, out var files)) cache[handle] = files = FilesOf(pdb, handle);
                result[index] = files;
            }
        }
        return result;
    }

    // The constant pushed just before the call, in whichever ldc.i4 encoding the instrumenter left.
    private static bool TryReadIndex(byte[] il, int callAt, out int index)
    {
        index = 0;
        if (callAt >= 5 && il[callAt - 5] == LoadInt32) { index = BitConverter.ToInt32(il, callAt - 4); return true; }
        if (callAt >= 2 && il[callAt - 2] == LoadInt8) { index = (sbyte)il[callAt - 1]; return true; }
        if (callAt >= 1 && il[callAt - 1] is >= 0x16 and <= 0x1E) { index = il[callAt - 1] - 0x16; return true; }
        return false;
    }

    // Instrumented code calls the tracker through a member reference, so the call tokens come from that table.
    private static HashSet<int> HitMethodTokens(MetadataReader metadata)
    {
        var tokens = new HashSet<int>();
        foreach (var handle in metadata.MemberReferences)
        {
            var name = metadata.GetString(metadata.GetMemberReference(handle).Name);
            if (name is "RecordHit" or "RecordSingleHit") tokens.Add(MetadataTokens.GetToken(handle));
        }
        return tokens;
    }

    private string[] FilesOf(MetadataReader pdb, MethodDefinitionHandle handle)
    {
        var info = pdb.GetMethodDebugInformation(handle.ToDebugInformationHandle());
        var documents = new HashSet<DocumentHandle>();
        if (!info.Document.IsNil) documents.Add(info.Document);
        foreach (var point in info.GetSequencePoints())
            if (!point.Document.IsNil) documents.Add(point.Document);
        return documents
            .Select(document => pdb.GetString(pdb.GetDocument(document).Name))
            .Select(name => Path.GetRelativePath(root, name).Replace('\\', '/'))
            .ToArray();
    }
}
