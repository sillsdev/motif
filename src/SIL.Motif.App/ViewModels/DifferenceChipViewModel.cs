using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SIL.Motif.App.ViewModels;

/// <summary>What a Difference chip stands for.</summary>
public enum DifferenceChipKind
{
    /// <summary>Every word that may have got worse.</summary>
    CheckOnly,

    /// <summary>One category of change.</summary>
    Bucket,

    /// <summary>One before and after approval-state pair.</summary>
    Moved,

    /// <summary>One search limit.</summary>
    Limitation,

    /// <summary>One word of the list.</summary>
    Word,
}

/// <summary>One chip of a saved Difference: a label, how many words it holds, and whether it is the one in force.</summary>
public sealed partial class DifferenceChipViewModel : ObservableObject
{
    public DifferenceChipViewModel(DifferenceChipKind kind, string label, int count, string code, bool canChoose,
        Action<DifferenceChipViewModel> chosen)
    {
        ArgumentNullException.ThrowIfNull(chosen);
        Kind = kind;
        Label = label;
        Count = count;
        Code = code;
        CanChoose = canChoose;
        ChooseCommand = new RelayCommand(() => chosen(this));
    }

    /// <summary>What the chip stands for.</summary>
    public DifferenceChipKind Kind { get; }

    /// <summary>The chip's words.</summary>
    public string Label { get; }

    /// <summary>How many words the chip holds, or how many places a word has in the Text.</summary>
    public int Count { get; }

    /// <summary>The code the chip sends to the command, or the word itself for a word chip.</summary>
    public string Code { get; }

    /// <summary>Whether the chip filters; it does not once the Proposal was decided and only the summary is left.</summary>
    public bool CanChoose { get; }

    /// <summary>Whether this chip is the one in force.</summary>
    [ObservableProperty]
    private bool _isActive;

    /// <summary>Chooses the chip, or lifts it when it is already in force.</summary>
    public ICommand ChooseCommand { get; }
}
