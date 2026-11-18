using System.Collections.Immutable;
using System.Text.Json;

namespace SIL.Motif.RecordStore;

public sealed class DeletionImpact
{
    internal DeletionImpact(ReferenceResult references, ImmutableArray<NativeReference> links,
        ImmutableArray<Guid> analyses, ImmutableArray<Guid> bundles, ImmutableArray<string> limitations)
    {
        Revision = references.Revision; Targets = references.Targets; Links = links;
        Analyses = analyses; Bundles = bundles; Witnesses = references.Witnesses;
        Limitations = references.Limitations.AddRange(limitations);
        NativeComplete = references.NativeComplete && limitations.IsEmpty;
        Complete = NativeComplete && references.UsageComplete;
    }

    public string Revision { get; }
    public ImmutableArray<Guid> Targets { get; }
    public ImmutableArray<NativeReference> Links { get; }
    public ImmutableArray<Guid> Analyses { get; }
    public ImmutableArray<Guid> Bundles { get; }
    public ImmutableArray<ReferenceWitness> Witnesses { get; }
    public ImmutableArray<string> Limitations { get; }
    public bool NativeComplete { get; }
    public bool Complete { get; }
    public long? NativeAnalysisCount => NativeComplete ? Analyses.Length : null;
    public long? NativeBundleCount => NativeComplete ? Bundles.Length : null;
    public long? NativeLinkCount => NativeComplete ? Links.Length : null;
    public long? AnalysisCount => Complete ? Analyses.Length : null;
    public long? LinkCount => Complete ? (long)Links.Length + Witnesses.Length : null;
    public bool DeleteAllowed => Complete && Links.IsEmpty && Witnesses.IsEmpty;

    /// <summary>Identifies this exact plan: revision, targets, links, analyses, bundles, witnesses and limitations.</summary>
    public string PlanToken
    {
        get
        {
            var text = string.Join('\n', new[] { Revision, Complete.ToString(), NativeComplete.ToString() }
                .Concat(Targets.Select(t => "T" + t)).Concat(Links.Select(l => "L" + l))
                .Concat(Analyses.Select(a => "A" + a)).Concat(Bundles.Select(b => "B" + b))
                .Concat(Witnesses.Select(w => "W" + w)).Concat(Limitations.Select(l => "X" + l)));
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
        }
    }

    public void RequireDeletionAllowed(string? acknowledgedPlanToken)
    {
        if (acknowledgedPlanToken is not null && acknowledgedPlanToken != PlanToken)
            throw new DeletionPlanMismatchException("The acknowledged deletion plan is not the current plan; show the warning again.");
        if (!Complete) throw new IncompleteDeletionClosureException("Deletion requires complete usage closure: " + string.Join("; ", Limitations));
        if (acknowledgedPlanToken is null && (!Links.IsEmpty || !Witnesses.IsEmpty))
            throw new DeletionPlanMismatchException("Acknowledge this plan's analysis and link warning before deletion.");
        if (!DeleteAllowed) throw new InvalidOperationException("Referenced deletion requires explicit link handling; links cannot be cleared silently.");
    }
}

public sealed partial class ReferenceIndexSnapshot
{
    private readonly NativeUsageSchema? usageSchema;

    public DeletionImpact DeletionImpact(Guid target)
    {
        var references = Incoming([target]);
        var targets = references.Targets.ToHashSet();
        var links = references.Native.Where(e => !e.Owning && !targets.Contains(e.Source)).ToImmutableArray();
        var missing = ImmutableArray.CreateBuilder<string>();
        var analyses = new HashSet<Guid>();
        var bundles = new HashSet<Guid>();
        if (usageSchema is null) missing.Add("Native analysis metadata is unavailable.");
        else
        {
            if (!records.TryGetValue(target, out var record) || !usageSchema.IsDeletable(record.ClassId))
                missing.Add("Deletion target must be a known entry, sense or allomorph.");
            foreach (var source in links.Select(e => e.Source).Distinct())
            {
                var seen = new HashSet<Guid>();
                var current = source;
                while (records.TryGetValue(current, out var header) && seen.Add(current))
                {
                    if (usageSchema.IsBundle(header.ClassId))
                    {
                        bundles.Add(current);
                        if (header.Owner is not { } owner || !records.TryGetValue(owner, out var parent) ||
                            !usageSchema.IsAnalysis(parent.ClassId) || header.OwningField != usageSchema.BundlesField)
                            missing.Add("A morph bundle has no verified owning analysis.");
                    }
                    if (usageSchema.IsAnalysis(header.ClassId)) { analyses.Add(current); break; }
                    if (header.Owner is not { } next) break;
                    current = next;
                }
            }
        }
        return new(references, links, analyses.Order().ToImmutableArray(), bundles.Order().ToImmutableArray(),
            missing.Distinct(StringComparer.Ordinal).ToImmutableArray());
    }
}

internal sealed class NativeUsageSchema
{
    private readonly Dictionary<int, int?> bases;
    private readonly Dictionary<string, int> classes;
    public int BundlesField { get; }
    private readonly Dictionary<int, int> destinations;

    private static readonly string[] Roles = ["LexEntry", "LexSense", "MoForm", "WfiAnalysis", "WfiMorphBundle", "WfiGloss", "WfiWordform"];

    private NativeUsageSchema(JsonElement schema, GenericRecord[] native, string adapterVersion)
    {
        var definitions = schema.GetProperty("classes").EnumerateArray().ToArray();
        classes = definitions.ToDictionary(c => c.GetProperty("name").GetString()!, c => c.GetProperty("id").GetInt32(), StringComparer.Ordinal);
        bases = definitions.ToDictionary(c => c.GetProperty("id").GetInt32(), c =>
            c.GetProperty("baseId").ValueKind == JsonValueKind.Null ? (int?)null : c.GetProperty("baseId").GetInt32());
        foreach (var name in Roles)
            if (!classes.ContainsKey(name)) throw new InvalidDataException("Missing native usage class.");
        destinations = schema.GetProperty("fields").EnumerateArray().Where(f => f.GetProperty("type").GetInt32() == 24)
            .ToDictionary(f => f.GetProperty("id").GetInt32(), f => f.GetProperty("destination").GetInt32());
        BundlesField = schema.GetProperty("fields").EnumerateArray().Single(f =>
            f.GetProperty("owner").GetString() == "WfiAnalysis" && f.GetProperty("name").GetString() == "MorphBundles" &&
            f.GetProperty("type").GetInt32() == 27 && !f.GetProperty("custom").GetBoolean() &&
            f.GetProperty("destination").GetInt32() == classes["WfiMorphBundle"]).GetProperty("id").GetInt32();
        foreach (var id in bases.Keys)
        {
            var seen = new HashSet<int>();
            int? current = id;
            while (current.HasValue)
            {
                if (!seen.Add(current.Value) || !bases.TryGetValue(current.Value, out current))
                    throw new InvalidDataException("Invalid native class inheritance.");
            }
        }
        var headers = native.ToDictionary(r => r.Id);
        foreach (var record in native)
        {
            if (!bases.ContainsKey(record.ClassId) || record.Provenance is not { } provenance ||
                provenance.TraversalVersion != adapterVersion ||
                Roles.Any(role => IsA(record.ClassId, role) != provenance.RepositoryOrdinals.ContainsKey(role)))
                throw new InvalidDataException("Native class metadata disagrees with captured repository membership.");
            if (IsBundle(record.ClassId) && (record.Owner is not { } owner || !headers.TryGetValue(owner, out var parent) ||
                !IsAnalysis(parent.ClassId) || record.OwningField != BundlesField))
                throw new InvalidDataException("Native morph bundle ownership disagrees with the retained schema.");
        }
    }

    public static NativeUsageSchema? Read(IEnumerable<GenericRecord> records)
    {
        try
        {
            var all = records.ToArray();
            var manifest = all.Single(r => r.Kind == "manifest");
            var basis = all.Single(r => r.Kind == "native-coverage").Values["basis"].Deserialize<NativeImportCoverage>();
            var schema = manifest.Values["schema"];
            if (basis is null || basis.Version != 1 || basis.SchemaDigest != NativeImportCoverage.ComputeSchemaDigest(schema))
                return null;
            return new NativeUsageSchema(schema, all.Where(r => r.Kind == "lcm").ToArray(), basis.AdapterVersion);
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException or
            InvalidDataException or ArgumentException or FormatException or OverflowException or JsonException) { return null; }
    }

    public bool IsDeletable(int classId) => IsA(classId, "LexEntry") || IsA(classId, "LexSense") || IsA(classId, "MoForm");
    public bool IsBundle(int classId) => IsA(classId, "WfiMorphBundle");
    public bool IsAnalysis(int classId) => IsA(classId, "WfiAnalysis");
    public bool AcceptsReference(int field, int classId) => destinations.TryGetValue(field, out var destination) && IsA(classId, destination);

    private bool IsA(int classId, string name) => IsA(classId, classes[name]);

    private bool IsA(int classId, int ancestor)
    {
        int? current = classId;
        while (current.HasValue && bases.TryGetValue(current.Value, out var parent))
        {
            if (current.Value == ancestor) return true;
            current = parent;
        }
        return false;
    }
}
