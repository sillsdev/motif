using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SIL.TextEditing.Project
{
    /// <summary>The <c>base:</c> value of an exported record: <c>sha256:</c> and 64 lowercase hex digits over the record file's text (without any <c>base:</c> line) and
    /// every <see cref="UnexportedValue"/> of the export, so that a change to a value the file cannot show (a timestamp, say) also changes the digest. The text is
    /// hashed as UTF-8 bytes; the unexported values are hashed sorted, each of its six parts as a length in four bytes and its UTF-8 bytes (a null value as length -1).</summary>
    internal static class RecordBaseDigest
    {
        public const string Prefix = "sha256:";

        public static string Compute(string text, IReadOnlyList<UnexportedValue> unexported)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, new UTF8Encoding(false)))
            {
                Part(writer, text);
                writer.Write(unexported.Count);
                foreach (var u in unexported.OrderBy(u => u.Object).ThenBy(u => u.Class, StringComparer.Ordinal).ThenBy(u => u.Field, StringComparer.Ordinal)
                    .ThenBy(u => u.Reason, StringComparer.Ordinal).ThenBy(u => u.Value, StringComparer.Ordinal))
                {
                    Part(writer, u.Object.ToString("D"));
                    Part(writer, u.Class);
                    Part(writer, u.Field);
                    Part(writer, u.Reason);
                    Part(writer, u.Value);
                }
                writer.Flush();
                using (var sha = SHA256.Create())
                    return Prefix + string.Concat(sha.ComputeHash(stream.ToArray()).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
            }
        }

        static void Part(BinaryWriter writer, string? value)
        {
            if (value == null) { writer.Write(-1); return; }
            var bytes = Encoding.UTF8.GetBytes(value);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }
    }
}
