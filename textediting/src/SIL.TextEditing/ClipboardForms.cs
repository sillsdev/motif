using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace SIL.TextEditing
{
    /// <summary>What a clipboard form's payload is, so the backend reads the format back as that and never guesses.</summary>
    public enum ClipboardPayloadKind
    {
        /// <summary>A string (the backend stores and returns text).</summary>
        Text,
        /// <summary>A <c>byte[]</c> (stored and returned as the same bytes, whatever they hold).</summary>
        Bytes,
        /// <summary>A host object that only the host's own backend can store.</summary>
        Object,
    }

    /// <summary>One way a selection travels on the clipboard (ADR 0029). The host registers the forms it can write and read: a
    /// FieldWorks host registers its own TsString form (a binary-serialised wrapper object under "TsString"), the adapter's TsString-XML form and the FieldWorks
    /// Markdown form (the Markdown library); plain text is built into the field and needs no registration. A payload is an <c>object</c>, handed to the
    /// <see cref="IClipboardBackend"/> untouched: a string, a <c>byte[]</c> or a host object the host's own backend can store, so the field takes no
    /// dependency on the library that writes it and a form is not limited to text.</summary>
    public interface IClipboardForm
    {
        /// <summary>The stable name the paste order and "Copy as" use, such as <see cref="ClipboardForms.TsStringId"/>.</summary>
        string Id { get; }

        /// <summary>The name a menu shows for "Copy as" (see <see cref="OffersCopyAs"/>).</summary>
        string DisplayName { get; }

        /// <summary>The clipboard format name the payload is stored under ("TsString" for FieldWorks' own format).</summary>
        string FormatName { get; }

        /// <summary>What the payload is: <see cref="ClipboardPayloadKind.Text"/> (a string), <see cref="ClipboardPayloadKind.Bytes"/> (a <c>byte[]</c>) or
        /// <see cref="ClipboardPayloadKind.Object"/> (a host object). The backend reads the format back as this kind, so bytes are never mistaken for text.</summary>
        ClipboardPayloadKind PayloadKind { get; }

        /// <summary>True when "Copy as" offers this form on its own. Only a form whose payload is text can be offered (the copy is as text):
        /// <c>RichTextField.CopyAsAsync</c> returns false for a form whose payload is not a string, so a form of bytes or objects, such as the TsString wrapper, says false.</summary>
        bool OffersCopyAs { get; }

        /// <summary>The payload for a copied selection (the fragment <see cref="DocumentEditor.GetFragment"/> returns): a string, a <c>byte[]</c> or a host object
        /// its backend stores, or null when the form cannot carry it. The field skips a form that returns null or throws; the other forms still go on the clipboard.</summary>
        object? Write(IReadOnlyList<Paragraph> fragment);

        /// <summary>The paragraphs a payload holds, or null when it is not this form's (the field then tries the next form in the paste order). The payload is
        /// what the backend read under <see cref="FormatName"/>: usually a string or a <c>byte[]</c> (<see cref="ClipboardPayload.AsText"/> gives text from either), or
        /// the host's own object. The paragraphs are new objects with no keys; the field maps writing systems the target lacks (ADR 0029) before inserting them,
        /// and the editor normalises their text. A form that throws counts as null.</summary>
        IReadOnlyList<Paragraph>? Read(object payload);
    }

    /// <summary>Where the clipboard is kept (ADR 0029): the field reads and writes it only through this, so a host can plug in its own (FieldWorks'
    /// WinForms host, which puts a serialised object under "TsString" with its own clipboard calls). <c>RichTextField</c> uses Avalonia's clipboard until a
    /// host sets <c>ClipboardBackend</c>. Payloads pass through untouched.</summary>
    public interface IClipboardBackend
    {
        /// <summary>Replaces the clipboard with the plain text and the named formats. Returns the format names it could not store (the default backend cannot store a
        /// host object), so the field can tell the host; the others were stored.</summary>
        System.Threading.Tasks.Task<IReadOnlyList<string>> SetAsync(string plainText, IReadOnlyList<KeyValuePair<string, object>> forms);

        /// <summary>The payload stored under a format name, read as the kind the form says it is (a string for <see cref="ClipboardPayloadKind.Text"/>, a <c>byte[]</c> for
        /// <see cref="ClipboardPayloadKind.Bytes"/>, the host's object for <see cref="ClipboardPayloadKind.Object"/>), or null when there is none. Must not throw for data it
        /// does not understand.</summary>
        System.Threading.Tasks.Task<object?> GetAsync(string formatName, ClipboardPayloadKind kind);

        /// <summary>The plain text on the clipboard, or null.</summary>
        System.Threading.Tasks.Task<string?> GetTextAsync();
    }

    /// <summary>Helpers for a form that carries text.</summary>
    public static class ClipboardPayload
    {
        /// <summary>The text of a <see cref="ClipboardPayloadKind.Text"/> payload: the string, or null for anything else. Bytes are never decoded by guessing: a form that
        /// carries bytes decodes them itself, knowing its encoding.</summary>
        public static string? AsText(object? payload) => payload as string;
    }

    /// <summary>The clipboard forms a field knows and the order paste reads them in (ADR 0029). Plain text is always available under
    /// <see cref="PlainTextId"/> and is written on every copy; a form is only read when its id is in <see cref="PasteOrder"/>.</summary>
    public sealed class ClipboardForms : IEnumerable<IClipboardForm>
    {
        /// <summary>The id of the built-in plain-text form in <see cref="PasteOrder"/> and in "Copy as".</summary>
        public const string PlainTextId = "text/plain";

        /// <summary>The id FieldWorks' TsString form (registered through the LCModel adapter) uses.</summary>
        public const string TsStringId = "TsString";

        /// <summary>The id the FieldWorks Markdown form (registered through the Markdown library) uses.</summary>
        public const string FieldWorksMarkdownId = "FieldWorks Markdown";

        readonly List<IClipboardForm> _forms = new List<IClipboardForm>();

        public ClipboardForms()
        {
            PasteOrder = new List<string> { TsStringId, FieldWorksMarkdownId, PlainTextId };
        }

        /// <summary>The ids paste tries, first to last: the first one the clipboard holds and that reads wins. The default is FieldWorks'
        /// own order, TsString, then FieldWorks Markdown, then plain text. The host may replace the list or edit it; an id with no
        /// registered form (and not <see cref="PlainTextId"/>) is skipped, and a form not in the list is never read.</summary>
        public IList<string> PasteOrder { get; set; }

        /// <summary>Registers a form; one with the same <see cref="IClipboardForm.Id"/> is replaced.</summary>
        public ClipboardForms Add(IClipboardForm form)
        {
            if (form == null) throw new ArgumentNullException(nameof(form));
            Remove(form.Id);
            _forms.Add(form);
            return this;
        }

        public bool Remove(string id) => _forms.RemoveAll(f => f.Id == id) > 0;

        public IClipboardForm? Find(string id) => _forms.FirstOrDefault(f => f.Id == id);

        /// <summary>The forms "Copy as" lists, in registration order, not counting plain text, which is always offered.</summary>
        public IEnumerable<IClipboardForm> CopyAsForms => _forms.Where(f => f.OffersCopyAs);

        public IEnumerator<IClipboardForm> GetEnumerator() => _forms.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>What <see cref="ClipboardWritingSystems.MapMissing"/> did.</summary>
    public sealed class WritingSystemMapping : EventArgs
    {
        public WritingSystemMapping(IReadOnlyList<string> missing, string target)
        {
            Missing = missing;
            Target = target;
        }

        /// <summary>The writing systems of the pasted text that the target lacks, each once, in the order they appear. Text with no writing-system id
        /// is mapped too but is not listed: nothing is missing, the payload just named none.</summary>
        public IReadOnlyList<string> Missing { get; }

        /// <summary>The writing system they were mapped to: the one of the selection the text replaced.</summary>
        public string Target { get; }

        /// <summary>True when any writing system was mapped (so the host may show a notice).</summary>
        public bool Any => Missing.Count > 0;
    }

    /// <summary>The writing-system rule of paste (ADR 0029, as FieldWorks does it): text in a writing system the target project lacks
    /// takes the writing system of the selection it lands in.</summary>
    public static class ClipboardWritingSystems
    {
        /// <summary>Maps every run of the fragment whose writing system <paramref name="isKnown"/> refuses, and every run with no id (mapped but not listed as missing),
        /// to <paramref name="target"/>, and every paragraph default likewise; runs that become alike merge. The fragment is
        /// changed in place, so it must be new objects (a form's <see cref="IClipboardForm.Read"/> gives that).</summary>
        public static WritingSystemMapping MapMissing(IEnumerable<Paragraph> fragment, Func<string, bool> isKnown, string target)
        {
            var missing = new List<string>();
            bool Unknown(string? ws)
            {
                string id = ws ?? string.Empty;
                if (id.Length == 0) return true;   // names no writing system: it takes the target's, and there is nothing to tell the user
                bool bad = !isKnown(id);
                if (bad && !missing.Contains(id)) missing.Add(id);
                return bad;
            }
            foreach (var p in fragment)
            {
                bool changed = false;
                foreach (var r in p.Runs)
                    if (Unknown(r.Ws)) { r.Ws = target; changed = true; }
                if (Unknown(p.DefaultWs)) p.DefaultWs = target;
                if (changed) p.Normalize();
            }
            return new WritingSystemMapping(missing, target);
        }
    }

    /// <summary>The field's own debug and test dump (<see cref="Interchange"/>) as a clipboard form. A host never registers it for
    /// users (ADR 0029 retired it from the clipboard); tests and the playground do, so that formatting travels without liblcm or the
    /// Markdown library.</summary>
    public sealed class InterchangeClipboardForm : IClipboardForm
    {
        public string Id => "SIL.TextEditing.Runs.v1";
        public string DisplayName => "Runs (debug dump)";
        public string FormatName => Interchange.ClipboardFormat;
        public bool OffersCopyAs => true;
        public ClipboardPayloadKind PayloadKind => ClipboardPayloadKind.Text;
        public object? Write(IReadOnlyList<Paragraph> fragment) => Interchange.ToXml(fragment);
        public IReadOnlyList<Paragraph>? Read(object payload) => ClipboardPayload.AsText(payload) is string xml ? Interchange.FromXml(xml) : null;
    }
}
