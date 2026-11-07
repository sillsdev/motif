using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>One Request nobody has answered yet, as the Proposals page lists it.</summary>
public sealed class OpenRequestRowViewModel
{
    public OpenRequestRowViewModel(RequestItem request, DateTimeOffset now)
    {
        Request = request;
        WithdrawAutomationId = AutomationIds.ForRequestWithdraw(request.RequestId);
        TargetText = string.Format(CultureInfo.CurrentCulture, "{0}: {1}", KindText(request.TargetKind),
            request.TargetLabel);
        NoteText = string.IsNullOrWhiteSpace(request.Note) ? string.Empty : "“" + request.Note.Trim() + "”";
        AgeText = AgeOf(request.CreatedUtc, now);
    }

    public RequestItem Request { get; }

    public string RequestId => Request.RequestId;

    /// <summary>What was asked about, such as "Word: kita".</summary>
    public string TargetText { get; }

    /// <summary>The person's note in quotation marks, or empty when there is none.</summary>
    public string NoteText { get; }

    public bool HasNote => NoteText.Length > 0;

    /// <summary>How long ago the Request was made.</summary>
    public string AgeText { get; }

    public string WithdrawAutomationId { get; }

    private static string KindText(string kind) => kind switch
    {
        "word" => MotifAppStrings.RequestKindWord,
        "warning" => MotifAppStrings.RequestKindWarning,
        _ => MotifAppStrings.RequestKindMatrixCell,
    };

    private static string AgeOf(string createdUtc, DateTimeOffset now)
    {
        if (!DateTimeOffset.TryParse(createdUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal,
                out var created))
            return string.Empty;
        var days = (int)Math.Max(0, (now - created).TotalDays);
        return days == 0 ? MotifAppStrings.RequestAskedToday
            : PluralText.Format(MotifAppStrings.RequestAskedDaysPattern, days);
    }
}

/// <summary>The Proposals page's group "Asked, waiting for a Proposal": the open Requests, oldest first.</summary>
public sealed partial class OpenRequestsViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRequests))]
    private IReadOnlyList<OpenRequestRowViewModel> _rows = [];

    [ObservableProperty]
    private bool _hasMore;

    /// <summary>The group's heading.</summary>
    public string Title => MotifAppStrings.RequestsGroupTitle;

    /// <summary>Whether any Request is open; the group is hidden otherwise.</summary>
    public bool HasRequests => Rows.Count > 0;

    /// <summary>Shows the open Requests from a read.</summary>
    public void Show(IReadOnlyList<RequestItem> requests, DateTimeOffset now, bool append = false, int total = 0)
    {
        var rows = requests.Where(request => request.Status == "open")
            .Select(request => new OpenRequestRowViewModel(request, now));
        Rows = append ? [.. Rows, .. rows] : [.. rows];
        HasMore = Rows.Count < total;
    }
}
