using System.Buffers.Binary;
using System.Security.Cryptography;

namespace SIL.Motif.Worker.Sources;

internal sealed class CorpusShardDigest : IDisposable
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    public void Append(long ordinal, long form, string? formKey, int segment, int word, long? offset, int? length,
        string? verse, string? surface)
    {
        Span<byte> numbers = stackalloc byte[36];
        BinaryPrimitives.WriteInt64LittleEndian(numbers, ordinal);
        BinaryPrimitives.WriteInt64LittleEndian(numbers[8..], form);
        BinaryPrimitives.WriteInt32LittleEndian(numbers[16..], segment);
        BinaryPrimitives.WriteInt32LittleEndian(numbers[20..], word);
        BinaryPrimitives.WriteInt64LittleEndian(numbers[24..], offset ?? -1);
        BinaryPrimitives.WriteInt32LittleEndian(numbers[32..], length ?? -1);
        _hash.AppendData(numbers);
        Append(formKey);
        Append(verse);
        Append(surface);
    }

    private void Append(string? text)
    {
        Span<byte> length = stackalloc byte[4];
        var count = text is null ? -1 : CorpusContent.Utf8.GetByteCount(text);
        BinaryPrimitives.WriteInt32LittleEndian(length, count);
        _hash.AppendData(length);
        if (count <= 0) return;
        if (count > 128)
        {
            _hash.AppendData(CorpusContent.Utf8.GetBytes(text!));
            return;
        }
        Span<byte> bytes = stackalloc byte[count];
        CorpusContent.Utf8.GetBytes(text, bytes);
        _hash.AppendData(bytes);
    }

    public string Complete() => Convert.ToHexStringLower(_hash.GetHashAndReset());

    public void Dispose() => _hash.Dispose();
}
