using System;
using System.Collections.Generic;
using System.Linq;

namespace SIL.TextEditing.Tests
{
    /// <summary>A stand-in for a host's clipboard form (a FieldWorks Markdown form is written by the Markdown library, which the field
    /// does not reference). It writes "[tag]" plus the paragraphs' text joined with "|", and reads that back: every paragraph one run
    /// in <see cref="ReadWs"/> (or the writing system the payload names after the tag, "[tag:ws]").</summary>
    public sealed class FakeClipboardForm : IClipboardForm
    {
        readonly string _tag;

        public FakeClipboardForm(string id, bool offersCopyAs = true, string? formatName = null)
        {
            Id = id;
            DisplayName = id;
            FormatName = formatName ?? "test/" + id;
            OffersCopyAs = offersCopyAs;
            _tag = "[" + id + "]";
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string FormatName { get; }
        public bool OffersCopyAs { get; }
        public ClipboardPayloadKind PayloadKind => ClipboardPayloadKind.Text;
        public bool Throws { get; set; }
        public bool WritesNothing { get; set; }
        public string ReadWs { get; set; } = "en";
        public int Reads { get; private set; }

        public object? Write(IReadOnlyList<Paragraph> fragment)
        {
            if (Throws) throw new InvalidOperationException("fake form cannot write");
            return WritesNothing ? null : _tag + string.Join("|", fragment.Select(p => p.Text));
        }

        public IReadOnlyList<Paragraph>? Read(object payloadObject)
        {
            Reads++;
            if (Throws) throw new InvalidOperationException("fake form cannot read");
            var payload = ClipboardPayload.AsText(payloadObject);
            if (payload == null) return null;
            if (!payload.StartsWith(_tag, StringComparison.Ordinal)) return null;
            return payload.Substring(_tag.Length).Split('|').Select(t =>
            {
                var p = new Paragraph { DefaultWs = ReadWs };
                p.Runs.Add(new Run { Ws = ReadWs, Text = t });
                return p;
            }).ToList();
        }
    }
}
