using CommunityToolkit.Mvvm.ComponentModel;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// One Text a person can add to a Selection. <see cref="Id"/> is the Text's own GUID and the only thing
/// <see cref="SelectionViewModel"/> composes into a <c>SelectionRequest</c> — never <see cref="Title"/>,
/// which is display text only (AGENTS.md rule 12).
/// </summary>
public sealed partial class TextChoiceViewModel : ObservableObject
{
    public TextChoiceViewModel(Guid id, string title)
    {
        Id = id;
        Title = title;
    }

    public Guid Id { get; }

    public string Title { get; }

    [ObservableProperty]
    private bool _isChecked;
}
