using System;
using System.IO;
using Microsoft.Data.Sqlite;
using SIL.Motif.Commands;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection("BusyRetryBackoff")]
public sealed class BusyRetryTests : IDisposable
{
    private readonly int[] _saved = BusyRetry.BackoffMilliseconds;

    public BusyRetryTests() => BusyRetry.BackoffMilliseconds = [1, 1, 1];

    public void Dispose() => BusyRetry.BackoffMilliseconds = _saved;

    [Fact]
    public void ALockThatClearsWithinTheBoundLetsTheStepFinish()
    {
        var calls = 0;
        BusyRetry.Run(() => { if (++calls < 3) throw new SqliteException("database is locked", 5); });
        Assert.Equal(3, calls);
    }

    [Fact]
    public void ALockWrappedInAnIoExceptionIsRetriedToo()
    {
        var calls = 0;
        BusyRetry.Run(() =>
        {
            if (++calls < 2) throw new IOException("store", new SqliteException("database is locked", 5));
        });
        Assert.Equal(2, calls);
    }

    [Fact]
    public void ALockThatOutlastsTheBoundIsRethrownAfterTheRetries()
    {
        var calls = 0;
        Assert.Throws<SqliteException>(() =>
            BusyRetry.Run(() => { calls++; throw new SqliteException("database is locked", 5); }));
        Assert.Equal(4, calls);
    }

    [Fact]
    public void AnotherFailureIsNotRetried()
    {
        var calls = 0;
        Assert.Throws<IOException>(() => BusyRetry.Run(() => { calls++; throw new IOException("disk"); }));
        Assert.Equal(1, calls);
    }
}
