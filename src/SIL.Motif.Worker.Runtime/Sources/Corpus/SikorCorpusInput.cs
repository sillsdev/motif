using System.Numerics;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;

namespace SIL.Motif.Worker.Sources;

internal sealed class SikorCorpusInput
{
    private readonly HashSet<string> _devIds;
    private readonly string _path;
    private readonly string _scope;
    private readonly byte[] _key;

    public string SplitDigest { get; }
    public string KeyId => Protect("key", "corpus-source");
    public string Protect(string kind, string value) => CorpusContent.PrivateHash(_key, kind, _scope, value);

    public SikorCorpusInput(string path, string scope, CorpusSikor split)
    {
        _path = path;
        _scope = scope;
        _key = split.WitnessKey.ToArray();
        _devIds = new HashSet<string>(split.DevIds, StringComparer.Ordinal);
        SplitDigest = CorpusContent.Digest(_devIds.Order(StringComparer.Ordinal).ToArray());
        if (_key.Length < 32 || _devIds.Count == 0 || _devIds.Count != split.DevIds.Length ||
            !string.Equals(SplitDigest, split.DevIdsSha256, StringComparison.OrdinalIgnoreCase) ||
            _devIds.Any(id => string.IsNullOrWhiteSpace(id) || !IsDevBucket(id)))
            throw new InvalidDataException("The SIKOR Source requires a pinned dev-ID list, hash split and a private witness key.");
    }

    private static bool IsDevBucket(string id)
    {
        var digest = SHA256.HashData(CorpusContent.Utf8.GetBytes(id));
        return new BigInteger(digest, isUnsigned: true, isBigEndian: true) % 10 >= 2;
    }

    public IEnumerable<CorpusInputDocument> Read(CancellationToken cancellationToken)
    {
        using var input = File.OpenRead(_path);
        using var xml = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var selected = 0;
        if (xml.MoveToContent() != XmlNodeType.Element || xml.LocalName != "corpus")
            throw InvalidSource();
        var rootDepth = xml.Depth;
        xml.Read();
        while (!xml.EOF)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (xml.NodeType != XmlNodeType.Element || xml.LocalName != "text" || xml.Depth != rootDepth + 1)
            {
                xml.Read();
                continue;
            }
            var id = xml.GetAttribute("id") ?? xml.GetAttribute("id", XNamespace.Xml.NamespaceName);
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
                throw InvalidSource();
            if (!_devIds.Contains(id))
            {
                xml.Skip();
                continue;
            }

            if (string.Equals(xml.GetAttribute("gt_domain"), "bible", StringComparison.OrdinalIgnoreCase)) throw InvalidSource();
            var tokens = ReadWords(xml, cancellationToken);
            var opaque = Protect("document", id);
            var hash = Protect("document-words", CorpusContent.Digest(tokens));
            yield return new(new(opaque, "", ++selected, hash, hash, null),
                tokens.Select((token, index) => new CorpusSegment(index + 1, token)));
            xml.Read();
        }
        if (selected != _devIds.Count)
            throw new InvalidDataException("The SIKOR Source is missing one or more pinned dev IDs.");
    }

    private static List<string> ReadWords(XmlReader xml, CancellationToken cancellationToken)
    {
        var tokens = new List<string>();
        using var subtree = xml.ReadSubtree();
        while (subtree.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (subtree.NodeType != XmlNodeType.Element) continue;
            if (subtree.LocalName == "text" && subtree.Depth != 0) throw InvalidSource();
            if (subtree.LocalName == "w")
                tokens.Add(subtree.GetAttribute("word") ?? throw InvalidSource());
        }
        return tokens;
    }

    private static InvalidDataException InvalidSource() =>
        new("The SIKOR Source has invalid text IDs or authorized word fields.");
}
