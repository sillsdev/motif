using System.Text;

namespace SIL.Motif.RecordStore;

internal sealed class BinaryOutput : IDisposable
{
    private readonly MemoryStream stream = new();
    public void Byte(byte value) => stream.WriteByte(value);
    public void Unsigned(ulong value)
    {
        while (value >= 128) { Byte((byte)(value | 128)); value >>= 7; }
        Byte((byte)value);
    }
    public void Integer(long value) => Unsigned(unchecked((ulong)((value << 1) ^ (value >> 63))));
    public void Guid(Guid value)
    {
        Span<byte> buffer = stackalloc byte[16];
        value.TryWriteBytes(buffer, bigEndian: true, out _);
        stream.Write(buffer);
    }
    public void Bytes(byte[] value) { Unsigned((ulong)value.Length); stream.Write(value); }
    public void Text(string value) => Bytes(Encoding.UTF8.GetBytes(value));
    public byte[] Finish() => stream.ToArray();
    /// <summary>The bytes written so far, without a copy; valid until the next write or reset.</summary>
    public ReadOnlySpan<byte> Written => stream.GetBuffer().AsSpan(0, (int)stream.Length);
    public void Reset() => stream.SetLength(0);
    public void Append(ReadOnlySpan<byte> value) => stream.Write(value);
    public void Dispose() => stream.Dispose();
}

internal sealed class BinaryInput(byte[] bytes)
{
    private int position;
    public byte Byte()
    {
        if (position == bytes.Length) throw new InvalidDataException("Truncated record payload.");
        return bytes[position++];
    }
    public ulong Unsigned()
    {
        ulong value = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            var next = Byte();
            if (shift == 63 && next > 1) throw new InvalidDataException("Overflow in record payload.");
            value |= (ulong)(next & 127) << shift;
            if (next < 128) return value;
        }
        throw new InvalidDataException("Invalid record integer.");
    }
    public long Integer() { var n = Unsigned(); return unchecked((long)(n >> 1) ^ -(long)(n & 1)); }
    public int Count()
    {
        var count = Unsigned();
        if (count > int.MaxValue || count > (ulong)bytes.Length) throw new InvalidDataException("Invalid record count.");
        return (int)count;
    }
    public Guid Guid() => new(Take(16), bigEndian: true);
    public byte[] Bytes() => Take(Count()).ToArray();
    public string Text() => new UTF8Encoding(false, true).GetString(Bytes());
    private ReadOnlySpan<byte> Take(int count)
    {
        if (count > bytes.Length - position) throw new InvalidDataException("Truncated record payload.");
        var result = bytes.AsSpan(position, count);
        position += count;
        return result;
    }
    public void End()
    {
        if (position != bytes.Length) throw new InvalidDataException("Trailing record payload.");
    }
}
