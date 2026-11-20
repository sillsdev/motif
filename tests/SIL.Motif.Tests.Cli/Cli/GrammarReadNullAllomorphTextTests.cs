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
        return GrammarReadTextRenderer.Render(new GrammarReadResponse(GrammarReadKind.lexical_entry, 1, 0, 10, [entry]));
    }

    [Fact]
    public void ANullAffixAllomorphIsShownAsANullAllomorph() =>
        Assert.Contains("Lexeme form: ∅ (null allomorph) (qaa)", Render("∅", "MoAffixAllomorph"), StringComparison.Ordinal);

    [Fact]
    public void AStemFormOfTheSameTextIsShownPlainly() =>
        Assert.DoesNotContain("null allomorph", Render("∅", "MoStemAllomorph"), StringComparison.Ordinal);
}
