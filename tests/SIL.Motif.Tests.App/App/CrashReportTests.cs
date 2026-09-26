using SIL.Motif.App.Services;
using SIL.Motif.Host;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins what the error window saves and what its email carries, from a synthetic nested exception, so the
/// saved report holds the whole trace and the mail program is handed only a short note.
/// </summary>
public sealed class CrashReportTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 9, 26, 14, 5, 9, TimeSpan.Zero);

    [Fact]
    public void TheSavedReportHoldsTheVersionTimePlatformAndTheWholeExceptionIncludingItsInnerOne()
    {
        var failure = NestedFailure();
        var report = Report(failure);

        var text = report.ToText();

        Assert.StartsWith("Motif error report\r\n", text, StringComparison.Ordinal);
        Assert.Contains("Motif version: 0.1.0\r\n", text, StringComparison.Ordinal);
        Assert.Contains("Time (UTC): 2026-09-26 14:05:09\r\n", text, StringComparison.Ordinal);
        Assert.Contains("Operating system: Test OS 1.0\r\n", text, StringComparison.Ordinal);
        Assert.Contains(".NET: Test .NET 10\r\n", text, StringComparison.Ordinal);
        Assert.EndsWith(failure.ToString() + "\r\n", text, StringComparison.Ordinal);
        Assert.Contains("the store was closed", text, StringComparison.Ordinal);
        Assert.Contains(nameof(NestedFailure), text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDetailsAreTheExceptionsOwnAccountAndTheMessageIsItsOuterMessage()
    {
        var failure = NestedFailure();
        var report = Report(failure);

        Assert.Equal(failure.ToString(), report.Details);
        Assert.Equal("Refreshing the texts failed.", report.Message);
    }

    [Fact]
    public void TheSuggestedFileNameIsATextFileNamedForWhenTheErrorHappened()
    {
        Assert.Equal("motif-error-20260926-140509.txt", Report(NestedFailure()).SuggestedFileName);
    }

    [Fact]
    public void TheEmailIsAddressedToTheMaintainerWithAShortSubject()
    {
        var mail = CrashReportEmail.MailtoFor(Report(NestedFailure()), MotifSupport.SupportEmail);

        Assert.Equal("mailto", mail.Scheme);
        Assert.StartsWith("mailto:john_lambert@sil.org?subject=Motif%200.1.0%20error%20report&body=", mail.AbsoluteUri,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheEmailBodyNamesTheErrorAndAsksForTheSavedReportButCarriesNoTrace()
    {
        var failure = NestedFailure();

        var body = BodyOf(CrashReportEmail.MailtoFor(Report(failure), MotifSupport.SupportEmail));

        Assert.Contains("Motif 0.1.0 closed after an error: InvalidOperationException: Refreshing the texts failed.",
            body, StringComparison.Ordinal);
        Assert.Contains("Please attach the report you saved from Motif's error window.", body, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", body, StringComparison.Ordinal);
        Assert.DoesNotContain("the store was closed", body, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(NestedFailure), body, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEmailPercentEncodesLineBreaksAndReservedCharacters()
    {
        var failure = new InvalidOperationException("a&b=c?d #e\r\nsecond line");

        var mail = CrashReportEmail.MailtoFor(Report(failure), MotifSupport.SupportEmail);

        var query = mail.AbsoluteUri[(mail.AbsoluteUri.IndexOf('?') + 1)..];
        Assert.Equal(2, query.Split('&').Length);
        Assert.Contains("%0D%0A", query, StringComparison.Ordinal);
        Assert.DoesNotContain("#", query, StringComparison.Ordinal);
        Assert.Contains("a&b=c?d #e second line", BodyOf(mail), StringComparison.Ordinal);
    }

    [Fact]
    public void ALongMessageIsCutShortInTheEmailSoTheLinkStaysShort()
    {
        var failure = new InvalidOperationException(new string('x', 5000));

        var mail = CrashReportEmail.MailtoFor(Report(failure), MotifSupport.SupportEmail);

        Assert.True(mail.AbsoluteUri.Length < 1000, $"The mail link is {mail.AbsoluteUri.Length} characters long.");
        Assert.Contains(new string('x', CrashReportEmail.MessageLimit) + "…", BodyOf(mail), StringComparison.Ordinal);
    }

    private static CrashReport Report(Exception failure) =>
        new(failure, OccurredAt, "0.1.0", "Test OS 1.0", "Test .NET 10");

    private static string BodyOf(Uri mail)
    {
        var uri = mail.AbsoluteUri;
        var start = uri.IndexOf("&body=", StringComparison.Ordinal) + "&body=".Length;
        return Uri.UnescapeDataString(uri[start..]);
    }

    // Thrown for real so each level carries a stack trace, as an escaped error does.
    private static Exception NestedFailure()
    {
        try
        {
            try
            {
                throw new IOException("the store was closed");
            }
            catch (IOException inner)
            {
                throw new InvalidOperationException("Refreshing the texts failed.", inner);
            }
        }
        catch (InvalidOperationException outer)
        {
            return outer;
        }
    }
}
