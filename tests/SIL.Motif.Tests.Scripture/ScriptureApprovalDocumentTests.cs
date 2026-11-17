using SIL.Motif.Runner.Operations;
using Xunit;

namespace SIL.Motif.Tests.Scripture;

public sealed class ScriptureApprovalDocumentTests
{
    private static readonly Guid A = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid B = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void ADocumentKeepsLocationsInNumericOrderAndAnOccurrenceHasAtMostOneAnalysis()
    {
        var document = new ScriptureApprovalDocument();
        document.Set("MAT 5:10#1", new(B, "v10", "w", "s"));
        document.Set("MAT 5:3#10", new(A, "v3", "x", "s"));
        document.Set("MAT 5:3#2", new(A, "v3", "lamp", "s"));
        document.Set("MAT 5:3#2", new(B, "v3", "lamp", "s"));

        Assert.Equal(["MAT 5:3#2", "MAT 5:3#10", "MAT 5:10#1"], document.Approvals.Keys);
        Assert.Equal(B, document.Approvals["MAT 5:3#2"].Analysis);
        Assert.True(document.Remove("MAT 5:3#2"));
        Assert.False(document.Remove("MAT 5:3#2"));
    }

    private static readonly Guid Source = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Ours = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Theirs = Guid.Parse("55555555-5555-5555-5555-555555555555");

    [Fact]
    public void ABlobRoundTripsInLocationOrderWithClearsAndSettleRecords()
    {
        var blob = new ScriptureApprovalBlob(Source, "MAT 5", Ours);
        blob.Set(new("MAT 5:10#1", null, "0000000a", [new(Theirs, "0000000b")]));
        blob.Set(new("MAT 5:3#2", new(A, "v3", "lamp", "s"), "0000000c", []));

        var text = blob.Serialize();

        Assert.Equal("motif-approvals\t1\nsource\t33333333-3333-3333-3333-333333333333\npassage\tMAT 5\n" +
            "installation\t44444444-4444-4444-4444-444444444444\n" +
            "MAT 5:3#2\t11111111-1111-1111-1111-111111111111\tv3\tlamp\ts\t0000000c\t\n" +
            "MAT 5:10#1\t\t\t\t\t0000000a\t55555555555555555555555555555555:0000000b", text);
        var read = ScriptureApprovalBlob.Parse(text);
        Assert.Equal((Source, "MAT 5", Ours), (read.Source, read.Passage, read.Installation));
        Assert.Equal(blob.Entries.Keys, read.Entries.Keys);
        Assert.All(blob.Entries, pair => Assert.True(pair.Value.SaysTheSame(read.Entries[pair.Key])));
        Assert.Equal(text, read.Serialize());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not approvals")]
    [InlineData("motif-approvals\t2\nsource\t33333333-3333-3333-3333-333333333333\npassage\tMAT 5\ninstallation\t44444444-4444-4444-4444-444444444444")]
    [InlineData("motif-approvals\t1\nsource\tnope\npassage\tMAT 5\ninstallation\t44444444-4444-4444-4444-444444444444")]
    [InlineData("motif-approvals\t1\nsource\t33333333-3333-3333-3333-333333333333\npassage\tMAT\ninstallation\t44444444-4444-4444-4444-444444444444")]
    [InlineData("motif-approvals\t1\nsource\t33333333-3333-3333-3333-333333333333\npassage\tMAT 5\ninstallation\t44444444-4444-4444-4444-444444444444\nMAT 6:1#1\t\t\t\t\tx\t")]
    [InlineData("motif-approvals\t1\nsource\t33333333-3333-3333-3333-333333333333\npassage\tMAT 5\ninstallation\t44444444-4444-4444-4444-444444444444\nMAT 5:1#1\tnope\tv\tw\ts\tx\t")]
    [InlineData("motif-approvals\t1\nsource\t33333333-3333-3333-3333-333333333333\npassage\tMAT 5\ninstallation\t44444444-4444-4444-4444-444444444444\nMAT 5:1#1\t\t\t\t\t\t")]
    [InlineData("motif-approvals\t1\nsource\t33333333-3333-3333-3333-333333333333\npassage\tMAT 5\ninstallation\t44444444-4444-4444-4444-444444444444\nMAT 5:1#1\t\t\t\t\tx\tbad")]
    public void TextThatIsNotABlobIsAFormatErrorNeverNothing(string? text) =>
        Assert.Throws<FormatException>(() => ScriptureApprovalBlob.Parse(text));

    [Fact]
    public void AnEntryNamedByAnySettleRecordAtItsOccurrenceNoLongerStands()
    {
        var ours = new ScriptureApprovalBlob(Source, "MAT 5", Ours);
        var theirs = new ScriptureApprovalBlob(Source, "MAT 5", Theirs);
        theirs.Set(new("MAT 5:3#2", new(A, "v", "w", "s"), "t1", []));
        ours.Set(new("MAT 5:3#2", new(B, "v", "w", "s"), "o1", []));

        Assert.Equal(2, ScriptureApprovalBlob.Standing([ours, theirs], "MAT 5:3#2").Count);

        ours.Set(new("MAT 5:3#2", new(B, "v", "w", "s"), "o2", [new(Theirs, "t1")]));
        Assert.Equal(Ours, Assert.Single(ScriptureApprovalBlob.Standing([ours, theirs], "MAT 5:3#2")).Installation);

        // A later approval by the overruled installation has a new stamp, so it stands again beside the settled one.
        theirs.Set(new("MAT 5:3#2", new(A, "v", "w", "s"), "t2", []));
        Assert.Equal(2, ScriptureApprovalBlob.Standing([ours, theirs], "MAT 5:3#2").Count);
        Assert.Empty(ScriptureApprovalBlob.Standing([ours, theirs], "MAT 5:4#1"));
    }

    [Theory]
    [InlineData("MAT 5:3#2", true)]
    [InlineData("1CO 13:4#10", true)]
    [InlineData("MAT 5:3", false)]
    [InlineData("mat 5:3#2", false)]
    [InlineData("MAT 5:3#x", false)]
    [InlineData(null, false)]
    public void AnchorsHaveOneShape(string? anchor, bool expected) =>
        Assert.Equal(expected, ScriptureApprovalDocument.IsAnchor(anchor));

    [Fact]
    public void PassageAndVerseAreReadFromTheLocation()
    {
        Assert.Equal("MAT 5", ScriptureApprovalDocument.PassageOf("MAT 5:3#2"));
        Assert.Equal("MAT 5:3", ScriptureApprovalDocument.VerseOf("MAT 5:3#2"));
        Assert.Throws<FormatException>(() => ScriptureApprovalDocument.PassageOf("MAT 5"));
    }

    [Fact]
    public void AnApprovalsItemGuidIsStableAndDiffersBySourcePassageAndInstallation()
    {
        var item = ScriptureApprovalFields.ItemGuid(Source, "MAT 5", Ours);

        Assert.Equal(item, ScriptureApprovalFields.ItemGuid(Source, "MAT 5", Ours));
        Assert.NotEqual(item, ScriptureApprovalFields.ItemGuid(Source, "MAT 5", Theirs));
        Assert.NotEqual(item, ScriptureApprovalFields.ItemGuid(Source, "MAT 6", Ours));
        Assert.NotEqual(item, ScriptureApprovalFields.ItemGuid(Guid.NewGuid(), "MAT 5", Ours));
    }
}
