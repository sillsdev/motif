using System;
using System.Collections.Generic;
using Avalonia.Media;

namespace SIL.TextEditing.Avalonia
{
    /// <summary>How the field draws one kind of language-service token (ADR 0030): a chip behind the glyphs, a fill and an outline, either of which may be null,
    /// and a mute that draws the text fainter (ADR 0035: a link's GUID in source mode). The text keeps its own colour and its runs, so what the user copies,
    /// saves and undoes is the plain text it was.</summary>
    public sealed class TokenStyle
    {
        public TokenStyle(IBrush? fill, IBrush? outline = null, double mute = 0)
        {
            Fill = fill;
            Outline = outline;
            Mute = Math.Max(0, Math.Min(1, mute));
        }

        public IBrush? Fill { get; }
        public IBrush? Outline { get; }

        /// <summary>How much fainter the text is drawn, from 0 (as it is) to 1 (not seen): a wash of the field's background at this opacity is laid over the
        /// glyphs. Like chips, it is not drawn in a high contrast theme.</summary>
        public double Mute { get; }

        internal bool Draws => Fill != null || Outline != null || Mute > 0;
    }

    /// <summary>The host's map from a token kind to the way it is drawn, for the light theme and the dark one (<c>RichTextField.TokenStyles</c>). A kind with no entry is not drawn. In a
    /// high contrast theme the field draws no chips at all, so text stays in the system's colours.</summary>
    public sealed class TokenStyleMap
    {
        readonly Dictionary<string, (TokenStyle Light, TokenStyle Dark)> _styles = new Dictionary<string, (TokenStyle, TokenStyle)>(StringComparer.Ordinal);

        /// <summary>Sets the style of a kind for the light theme and, when given, the dark one (else the light one is used in both).</summary>
        public TokenStyleMap Set(string kind, TokenStyle light, TokenStyle? dark = null)
        {
            if (kind == null) throw new ArgumentNullException(nameof(kind));
            if (light == null) throw new ArgumentNullException(nameof(light));
            _styles[kind] = (light, dark ?? light);
            return this;
        }

        /// <summary>Sets a chip with this fill, for both themes.</summary>
        public TokenStyleMap Set(string kind, IBrush fill, IBrush? outline = null) => Set(kind, new TokenStyle(fill, outline));

        /// <summary>The style of a kind for a theme, or null when the map has none.</summary>
        public TokenStyle? Resolve(string kind, bool dark) => _styles.TryGetValue(kind, out var s) ? (dark ? s.Dark : s.Light) : null;

        public IEnumerable<string> Kinds => _styles.Keys;
    }
}
