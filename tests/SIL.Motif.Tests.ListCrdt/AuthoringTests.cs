using SIL.ListCrdt;
using Xunit;

namespace SIL.ListCrdt.Tests;

public class AuthoringTests
{
    [Fact]
    public void SameIndexMoveAuthorsNothingAndDoesNotCompeteWithConcurrentInsert()
    {
        var state = ListState.FromInitial(["first", "second"]);
        var local = new ListReplica("a", state);
        var remote = new ListReplica("b", state.Clone());
        var inserted = remote.InsertAt(0, "new");
        int count = state.AppliedOperations.Count;

        Assert.Null(local.MoveToIndex("first", 0));
        Assert.Equal(count, state.AppliedOperations.Count);
        local.Receive(inserted);
        Assert.Equal(["new", "first", "second"], state.Elements());
    }
}
