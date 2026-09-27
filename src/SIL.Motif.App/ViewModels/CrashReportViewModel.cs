using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// What Motif's error window shows about an error that escaped the UI thread, and its actions for reporting it.
/// </summary>
/// <remarks>
/// Copy, save, and email update <see cref="Status"/>; the problem link opens the shared issues page.
/// </remarks>
public sealed partial class CrashReportViewModel : ObservableObject
{
    /// <summary>The window's heading.</summary>
    public const string Headline = "Motif ran into a problem it could not recover from";

    /// <summary>The plain summary under the heading.</summary>
    public const string Summary =
        "Motif will close when you close this window. To help get the problem fixed, save a report and email it " +
        "to the maintainer.";

    /// <summary>Thanks the person for reporting a beta problem.</summary>
    public const string ThankYou = "Thank you for helping improve Motif.";

    private readonly IClipboard _clipboard;
    private readonly IReportFilePicker _files;
    private readonly IUriLauncher _launcher;
    private readonly string _supportEmail;

    /// <summary>Shows <paramref name="report"/> with actions that go through the given seams.</summary>
    /// <param name="report">The error and what surrounds it.</param>
    /// <param name="clipboard">Where Copy details puts the whole report.</param>
    /// <param name="files">The save dialog Save report writes through.</param>
    /// <param name="launcher">Opens the mail program for Email maintainer.</param>
    /// <param name="supportEmail">The maintainer's address.</param>
    public CrashReportViewModel(
        CrashReport report, IClipboard clipboard, IReportFilePicker files, IUriLauncher launcher, string supportEmail)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentException.ThrowIfNullOrWhiteSpace(supportEmail);
        Report = report;
        _clipboard = clipboard;
        _files = files;
        _launcher = launcher;
        _supportEmail = supportEmail;
    }

    /// <summary>The error the window reports.</summary>
    public CrashReport Report { get; }

    /// <summary>The error's own message.</summary>
    public string Message => Report.Message;

    /// <summary>The error's full account, folded under Details.</summary>
    public string Details => Report.Details;

    /// <summary>How the last action went, or <see langword="null"/> before the first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _status;

    /// <summary>Whether <see cref="Status"/> has anything to say.</summary>
    public bool HasStatus => Status is not null;

    /// <summary>Puts the whole report on the clipboard.</summary>
    public async Task CopyDetailsAsync()
    {
        Status = null;
        try
        {
            await _clipboard.SetTextAsync(Report.ToText()).ConfigureAwait(true);
            Status = "The details are on the clipboard.";
        }
        catch (Exception exception)
        {
            Status = "Motif could not copy the details: " + exception.Message;
        }
    }

    /// <summary>Asks where to save the report and writes it there; a cancelled dialog says nothing.</summary>
    public async Task SaveReportAsync()
    {
        Status = null;
        try
        {
            if (await _files.SaveReportAsync(Report.SuggestedFileName, Report.ToText()).ConfigureAwait(true))
                Status = "The report is saved. Attach it to your email.";
        }
        catch (Exception exception)
        {
            Status = "Motif could not save the report: " + exception.Message;
        }
    }

    /// <summary>Opens the mail program with a short message to the maintainer.</summary>
    public async Task EmailMaintainerAsync()
    {
        Status = null;
        var opened = false;
        try
        {
            opened = await _launcher.LaunchAsync(CrashReportEmail.MailtoFor(Report, _supportEmail)).ConfigureAwait(true);
        }
        catch (Exception)
        {
            // A launcher that throws has opened nothing, which the sentence below already says.
        }
        Status = opened
            ? "Your email program should open. Attach the report you saved."
            : $"Motif could not open an email program. Write to {_supportEmail} and attach the saved report.";
    }

    /// <summary>Opens the Motif issue page.</summary>
    [RelayCommand]
    private async Task ReportProblemAsync() =>
        await _launcher.LaunchAsync(new Uri(AppLinks.Issues)).ConfigureAwait(true);
}
