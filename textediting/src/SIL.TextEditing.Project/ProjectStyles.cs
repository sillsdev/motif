using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.DomainServices;
using SIL.TextEditing.LCModel;
using SIL.TextEditing.Markdown;

namespace SIL.TextEditing.Project
{
    /// <summary>
    /// Reads a project's paragraph and character styles, with based-on inheritance and writing-system overrides.
    /// The caller owns the cache and serializes calls on its owning thread while it is alive. Reads never open a unit
    /// of work or change the project. Returned bags and lists are detached; invalidate field layouts after style edits.
    /// </summary>
    public sealed class ProjectStyles : IStyleResolver, IStyleCatalog, IKnownStyles
    {
        readonly LcmCache _cache;
        readonly IWritingSystemHandles _handles;

        public ProjectStyles(LcmCache cache)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _handles = WritingSystemHandles.FromFactory(cache.WritingSystemFactory);
        }

        IStStyle? Find(string name) => _cache.LangProject.StylesOC.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));

        /// <inheritdoc />
        public bool IsParagraphStyle(string name) => Find(name)?.Type == StyleType.kstParagraph;

        /// <inheritdoc />
        public bool IsCharacterStyle(string name) => Find(name)?.Type == StyleType.kstCharacter;

        /// <inheritdoc />
        public IReadOnlyList<string> ParagraphStyleNames => Names(StyleType.kstParagraph);

        /// <inheritdoc />
        public IReadOnlyList<string> CharacterStyleNames => Names(StyleType.kstCharacter);

        string[] Names(StyleType type) => _cache.LangProject.StylesOC.Where(s => s.Type == type).Select(s => s.Name).ToArray();

        /// <summary>
        /// Returns effective properties, or null for an absent style. An unknown writing-system id uses the style's
        /// common properties. Malformed based-on cycles throw <see cref="InvalidOperationException"/>.
        /// </summary>
        public PropertyBag? Resolve(string styleName, string ws)
        {
            if (Find(styleName) == null) return null;
            CheckInheritance();
            var sheet = new LcmStyleSheet();
            sheet.Init(_cache, _cache.LangProject.Hvo, LangProjectTags.kflidStyles);
            var properties = sheet.GetStyleRgch(styleName.Length, styleName);
            var bag = TsStringConversion.ToBag(properties);
            int handle = _handles.GetHandle(ws);
            var font = handle == 0 ? null : sheet.Style(styleName)?.OverrideCharacterStyleInfo(handle);
            if (font != null)
            {
                String(bag, FwTextPropType.ktptFontFamily, font.FontName);
                String(bag, FwTextPropType.ktptFontVariations, font.Features);
                Integer(bag, FwTextPropType.ktptFontSize, 1, font.FontSize, v => v);
                Integer(bag, FwTextPropType.ktptOffset, 1, font.Offset, v => v);
                Integer(bag, FwTextPropType.ktptBold, 3, font.Bold, v => v ? 1 : 0);
                Integer(bag, FwTextPropType.ktptItalic, 3, font.Italic, v => v ? 1 : 0);
                Integer(bag, FwTextPropType.ktptSuperscript, 3, font.SuperSub, v => (int)v);
                Integer(bag, FwTextPropType.ktptUnderline, 3, font.Underline, v => (int)v);
                Integer(bag, FwTextPropType.ktptForeColor, 0, font.FontColor, Bgr);
                Integer(bag, FwTextPropType.ktptBackColor, 0, font.BackColor, Bgr);
                Integer(bag, FwTextPropType.ktptUnderColor, 0, font.UnderlineColor, Bgr);
            }
            bag.StrProps.Remove((int)FwTextPropType.ktptWsStyle);
            if (handle != 0 && bag.TryGetStr((int)FwTextPropType.ktptFontFamily, out var family) && StyleServices.IsMagicFontName(family))
            {
                var definition = _cache.ServiceLocator.WritingSystemManager.Get(handle);
                bag.SetStr((int)FwTextPropType.ktptFontFamily, definition.DefaultFontName);
                if (!bag.TryGetStr((int)FwTextPropType.ktptFontVariations, out var features) || features.Length == 0)
                    bag.SetStr((int)FwTextPropType.ktptFontVariations, definition.DefaultFontFeatures);
            }
            return bag;
        }

        static void String(PropertyBag bag, FwTextPropType type, IStyleProp<string> property)
        {
            if (property.ValueIsSet) bag.SetStr((int)type, property.Value);
        }

        static void Integer<T>(PropertyBag bag, FwTextPropType type, int variation, IStyleProp<T> property, Func<T, int> value)
        {
            if (property.ValueIsSet) bag.SetInt((int)type, value(property.Value), variation);
        }

        static int Bgr(Color color) => color.A == 0 ? unchecked((int)0xC0000000) : color.R | color.G << 8 | color.B << 16;

        void CheckInheritance()
        {
            var complete = new HashSet<Guid>();
            foreach (var style in _cache.LangProject.StylesOC)
            {
                var chain = new HashSet<Guid>();
                for (var current = style; current != null && !complete.Contains(current.Guid); current = current.BasedOnRA)
                    if (!chain.Add(current.Guid)) throw new InvalidOperationException("Style inheritance contains a cycle at " + current.Name + ".");
                complete.UnionWith(chain);
            }
        }
    }
}
