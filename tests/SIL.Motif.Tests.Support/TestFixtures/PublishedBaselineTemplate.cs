using System.Collections.Concurrent;
using System.Security.Cryptography;
using SIL.LCModel;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Texts;
using SIL.Motif.Worker.Baselines;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// A published Baseline folder built once per name and process (once per gate through <see cref="FixtureDiskCache"/>),
/// which each test copies into its own private folder instead of re-seeding, saving and publishing the same project.
/// </summary>
/// <remarks>
/// <para>
/// The folder is made from the pristine master by <c>prepare</c>, saved, published with
/// <see cref="PublishedBaselineFixture"/>, and never opened for writing again. A copy is a plain file copy of about 100 KB
/// on disk; the resident cost is one <see cref="TextWordsProjection"/> and <see cref="ProjectSummarySnapshot"/> per
/// template, read once from the published project and shared as read-only values.
/// </para>
/// <para>
/// The cache folder is keyed by the assembly that holds <c>prepare</c> as well as by <see cref="FixtureDiskCache"/>'s own
/// input hash, so editing the preparing test class builds a fresh template instead of reusing a stale one.
/// </para>
/// </remarks>
internal sealed class PublishedBaselineTemplate<T> where T : class
{
    private static readonly ConcurrentDictionary<string, Lazy<PublishedBaselineTemplate<T>>> Templates = new();
    private readonly string _root;
    private readonly Lazy<(TextWordsProjection Projection, ProjectSummarySnapshot Summary)> _evidence;

    private PublishedBaselineTemplate(string root, T metadata)
    {
        _root = root;
        Metadata = metadata;
        _evidence = new(() =>
        {
            using var cache = new FwDataProjectLoader().LoadScratchCache(PublishedFwDataPathIn(_root));
            return (TextWordsProjectionBuilder.Build(cache, CancellationToken.None), ProjectSummaryReader.Read(cache));
        });
    }

    /// <summary>What <c>prepare</c> returned: identities the tests need to name what it wrote.</summary>
    public T Metadata { get; }

    /// <summary>The projection of the published project, shared and never mutated.</summary>
    public TextWordsProjection Projection => _evidence.Value.Projection;

    /// <summary>The summary of the published project, shared and never mutated.</summary>
    public ProjectSummarySnapshot Summary => _evidence.Value.Summary;

    /// <summary>Returns the template called <paramref name="name"/>, building it from <paramref name="pristine"/> once.</summary>
    public static PublishedBaselineTemplate<T> Get(string name, PristineProjectFixture pristine, Func<LcmCache, T> prepare) =>
        Templates.GetOrAdd(name, _ => new Lazy<PublishedBaselineTemplate<T>>(() => Build(name, pristine, prepare))).Value;

    /// <summary>Copies the published folder to <paramref name="destination"/> and returns the copy's <c>.fwdata</c> path.</summary>
    public string CopyTo(string destination)
    {
        CopyDirectory(_root, destination);
        return PublishedFwDataPathIn(destination);
    }

    private static PublishedBaselineTemplate<T> Build(string name, PristineProjectFixture pristine, Func<LcmCache, T> prepare)
    {
        var built = FixtureDiskCache.GetOrBuild("published-" + name + "-" + AssemblyKey(prepare), staging =>
        {
            using var cache = pristine.NewScratch();
            var metadata = prepare(cache);
            new FwDataProjectLoader().Save(cache);
            PublishedBaselineFixture.PublishAsync(cache, Path.Combine(staging, "published")).GetAwaiter().GetResult();
            return metadata;
        });
        if (!built.Shared)
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try { Directory.Delete(built.Root, recursive: true); }
                catch { /* best effort */ }
            };
        }
        return new PublishedBaselineTemplate<T>(Path.Combine(built.Root, "published"), built.Metadata);
    }

    private static string PublishedFwDataPathIn(string root) =>
        Path.Combine(root, NewLangProjFixture.ProjectName + ".fwdata");

    private static string AssemblyKey(Delegate prepare)
    {
        var path = prepare.Method.Module.Assembly.Location;
        var stamp = Path.GetFileName(path) + new FileInfo(path).Length + File.GetLastWriteTimeUtc(path).Ticks;
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(stamp)))[..12].ToLowerInvariant();
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}
