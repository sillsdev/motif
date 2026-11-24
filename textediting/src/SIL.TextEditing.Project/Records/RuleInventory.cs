using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SIL.LCModel;

namespace SIL.TextEditing.Project
{
    /// <summary>The phonemes and boundary markers of the project's first phoneme set, by code.</summary>
    internal sealed class RuleInventory
    {
        readonly int[] _writingSystems;
        readonly Dictionary<Guid, IPhTerminalUnit> _units = new Dictionary<Guid, IPhTerminalUnit>();
        readonly Dictionary<string, List<IPhTerminalUnit>> _byCode = new Dictionary<string, List<IPhTerminalUnit>>(StringComparer.Ordinal);

        internal RuleInventory(LcmCache cache)
        {
            var handles = new List<int> { cache.DefaultVernWs };
            handles.AddRange(cache.ServiceLocator.WritingSystems.CurrentVernacularWritingSystems.Select(w => w.Handle));
            _writingSystems = handles.Distinct().ToArray();
            var set = cache.LangProject.PhonologicalDataOA?.PhonemeSetsOS.FirstOrDefault();
            if (set == null) return;
            foreach (IPhTerminalUnit unit in set.PhonemesOC.Cast<IPhTerminalUnit>().Concat(set.BoundaryMarkersOC))
            {
                _units[unit.Guid] = unit;
                if (IsStandard(unit)) continue;
                foreach (string code in Codes(unit).Distinct())
                {
                    if (!_byCode.TryGetValue(code, out var list)) _byCode[code] = list = new List<IPhTerminalUnit>();
                    list.Add(unit);
                }
            }
        }

        internal static bool IsWordBoundary(IPhTerminalUnit unit) => unit.Guid == LangProjectTags.kguidPhRuleWordBdry;

        internal static bool IsStandard(IPhTerminalUnit unit) => unit.Guid == LangProjectTags.kguidPhRuleWordBdry || unit.Guid == LangProjectTags.kguidPhRuleMorphBdry;

        internal bool Contains(IPhTerminalUnit unit) => _units.ContainsKey(unit.Guid);

        IEnumerable<string> Codes(IPhTerminalUnit unit)
        {
            foreach (var code in unit.CodesOS)
                foreach (int ws in _writingSystems)
                {
                    string text = code.Representation.get_String(ws)?.Text ?? string.Empty;
                    if (text.Trim().Length > 0) yield return text.Normalize(NormalizationForm.FormC);
                }
        }

        /// <summary>The code the text writes: the first code that has a non-blank string in the first writing system that has one.</summary>
        internal string? Written(IPhTerminalUnit unit)
        {
            foreach (var code in unit.CodesOS)
                foreach (int ws in _writingSystems)
                {
                    string text = code.Representation.get_String(ws)?.Text ?? string.Empty;
                    if (text.Trim().Length > 0) return text.Normalize(NormalizationForm.FormC);
                }
            return null;
        }

        /// <summary>The ordinary (not standard-boundary) units any of whose codes is <paramref name="code"/>.</summary>
        internal IReadOnlyList<IPhTerminalUnit> Lookup(string code) =>
            _byCode.TryGetValue(code.Normalize(NormalizationForm.FormC), out var list) ? list : (IReadOnlyList<IPhTerminalUnit>)Array.Empty<IPhTerminalUnit>();

        /// <summary>The first-set unit with a standard marker's guid, or null.</summary>
        internal IPhTerminalUnit? Standard(Guid guid) => _units.TryGetValue(guid, out var unit) ? unit : null;
    }
}
