using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Xunit;

namespace SIL.Motif.Tests.App.TextViewParity;

/// <summary>
/// Left-to-right and right-to-left text: the order of words on a line, how a line wraps, how the morpheme columns
/// inside a word run, and what happens to left-to-right glosses under right-to-left forms. All assertions are on
/// bounds, never pixels.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class DirectionTests(LineScenes lines, CardScenes cards) : IClassFixture<LineScenes>, IClassFixture<CardScenes>
{
    private const double Epsilon = 1.0;

    private static void Run(Action work) => AvaloniaHeadlessPlatform.Invoke(work);

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void WordsOnALineRunInTheTextsDirection(string name) => Run(() =>
    {
        var scene = lines.Get(name);
        var rows = Rows(scene);
        Assert.True(rows[0].Count >= 3, "The scene should fit three words on its first row.");
        var first = rows[0];
        for (var i = 1; i < first.Count; i++)
        {
            var (before, after) = (scene.WordBounds(first[i - 1]), scene.WordBounds(first[i]));
            if (scene.Sample.RightToLeft) Assert.True(after.Right <= before.Left + Epsilon, $"Word {first[i]} is not left of word {first[i - 1]}.");
            else Assert.True(after.Left >= before.Right - Epsilon, $"Word {first[i]} is not right of word {first[i - 1]}.");
        }
    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void AWrappedLineContinuesOnTheNextRowFromTheReadingStartEdge(string name) => Run(() =>
    {
        var scene = lines.Get(name);
        var rows = Rows(scene);
        Assert.True(rows.Count >= 2, "The fourth word should wrap onto a second row.");
        Assert.Equal(Enumerable.Range(0, 4), rows.SelectMany(row => row));
        var (firstOfFirstRow, firstOfSecondRow) = (scene.WordBounds(rows[0][0]), scene.WordBounds(rows[1][0]));
        Assert.True(firstOfSecondRow.Top >= firstOfFirstRow.Bottom - Epsilon);
        if (scene.Sample.RightToLeft) Assert.Equal(firstOfFirstRow.Right, firstOfSecondRow.Right, 1.0);
        else Assert.Equal(firstOfFirstRow.Left, firstOfSecondRow.Left, 1.0);
    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void TheGutterSitsAtTheReadingStartSideOfTheWords(string name) => Run(() =>
    {
        var scene = lines.Get(name);
        var gutter = scene.Bounds(scene.GutterLabels()[0]);
        var all = Enumerable.Range(0, 4).Select(scene.WordBounds).ToArray();
        if (scene.Sample.RightToLeft) Assert.True(gutter.Left >= all.Max(b => b.Right) - Epsilon);
        else Assert.True(gutter.Right <= all.Min(b => b.Left) + Epsilon);
    });

    [Fact]
    public void LeftToRightMorphemeColumnsRunLeftToRightInBothProducers() => Run(() =>
    {
        var scene = lines.Get("ltr");
        for (var word = 0; word < 4; word++)
            foreach (var part in new[] { Part.FieldWorksForm, Part.PanGlossForm, Part.FieldWorksGloss, Part.PanGlossGloss })
                AssertOrder(scene, word, part, rightToLeft: false);
    });

    [Fact(Skip = "Fails today: a reader word strip is hosted in an items container that does not inherit the line's " +
        "right-to-left FlowDirection, so words wrap right-to-left but the morpheme columns inside each word still run " +
        "left-to-right (morpheme 0 is leftmost).")]
    public void RightToLeftMorphemeColumnsMirrorInBothProducers() => Run(() =>
    {
        var scene = lines.Get("rtl");
        for (var word = 0; word < 4; word++)
            foreach (var part in new[] { Part.FieldWorksForm, Part.PanGlossForm, Part.FieldWorksGloss, Part.PanGlossGloss })
                AssertOrder(scene, word, part, rightToLeft: true);
    });

    [Fact(Skip = "Fails today: the word card is not mirrored for right-to-left text, so its morpheme columns run left-to-right.")]
    public void RightToLeftMorphemeColumnsMirrorInTheCard() => Run(() =>
    {
        var scene = cards.Get("rtl");
        var forms = scene.CardConstituents().Where(c => c.Part == Part.CardForm).Take(3).ToArray();
        for (var i = 1; i < forms.Length; i++)
            Assert.True(scene.Bounds(forms[i].Visual).Right <= scene.Bounds(forms[i - 1].Visual).Left + Epsilon);
    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void EachScriptKeepsItsOwnDirectionAndGlossesSitInTheirFormsColumn(string name) => Run(() =>
    {
        var scene = lines.Get(name);
        var expectedForms = scene.Sample.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        var constituents = scene.Constituents(0);
        Assert.Equal(expectedForms, scene.Constituent(0, Part.Word).Visual.FlowDirection);
        foreach (var c in constituents.Where(c => c.Part is Part.FieldWorksForm or Part.PanGlossForm))
            Assert.Equal(expectedForms, c.Visual.FlowDirection);
        // Glosses are English in both samples: left-to-right even under right-to-left forms.
        foreach (var c in constituents.Where(c => c.Part is Part.FieldWorksGloss or Part.PanGlossGloss))
            Assert.Equal(FlowDirection.LeftToRight, c.Visual.FlowDirection);
        foreach (var (form, gloss) in new[] { (Part.FieldWorksForm, Part.FieldWorksGloss), (Part.PanGlossForm, Part.PanGlossGloss) })
            foreach (var morph in Enumerable.Range(0, scene.Constituents(0).Count(c => c.Part == form)))
            {
                var (f, g) = (scene.Bounds(scene.Constituent(0, form, morph).Visual), scene.Bounds(scene.Constituent(0, gloss, morph).Visual));
                Assert.True(g.Top >= f.Bottom - Epsilon, "A gloss belongs under its form.");
                Assert.True(Math.Abs(f.Center.X - g.Center.X) <= Epsilon + Math.Abs(f.Width - g.Width) / 2,
                    $"The gloss of morpheme {morph} is not in its form's column.");
            }
    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void AFormWithCombiningMarksNeverWrapsBetweenALetterAndItsMark(string name) => Run(() =>
    {
        var scene = cards.Get(name);
        try
        {
            scene.Resize(150);
            var form = scene.CardConstituents().Single(c => c.Text == scene.Sample.LongForm);
            var layout = form.Visual.TextLayout;
            Assert.True(layout.TextLines.Count > 1, "The long form should wrap in a narrow card.");
            foreach (var line in layout.TextLines.Skip(1))
            {
                var firstCharacter = form.Text[line.FirstTextSourceIndex];
                Assert.NotEqual(UnicodeCategory.NonSpacingMark, CharUnicodeInfo.GetUnicodeCategory(firstCharacter));
            }
        }
        finally { scene.Resize(700); }
    });

    /// <summary>Word indexes per visual row, top to bottom, each row in layout order.</summary>
    private static List<List<int>> Rows(TextViewScene scene)
    {
        var rows = new List<List<int>>();
        foreach (var index in Enumerable.Range(0, 4).OrderBy(i => scene.WordBounds(i).Top).ThenBy(i => i))
        {
            if (rows.Count > 0 && Math.Abs(scene.WordBounds(rows[^1][0]).Top - scene.WordBounds(index).Top) < Epsilon)
                rows[^1].Add(index);
            else rows.Add([index]);
        }
        return rows;
    }

    private static void AssertOrder(TextViewScene scene, int word, Part part, bool rightToLeft)
    {
        var parts = scene.Constituents(word).Where(c => c.Part == part).ToArray();
        for (var i = 1; i < parts.Length; i++)
        {
            var (before, after) = (scene.Bounds(parts[i - 1].Visual), scene.Bounds(parts[i].Visual));
            if (rightToLeft) Assert.True(after.Right <= before.Left + Epsilon, $"Word {word} {part} {i} is not left of {i - 1}.");
            else Assert.True(after.Left >= before.Right - Epsilon, $"Word {word} {part} {i} is not right of {i - 1}.");
        }
    }
}
