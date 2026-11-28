using System;
using System.Collections.Generic;
using SIL.Motif.Cli.Rendering;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class GrammarReadNullAllomorphTextTests
{
    private static string Render(string form, string kind)
    {
        var lexeme = new GrammarFormRead("form/one", form, "qaa", kind, "suffix", "type/one", []);
        var entry = new GrammarLexicalEntryRead("entry/one", form, "suffix", "type/one", lexeme, [], []);
        return GrammarReadTextRenderer.Render(new GrammarReadResponse(GrammarReadKind.lexical_entry, 1, 10, [entry]));
    }

    [Fact]
    public void ANullAffixAllomorphIsShownAsANullAllomorph() =>
        Assert.Contains("Lexeme form: ∅ (null allomorph) (qaa)", Render("∅", "MoAffixAllomorph"), StringComparison.Ordinal);

    [Fact]
    public void AStemFormOfTheSameTextIsShownPlainly() =>
        Assert.DoesNotContain("null allomorph", Render("∅", "MoStemAllomorph"), StringComparison.Ordinal);
}

public sealed class GrammarReadAffixTemplateTextTests
{
    [Fact]
    public void ATemplateRendersItsSlotsInOrderWithTheirAffixes()
    {
        var affix = new GrammarSlotAffixRead("entry/one", "ni-", "prefix", "msa/one", null);
        var template = new GrammarAffixTemplateRead("tpl/one", "VerbFinite", null, true,
            [new GrammarAffixSlotRead("slot/one", "Subject", false, [affix])],
            [new GrammarAffixSlotRead("slot/two", "Tense", true, [])]);

        var text = GrammarReadTextRenderer.Render(new GrammarReadResponse(GrammarReadKind.affix_template, 1, 10, [template]));

        Assert.Contains("VerbFinite  tpl/one  final", text, StringComparison.Ordinal);
        Assert.True(text.IndexOf("Prefix slot: Subject", StringComparison.Ordinal) < text.IndexOf("Suffix slot: Tense", StringComparison.Ordinal));
        Assert.Contains("Affix: ni-  entry/one  msa msa/one", text, StringComparison.Ordinal);
    }
}
