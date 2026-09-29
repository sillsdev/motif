using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;

namespace SIL.Motif.App.Controls;

/// <summary>Identifies the FieldWorks judgement or absence shown by an <see cref="OpinionMark"/>.</summary>
public enum OpinionMarkKind
{
    /// <summary>An analysis a person has approved.</summary>
    Approved,

    /// <summary>An analysis a person has disapproved.</summary>
    Disapproved,

    /// <summary>An analysis with no human judgement.</summary>
    Unknown,

    /// <summary>An analysis that FieldWorks does not hold.</summary>
    None,
}

/// <summary>Shows an opinion as its letter and shape, with a spoken name for assistive technology.</summary>
public sealed class OpinionMark : Border
{
    /// <summary>Creates the letter, shape and accessible name for one opinion mark.</summary>
    public OpinionMark(OpinionMarkKind kind)
    {
        Kind = kind;
        var (shape, letter, name) = kind switch
        {
            OpinionMarkKind.Approved => ("approved", (char?)'A', "Approved"),
            OpinionMarkKind.Disapproved => ("disapproved", (char?)'D', "Disapproved"),
            OpinionMarkKind.Unknown => ("unknown", (char?)'U', "Unknown"),
            OpinionMarkKind.None => ("none", (char?)null, "Not in FieldWorks"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        Letter = letter;

        Classes.Add("opinionMark");
        Classes.Add(shape);
        Child = letter is { } value
            ? new TextBlock { Text = value.ToString() }
            : new Grid { Children = { new Rectangle { Classes = { "opinionDash" } } } };
        AutomationProperties.SetName(this, name);
    }

    /// <summary>The judgement represented by this mark.</summary>
    public OpinionMarkKind Kind { get; }

    /// <summary>The displayed letter, or <see langword="null"/> for the dashed absence mark.</summary>
    public char? Letter { get; }
}
