using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;

namespace SIL.TextEditing.LCModel.Tests
{
    /// <summary>A host's handle mapping, as FieldWorks' writing-system factory would give it.</summary>
    sealed class Handles : IWritingSystemHandles
    {
        static readonly string[] Ids = { "", "en", "ar", "fr", "th" };
        public int GetHandle(string wsId) => Math.Max(0, Array.IndexOf(Ids, wsId));
        public string GetId(int handle) => handle > 0 && handle < Ids.Length ? Ids[handle] : string.Empty;
        public static readonly Handles Instance = new Handles();
        public const int En = 1, Ar = 2, Fr = 3;
    }

    static class Fixtures
    {
        // ktpt numbers and values of FieldWorks, written as numbers on purpose: they are facts about the format.
        public const int Bold = 3, Italic = 2, ForeColor = 8, NamedStyle = 133, Ws = 1, ObjData = 6, Rtl = 128;
        public const int VarEnum = 3, VarMilli = 1;
        public const int Off = 0, ForceOn = 1, Invert = 2;

        public static ITsTextProps Props(int ws, Action<ITsPropsBldr>? more = null)
        {
            var b = TsStringUtils.MakePropsBldr();
            b.SetIntPropValues(Ws, 0, ws);
            more?.Invoke(b);
            return b.GetTextProps();
        }

        public static ITsString Build(params (string text, ITsTextProps props)[] runs)
        {
            var b = TsStringUtils.MakeStrBldr();
            foreach (var (text, props) in runs) b.Replace(b.Length, b.Length, text, props);
            return b.GetString();
        }

        public static ITsString Mixed() => Build(
            ("Plain ", Props(Handles.En)),
            ("bold on ", Props(Handles.En, b => b.SetIntPropValues(Bold, VarEnum, ForceOn))),
            ("bold off ", Props(Handles.En, b => b.SetIntPropValues(Bold, VarEnum, Off))),
            ("bold invert ", Props(Handles.En, b => b.SetIntPropValues(Bold, VarEnum, Invert))),
            ("styled ", Props(Handles.Fr, b => b.SetStrPropValue(NamedStyle, "Emphasis"))),
            ("big red", Props(Handles.En, b =>
            {
                b.SetIntPropValues(ForeColor, 0, 0x0000FF);
                b.SetIntPropValues(6, VarMilli, 14000);
                b.SetIntPropValues(2, VarEnum, ForceOn);
            })));

        public static ITsString Arabic() => Build(
            ("\u0645\u0631\u062D\u0628\u0627 ", Props(Handles.Ar, b => b.SetIntPropValues(Rtl, VarEnum, 1))),
            (" hello", Props(Handles.En)));

        public static ITsString Nfd() => Build(("e\u0301cole\u0301 a\u0300", Props(Handles.Fr)));

        public static ITsString Empty() => TsStringUtils.MakeString(string.Empty, Handles.Fr);

        public static readonly Guid PictureGuid = new Guid("0d8cb8a8-52d6-4f0c-9b5b-6e8c9a6b2f10");

        public static ITsString WithPictureAndLink() => Build(
            ("see ", Props(Handles.En)),
            ("\uFFFC", Props(Handles.En, b => b.SetStrPropValue(ObjData, ObjDataOf(PictureGuid, FwObjDataTypes.kodtGuidMoveableObjDisp)))),
            (" and ", Props(Handles.En)),
            ("\uFFFC", Props(Handles.En, b => b.SetStrPropValue(ObjData, "\u0004C:\\data\\notes.pdf"))),
            (" end", Props(Handles.En)));

        static string ObjDataOf(Guid guid, FwObjDataTypes type)
            => Encoding.Unicode.GetString(TsStringUtils.GetObjData(guid, type));

        /// <summary>Everything an ITsString holds, as comparable text: per run its text and every int and string property.</summary>
        public static string Dump(ITsString s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.RunCount; i++)
            {
                s.GetBoundsOfRun(i, out int min, out int lim);
                sb.Append('[').Append(string.Join(" ", (s.GetChars(min, lim) ?? string.Empty).Select(c => ((int)c).ToString("X4")))).Append("] ").Append(Dump(s.get_Properties(i))).Append('\n');
            }
            return sb.ToString();
        }

        public static string Dump(ITsTextProps? p)
        {
            if (p == null) return "null";
            var parts = new List<string>();
            for (int i = 0; i < p.IntPropCount; i++)
            {
                int v = p.GetIntProp(i, out int tpt, out int var);
                parts.Add($"i{tpt}={var}:{v}");
            }
            for (int i = 0; i < p.StrPropCount; i++)
            {
                string v = p.GetStrProp(i, out int tpt);
                parts.Add($"s{tpt}=" + string.Join(".", v.Select(c => ((int)c).ToString("X"))));
            }
            return string.Join(",", parts);
        }
    }
}
