using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace SIL.TextEditing.Avalonia
{
    /// <summary>The default <see cref="IClipboardBackend"/>: Avalonia's clipboard of the window the field is in. Strings are stored as text and <c>byte[]</c>
    /// payloads as raw bytes, each read back as the kind the form declares; a host object cannot be stored here (it is reported back as not stored, and a host with one supplies its own backend).</summary>
    public sealed class AvaloniaClipboardBackend : IClipboardBackend
    {
        readonly Func<TopLevel?> _topLevel;

        public AvaloniaClipboardBackend(Func<TopLevel?> topLevel)
        {
            _topLevel = topLevel ?? throw new ArgumentNullException(nameof(topLevel));
        }

        public Task<IReadOnlyList<string>> SetAsync(string plainText, IReadOnlyList<KeyValuePair<string, object>> forms) => ClipboardCompat.SetAsync(_topLevel(), plainText, forms);
        public Task<object?> GetAsync(string formatName, ClipboardPayloadKind kind) => ClipboardCompat.GetAsync(_topLevel(), formatName, kind);
        public Task<string?> GetTextAsync() => ClipboardCompat.GetTextAsync(_topLevel());
    }
}
