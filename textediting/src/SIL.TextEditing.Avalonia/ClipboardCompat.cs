using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;

namespace SIL.TextEditing.Avalonia
{
    /// <summary>The one place where Avalonia 11.3 and 12 differ for this editor: the clipboard API.
    /// Everything else compiles unchanged for both.</summary>
    public static class ClipboardCompat
    {
        // One pair of format objects per clipboard format name: a string reader (how our own writer round-trips) and a byte reader (how
        // a native writer on Windows hands a custom payload over).
        static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DataFormat<string> Text, DataFormat<byte[]> Bytes)> Formats =
            new System.Collections.Concurrent.ConcurrentDictionary<string, (DataFormat<string>, DataFormat<byte[]>)>();

        static (DataFormat<string> Text, DataFormat<byte[]> Bytes) FormatsFor(string name) =>
            Formats.GetOrAdd(name, n => (DataFormat.CreateStringPlatformFormat(n), DataFormat.CreateBytesPlatformFormat(n)));

        /// <summary>Puts plain text and any number of named formats on the clipboard together: a string goes in as text, a <c>byte[]</c> as raw bytes. A host
        /// object cannot go through Avalonia's clipboard (a host with such objects plugs in its own <see cref="IClipboardBackend"/>); it is left out and its format name returned.</summary>
        public static async Task<IReadOnlyList<string>> SetAsync(TopLevel? topLevel, string plain, IEnumerable<KeyValuePair<string, object>>? forms = null)
        {
            var refused = new List<string>();
            var clipboard = topLevel?.Clipboard;
            if (clipboard == null) return refused;
            var transfer = new DataTransfer();
            var item = new DataTransferItem();
            item.SetText(plain);
            if (forms != null)
            {
                foreach (var form in forms)
                {
                    if (form.Value is string text) item.Set(FormatsFor(form.Key).Text, text);
                    else if (form.Value is byte[] bytes) item.Set(FormatsFor(form.Key).Bytes, bytes);
                    else
                    {
                        System.Diagnostics.Trace.TraceWarning("Clipboard format " + form.Key + " holds a host object, which Avalonia's clipboard cannot store; left out");
                        refused.Add(form.Key);
                    }
                }
            }
            transfer.Add(item);
            await clipboard.SetDataAsync(transfer).ConfigureAwait(true);
            return refused;
        }

        /// <summary>The payload under a format name, read as the kind the form says it is: a string for text, a <c>byte[]</c> for bytes (the raw bytes, never decoded as text),
        /// null for an object (Avalonia's clipboard cannot hold one) or when there is none. Never throws.</summary>
        public static async Task<object?> GetAsync(TopLevel? topLevel, string formatName, ClipboardPayloadKind kind)
        {
            var clipboard = topLevel?.Clipboard;
            if (clipboard == null || kind == ClipboardPayloadKind.Object) return null;
            var (format, formatBytes) = FormatsFor(formatName);
            try
            {
                if (kind == ClipboardPayloadKind.Text) return await clipboard.TryGetValueAsync(format).ConfigureAwait(true);
                return await clipboard.TryGetValueAsync(formatBytes).ConfigureAwait(true);
            }
            catch (Exception) { return null; }
        }

        /// <summary>The text stored under a format name, or null.</summary>
        public static async Task<string?> GetStringAsync(TopLevel? topLevel, string formatName) =>
            await GetAsync(topLevel, formatName, ClipboardPayloadKind.Text).ConfigureAwait(true) as string;

        public static async Task<string?> GetTextAsync(TopLevel? topLevel)
        {
            var clipboard = topLevel?.Clipboard;
            return clipboard == null ? null : await clipboard.TryGetTextAsync().ConfigureAwait(true);
        }
    }
}
