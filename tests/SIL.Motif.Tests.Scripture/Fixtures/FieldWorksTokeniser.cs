using SIL.LCModel.Core.WritingSystems;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.LcmUtils;
using SIL.WritingSystems;

namespace SIL.Motif.Tests.Scripture.Fixtures;

/// <summary>Builds the FieldWorks-rule tokeniser for an in-process writing system that declares the given word-forming characters.</summary>
internal static class FieldWorksTokeniser
{
    static FieldWorksTokeniser() => FwDataProjectLoader.Init();

    public static WritingSystemWordTokeniser Create(params string[] wordForming)
    {
        var ws = new WritingSystemManager().Set("qaa");
        if (wordForming.Length > 0)
        {
            var main = new CharacterSetDefinition("main");
            foreach (var character in wordForming) main.Characters.Add(character);
            ws.CharacterSets.Add(main);
        }
        return new(ws);
    }
}
